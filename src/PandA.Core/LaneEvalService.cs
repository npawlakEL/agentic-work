using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PandA.Core;

/// <summary>
/// Lane evaluation, ported from <c>sdisp_PA_LaneEval</c> (architecture-log 012). For each apply
/// orientation on the line it maintains exactly <see cref="PrinterGroupPolicy.OnlineMin"/> usable
/// (online, non-spare) printers: parking surplus online printers as spares, promoting a spare back
/// into rotation when an active is lost, and — when it can neither meet the minimum nor promote a
/// spare — deciding whether to run the line slow (degraded) or shut it down.
/// <para>
/// The service mutates <see cref="PrinterState.IsSpare"/> / <see cref="PrinterState.LastStatusUpdate"/>
/// directly and returns a <see cref="LaneEvalResult"/> describing the line-control decision and the
/// spare changes it applied. The real BluePaw slow/shut egress is a separate, stubbed concern.
/// </para>
/// </summary>
public sealed class LaneEvalService
{
    private readonly ILogger<LaneEvalService> _logger;

    public LaneEvalService(ILogger<LaneEvalService>? logger = null)
    {
        _logger = logger ?? NullLogger<LaneEvalService>.Instance;
    }

    /// <summary>
    /// Evaluate one line. <paramref name="now"/> stamps <see cref="PrinterState.LastStatusUpdate"/>
    /// on any promote/demote so the newest change is preferred next time.
    /// </summary>
    public LaneEvalResult Evaluate(
        LineConfig line,
        IReadOnlyDictionary<string, PrinterState> states,
        ZoneState zone,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(zone);

        // Zone down => the whole line is shut regardless of printer health (source sdisp_PA_Status_Zone).
        if (!zone.ZoneOnline)
        {
            _logger.LogWarning("Lane evaluation shut zone for line {LineId}: conveyor zone is down.", line.LineId);
            return new LaneEvalResult(LineControl.ShutZone, [], "Conveyor zone is down.");
        }

        var changes = new List<PrinterChange>();
        var control = LineControl.Balanced;
        var reasons = new List<string>();

        // Distinct orientations that actually have printers configured on this line (source: DISTINCT
        // PrinterType from the joined printer set). Re-evaluate the whole set after any promotion (GOTO TOPCURS).
        var orientations = line.Printers
            .Select(p => p.PrinterType)
            .Distinct()
            .ToList();

        var guard = 0;
        while (true)
        {
            if (guard++ > 1000)
            {
                throw new InvalidOperationException("Lane evaluation did not converge.");
            }

            // A printer that is offline cannot be a functional reserve — clear its spare flag first.
            // Scoped to this line's printers (source scopes the clear to the line's PrinterRecID list),
            // so a shared/global states dictionary can't let one line clear another's spares.
            foreach (var printer in line.Printers)
            {
                if (states.TryGetValue(printer.PrinterId, out var state) && !state.IsOnline)
                {
                    state.IsSpare = false;
                }
            }

            control = LineControl.Balanced;
            reasons.Clear();
            var promoted = false;

            foreach (var orientation in orientations)
            {
                if (!line.PrinterPolicies.TryGetValue(orientation, out var policy))
                {
                    // No configured minimum for this orientation => no constraint (source INNER JOIN drops it).
                    continue;
                }

                var group = line.Printers
                    .Where(p => p.PrinterType == orientation)
                    .Select(p => states.GetValueOrDefault(p.PrinterId))
                    .ToList();

                var online = group.Count(s => s is null || s.IsOnline);
                var spare = group.Count(s => s is { IsSpare: true });
                var usable = online - spare;

                if (usable < policy.OnlineMin)
                {
                    var promotedState = TryPromoteSpare(group, now);
                    if (promotedState is not null)
                    {
                        changes.Add(new PrinterChange(promotedState.PrinterId, SpareChange.PromotedFromSpare));
                        _logger.LogInformation(
                            "Promoted spare printer {PrinterId} on line {LineId} for orientation {Orientation}.",
                            promotedState.PrinterId, line.LineId, orientation);
                        promoted = true;
                        break; // restart the whole evaluation with the promoted printer in rotation
                    }

                    if (policy.AllowDegraded && usable >= policy.SlowLineFloor && usable >= 1)
                    {
                        control = Max(control, LineControl.SlowLine);
                        reasons.Add($"{orientation}: running slow ({usable}/{policy.OnlineMin} usable, degraded mode).");
                    }
                    else
                    {
                        control = Max(control, LineControl.ShutLine);
                        reasons.Add($"{orientation}: shutting line ({online}/{policy.OnlineMin} online, no spares).");
                    }
                }
                else if (online > policy.OnlineMin && usable > policy.OnlineMin)
                {
                    // Surplus online capacity — park the newest active printer as a spare.
                    var demoted = DemoteActive(group, now);
                    if (demoted is not null)
                    {
                        changes.Add(new PrinterChange(demoted.PrinterId, SpareChange.DemotedToSpare));
                        _logger.LogInformation(
                            "Demoted printer {PrinterId} to spare on line {LineId} for orientation {Orientation}.",
                            demoted.PrinterId, line.LineId, orientation);
                    }
                }
            }

            if (!promoted)
            {
                break;
            }
        }

        var reason = reasons.Count > 0 ? string.Join(" ", reasons) : "All printer groups balanced.";
        _logger.LogInformation(
            "Lane evaluation completed for line {LineId} with control {Control}: {Reason}.",
            line.LineId, control, reason);
        return new LaneEvalResult(control, changes, reason);
    }

    /// <summary>Promote the newest online spare in the group back into rotation; null if none.</summary>
    private static PrinterState? TryPromoteSpare(IEnumerable<PrinterState?> group, DateTimeOffset now)
    {
        var spare = group
            .Where(s => s is { IsSpare: true, IsOnline: true })
            .OrderByDescending(s => s!.LastStatusUpdate ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        if (spare is null)
        {
            return null;
        }

        spare.IsSpare = false;
        spare.LastStatusUpdate = now;
        return spare;
    }

    /// <summary>
    /// Park the newest active (online, non-spare) printer as a spare; null if none. Unlike the source
    /// query — which orders all online printers and can no-op on an already-spare pick — this selects a
    /// non-spare, matching the intent (see architecture-log 012 §4).
    /// </summary>
    private static PrinterState? DemoteActive(IEnumerable<PrinterState?> group, DateTimeOffset now)
    {
        var active = group
            .Where(s => s is { IsSpare: false, IsOnline: true })
            .OrderByDescending(s => s!.LastStatusUpdate ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        if (active is null)
        {
            return null;
        }

        active.IsSpare = true;
        active.LastStatusUpdate = now;
        return active;
    }

    private static LineControl Max(LineControl a, LineControl b) => (LineControl)Math.Max((int)a, (int)b);
}
