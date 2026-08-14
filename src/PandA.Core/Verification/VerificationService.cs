namespace PandA.Core.Verification;

/// <summary>
/// Default <see cref="IVerificationService"/>. Faithful port of <c>sdisp_TOOL_PA_VerifyLabel</c>
/// (architecture-log 006): compares scanned labels to the expected set, short-circuits on the first
/// failure, and classifies the failure (no-read / no-data / conflict / mismatch / missing / extra).
/// </summary>
public sealed class VerificationService : IVerificationService
{
    // Types that are ALWAYS verified, even when the content-label toggle is off (source: the
    // VerifyContentLabel=0 branch keeps only 'Shipping'/'Exception'). Named for that role — it is NOT
    // the set of "content" types; it is the content-toggle-exempt set.
    private static readonly HashSet<string> AlwaysVerifiedTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Shipping", "Exception" };

    /// <inheritdoc />
    public VerifyResult Verify(
        PandaLabelSet expected,
        IReadOnlyList<ScannedLabel> scanned,
        VerifyOptions options,
        IReadOnlyList<LabelXref>? xref = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(scanned);
        ArgumentNullException.ThrowIfNull(options);

        // Disabled verify: bypassed cartons are ignored (VerifyPass=3); otherwise a hard fail (reject).
        if (!options.VerifyEnabled)
        {
            if (!options.Bypass)
            {
                return new VerifyResult(VerifyOutcome.Fail, []);
            }

            // Bypass proceeds without comparing to the expected set — EXCEPT a scanner no-read / no-data
            // is not a valid bypass (a glitched read), so it still fails and counts toward the fail streak
            // (decision-004 / VF-3).
            foreach (var scan in scanned)
            {
                var glitch = Classify(scan.ScannedValue);
                if (glitch is VerifyLabelReason.NoRead or VerifyLabelReason.NoData)
                {
                    return Fail(
                        [new VerifyLabelDetail(scan.LabelType, null, scan.ScannedValue, glitch)],
                        ToOutcome(glitch));
                }
            }

            return new VerifyResult(VerifyOutcome.Ignore, []);
        }

        var acceptable = BuildXrefLookup(xref);
        var expectedLabels = BuildExpected(expected, options, acceptable);
        var scannedLabels = FilterScanned(scanned, options);

        var details = new List<VerifyLabelDetail>();

        foreach (var scan in scannedLabels)
        {
            var slot = expectedLabels.FirstOrDefault(
                e => !e.Consumed && string.Equals(e.LabelType, scan.LabelType, StringComparison.OrdinalIgnoreCase));

            if (slot is null)
            {
                // Read a label the carton data never expected → treat as a no-read (source code 14).
                details.Add(new VerifyLabelDetail(scan.LabelType, null, scan.ScannedValue, VerifyLabelReason.Extra));
                return Fail(details, VerifyOutcome.NoRead);
            }

            if (Matches(slot, scan.ScannedValue))
            {
                slot.Consumed = true;
                details.Add(new VerifyLabelDetail(slot.LabelType, slot.Primary, scan.ScannedValue, VerifyLabelReason.Matched));
                continue;
            }

            var reason = Classify(scan.ScannedValue);
            slot.Consumed = true;
            details.Add(new VerifyLabelDetail(slot.LabelType, slot.Primary, scan.ScannedValue, reason));
            return Fail(details, ToOutcome(reason));
        }

        // Any expected label never presented to the scanner is missing from the box → fail.
        var missing = expectedLabels.FirstOrDefault(e => !e.Consumed);
        if (missing is not null)
        {
            details.Add(new VerifyLabelDetail(missing.LabelType, missing.Primary, null, VerifyLabelReason.Missing));
            return Fail(details, VerifyOutcome.Fail);
        }

        return new VerifyResult(VerifyOutcome.Pass, details);
    }

    private static VerifyResult Fail(List<VerifyLabelDetail> details, VerifyOutcome outcome) =>
        new(outcome, details);

    private static VerifyLabelReason Classify(string value)
    {
        if (value.Contains('?', StringComparison.Ordinal))
        {
            return VerifyLabelReason.NoRead;
        }

        if (value.Contains('!', StringComparison.Ordinal)
            || value.Contains('~', StringComparison.Ordinal)
            || value == "0")
        {
            return VerifyLabelReason.NoData;
        }

        if (value.Contains('#', StringComparison.Ordinal))
        {
            return VerifyLabelReason.Conflict;
        }

        return VerifyLabelReason.Mismatch;
    }

    private static VerifyOutcome ToOutcome(VerifyLabelReason reason) => reason switch
    {
        VerifyLabelReason.NoRead => VerifyOutcome.NoRead,
        VerifyLabelReason.NoData => VerifyOutcome.NoData,
        VerifyLabelReason.Conflict => VerifyOutcome.Conflict,
        VerifyLabelReason.Extra => VerifyOutcome.NoRead,
        _ => VerifyOutcome.Fail,
    };

    private static bool Matches(ExpectedLabel slot, string scannedValue) =>
        slot.Acceptable.Contains(scannedValue);

    private static Dictionary<string, HashSet<string>> BuildXrefLookup(IReadOnlyList<LabelXref>? xref)
    {
        var lookup = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (xref is null)
        {
            return lookup;
        }

        foreach (var x in xref)
        {
            if (string.IsNullOrEmpty(x.Barcode))
            {
                continue;
            }

            if (!lookup.TryGetValue(x.LabelType, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                lookup[x.LabelType] = set;
            }

            set.Add(x.Barcode);
        }

        return lookup;
    }

    private static List<ExpectedLabel> BuildExpected(
        PandaLabelSet expected,
        VerifyOptions options,
        Dictionary<string, HashSet<string>> xref)
    {
        var result = new List<ExpectedLabel>();
        foreach (var label in expected.Labels)
        {
            // Drop orientation markers and "not in data" placeholders; honour the content-label toggle.
            if (string.Equals(label.LabelType, "Orientation", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrEmpty(label.Lpn) || label.Lpn == "-")
            {
                continue;
            }

            if (!options.VerifyContentLabel && !AlwaysVerifiedTypes.Contains(label.LabelType))
            {
                continue;
            }

            var acceptable = new HashSet<string>(StringComparer.Ordinal) { label.Lpn };
            if (xref.TryGetValue(label.LabelType, out var alts))
            {
                acceptable.UnionWith(alts);
            }

            result.Add(new ExpectedLabel(label.LabelType, label.Lpn, acceptable));
        }

        return result;
    }

    private static List<ScannedLabel> FilterScanned(IReadOnlyList<ScannedLabel> scanned, VerifyOptions options)
    {
        if (options.VerifyContentLabel)
        {
            return [.. scanned];
        }

        return [.. scanned.Where(s => AlwaysVerifiedTypes.Contains(s.LabelType))];
    }

    private sealed class ExpectedLabel(string labelType, string primary, HashSet<string> acceptable)
    {
        public string LabelType { get; } = labelType;

        public string Primary { get; } = primary;

        public HashSet<string> Acceptable { get; } = acceptable;

        public bool Consumed { get; set; }
    }
}
