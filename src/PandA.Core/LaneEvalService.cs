using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PandA.Core.Concurrency;

namespace PandA.Core;

/// <summary>
/// Lane evaluation, ported from <c>sdisp_PA_LaneEval</c> (architecture-log 012). For each apply
/// orientation on the line it maintains exactly <see cref="PrinterGroupPolicy.OnlineMin"/> usable
/// (online, non-spare) printers: parking surplus online printers as spares, promoting a spare back
/// into rotation when an active is lost, and — when it can neither meet the minimum nor promote a
/// spare — deciding whether to run the line slow (degraded) or shut it down.
/// <para>
/// Multi-host (decision-010): <see cref="EvaluateAsync"/> takes the shared <see cref="LockNames.PrinterState"/>
/// lease for the line, reads a fresh <see cref="IPrinterStateStore"/> snapshot, runs the pure
/// <see cref="Evaluate"/> decision on it, and writes every changed spare flag back through the store before
/// releasing the lease — so two hosts evaluating the same line serialize and never act on stale state.
/// On lease timeout the evaluation is skipped (logged at Warning); the next printer/zone status event
/// re-evaluates. The real BluePaw slow/shut egress is a separate, stubbed concern.
/// </para>
/// </summary>
public sealed class LaneEvalService
{
    private readonly ILockProvider _locks;
    private readonly IPrinterStateStore _printerStates;
    private readonly Settings.ISettingsProvider? _settings;
    private readonly ILogger<LaneEvalService> _logger;

    public LaneEvalService(
        ILockProvider locks,
        IPrinterStateStore printerStates,
        Settings.ISettingsProvider? settings = null,
        ILogger<LaneEvalService>? logger = null)
    {
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _printerStates = printerStates ?? throw new ArgumentNullException(nameof(printerStates));
        _settings = settings;
        _logger = logger ?? NullLogger<LaneEvalService>.Instance;
    }

    /// <summary>
    /// Evaluate <paramref name="line"/> against the shared printer state under the line's
    /// <c>PrinterState_{lineId}</c> lease (platform-settings F13) and persist the spare changes.
    /// Returns <see cref="LaneEvalResult.SkippedResult"/> when the lease cannot be acquired.
    /// </summary>
    public ValueTask<LaneEvalResult> EvaluateAsync(
        LineConfig line,
        ZoneState zone,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        ApplyUnderLeaseAsync(line, zone, now, statusChange: null, cancellationToken);

    /// <summary>
    /// Record a printer status signal (source <c>sdisp_PA_Status_Printer</c>) and re-evaluate the line, both
    /// under the line's <c>PrinterState_{lineId}</c> lease so the status write and the spare decision are one
    /// serialized step across hosts.
    /// </summary>
    public ValueTask<LaneEvalResult> ApplyPrinterStatusAsync(
        LineConfig line,
        string printerId,
        bool plcOnline,
        bool engineOnline,
        ZoneState zone,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        return ApplyUnderLeaseAsync(line, zone, now, (printerId, plcOnline, engineOnline), cancellationToken);
    }

    private async ValueTask<LaneEvalResult> ApplyUnderLeaseAsync(
        LineConfig line,
        ZoneState zone,
        DateTimeOffset now,
        (string PrinterId, bool Plc, bool Engine)? statusChange,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(zone);

        ILease lease;
        try
        {
            lease = await _locks
                .AcquireAsync(LockNames.PrinterState(line.LineId), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (LockTimeoutException ex)
        {
            _logger.LogWarning(
                ex,
                "Lane evaluation for line {LineId} skipped: lease {Resource} is held by another host; the next status event re-evaluates.",
                line.LineId,
                ex.Resource);
            return LaneEvalResult.SkippedResult();
        }

        await using (lease.ConfigureAwait(false))
        {
            if (statusChange is { } change)
            {
                await _printerStates
                    .SetStatusAsync(change.PrinterId, change.Plc, change.Engine, now, cancellationToken)
                    .ConfigureAwait(false);
            }

            // "2 Printer Rule": the line's own value wins; otherwise the global setting (source default 0).
            var twoPrinterRule = line.TwoPrinterRule
                ?? (_settings is { } settings
                    && await settings.GetAsync(Settings.KnownSettings.TwoPrinterRule, cancellationToken).ConfigureAwait(false));

            var before = await _printerStates.GetLineSnapshotAsync(line, cancellationToken).ConfigureAwait(false);
            var working = before.ToDictionary(kv => kv.Key, kv => kv.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            var result = Evaluate(line, working, zone, now, twoPrinterRule);

            foreach (var (printerId, after) in working)
            {
                var original = before[printerId];
                if (original.IsSpare == after.IsSpare)
                {
                    continue;
                }

                var stamp = after.LastStatusUpdate != original.LastStatusUpdate ? after.LastStatusUpdate : null;
                await _printerStates
                    .SetSpareAsync(printerId, after.IsSpare, stamp, cancellationToken)
                    .ConfigureAwait(false);
            }

            return result;
        }
    }

    /// <summary>
    /// Pure evaluation of one line over caller-owned <paramref name="states"/> (mutated in place). Does not
    /// touch the shared store or take a lease — callers that need persistence use <see cref="EvaluateAsync"/>.
    /// <paramref name="now"/> stamps <see cref="PrinterState.LastStatusUpdate"/> on any promote/demote so the
    /// newest change is preferred next time.
    /// </summary>
    /// <param name="twoPrinterRule">Effective "2 Printer Rule"; null uses <see cref="LineConfig.TwoPrinterRule"/>
    /// (unset → off, the source default).</param>
    public LaneEvalResult Evaluate(
        LineConfig line,
        IReadOnlyDictionary<string, PrinterState> states,
        ZoneState zone,
        DateTimeOffset now,
        bool? twoPrinterRule = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(zone);

        return EvaluateCore(line, states, zone, now, twoPrinterRule ?? line.TwoPrinterRule ?? false);
    }

    private LaneEvalResult EvaluateCore(
        LineConfig line,
        IReadOnlyDictionary<string, PrinterState> states,
        ZoneState zone,
        DateTimeOffset now,
        bool twoPrinterRule)
    {
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

                // Source sdivw_PrinterStatus INNER JOIN: a printer with no state row is not part of the
                // evaluated set at all — never online, not counted in the total (owner ruling 2026-10-21).
                var group = line.Printers
                    .Where(p => p.PrinterType == orientation)
                    .Select(p => states.GetValueOrDefault(p.PrinterId))
                    .OfType<PrinterState>()
                    .ToList();

                var total = group.Count;
                var online = group.Count(s => s.IsOnline);
                var spare = group.Count(s => s.IsSpare);
                var usable = online - spare;

                if (usable < policy.OnlineMin)
                {
                    // Source LaneEval L71-77/L170-175: the "2 Printer Rule" is checked BEFORE spare promotion
                    // — a 2-printer group with exactly one online runs slow (owner ruling 2026-10-21).
                    if (twoPrinterRule && total == 2 && online == 1)
                    {
                        control = Max(control, LineControl.SlowLine);
                        reasons.Add($"{orientation}: running slow (2 Printer Rule: 1 of 2 online).");
                        continue;
                    }

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
                    // Surplus online capacity — park the newest active printer as a spare. Like the source
                    // (sdisp_PA_LaneEval demote branch has no GOTO TOPCURS), this demotes exactly ONE printer
                    // per orientation per Evaluate call and converges across successive calls; only a
                    // promotion restarts the pass. Do NOT set promoted here.
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
    private static PrinterState? TryPromoteSpare(IEnumerable<PrinterState> group, DateTimeOffset now)
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
    private static PrinterState? DemoteActive(IEnumerable<PrinterState> group, DateTimeOffset now)
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
