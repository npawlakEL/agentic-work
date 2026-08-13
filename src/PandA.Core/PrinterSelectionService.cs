namespace PandA.Core;

/// <summary>
/// Authoritative printer selection (architecture-log 005), ported from <c>sdisp_PA_PickPrinter</c>.
/// <para>
/// Per label type, choose among eligible printers (online + not spare + LabelMap contains the type) the
/// least-recently-printed one (PID1), with the next as backup (PID2). Eligibility is driven purely by the
/// label-type → printer mapping: a printer's physical apply orientation (Side/Top) is <em>not</em> a filter,
/// so a single carton scan routes each label to whatever printer is mapped to that type — a top-apply
/// Content label and a side-apply Shipping label on the same carton each go to their own printer.
/// If one printer is the primary (PID1) for more than one of the carton's label types, the colliding
/// types fall back to their backup so the carton's labels spread across distinct printers when possible.
/// Ties on LastPrinted resolve by configured order (lowest <see cref="PrinterConfig.ConfigOrder"/>).
/// </para>
/// </summary>
public sealed class PrinterSelectionService : IPrinterSelectionService
{
    public PrinterSelectionResult Select(
        LineConfig line,
        IReadOnlyDictionary<string, PrinterState> states,
        PandaLabelSet labels)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(labels);

        // 1) Per needed label type, rank eligible printers → primary (PID1) + backup (PID2).
        var ranked = new Dictionary<string, (string? Primary, string? Backup)>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in labels.DistinctLabelTypes())
        {
            var candidates = line.Printers
                .Where(p => p.CanPrint(type) && IsAvailable(states, p.PrinterId))
                .OrderBy(p => SortKey(line.LoadBalance, states, p.PrinterId))
                .ThenBy(p => p.ConfigOrder)
                .Select(p => p.PrinterId)
                .ToList();

            ranked[type] = (
                candidates.Count > 0 ? candidates[0] : null,
                candidates.Count > 1 ? candidates[1] : null);
        }

        // 2) Count how many label types share each primary printer (collision detection).
        var primaryCounts = ranked.Values
            .Where(v => v.Primary is not null)
            .GroupBy(v => v.Primary!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // 3) Resolve the printer per type: collide → backup (when one exists), else primary.
        var chosenByType = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (type, pids) in ranked)
        {
            var (primary, backup) = pids;
            chosenByType[type] = primary is not null && primaryCounts[primary] > 1 && backup is not null
                ? backup
                : primary;
        }

        // 4) Assign every label its type's chosen printer.
        var assignments = labels.Labels.Select(label =>
            chosenByType.TryGetValue(label.LabelType, out var printerId) && printerId is not null
                ? LabelAssignment.Assigned(label, printerId)
                : LabelAssignment.NoPrinter(label));

        return new PrinterSelectionResult(assignments);
    }

    // A printer with no supplied state is treated as available (fail-open). Invariant: the line provider
    // is responsible for supplying a PrinterState for every configured printer whose health can vary;
    // a missing entry means "no health signal known", not "offline". Lane-eval/health drivers keep the
    // state map populated (architecture-log 012).
    private static bool IsAvailable(IReadOnlyDictionary<string, PrinterState> states, string printerId) =>
        !states.TryGetValue(printerId, out var state) || state.IsAvailable;

    /// <summary>
    /// Load-balance ranking key: least-recently-printed first (null = never printed = oldest).
    /// When load balancing is off, all printers share a constant key so the ThenBy(ConfigOrder) picks
    /// the first configured printer.
    /// </summary>
    private static DateTimeOffset SortKey(
        bool loadBalance,
        IReadOnlyDictionary<string, PrinterState> states,
        string printerId)
    {
        if (!loadBalance)
        {
            return DateTimeOffset.MinValue;
        }

        return states.TryGetValue(printerId, out var state)
            ? state.LastPrinted ?? DateTimeOffset.MinValue
            : DateTimeOffset.MinValue;
    }
}
