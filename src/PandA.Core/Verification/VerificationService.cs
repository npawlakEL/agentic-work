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

    private const string BlindLabelType = "BlindLabel";

    /// <inheritdoc />
    public VerifyResult Verify(
        PandaLabelSet expected,
        IReadOnlyList<ScannedLabel> scanned,
        VerifyOptions options,
        IReadOnlyList<LabelXref>? xref = null,
        string? blindLabel = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(scanned);
        ArgumentNullException.ThrowIfNull(options);

        // Owner ruling 2026-10-21 (R4): a Bypass carton (no labels printed) must NOT fail verify just for being
        // bypassed. If the scanner read the carton id (the blind label / tote id, or one of its 'BlindLabel' xref
        // aliases) the carton PASSES — whatever VerifyEnabled says. A bypass carton whose id was not read still
        // fails normally (no-read / no-data / conflict / mismatch / missing).
        if (options.Bypass)
        {
            return VerifyBypass(scanned, xref, blindLabel);
        }

        // Disabled verify on a non-bypass carton: hard fail (reject).
        if (!options.VerifyEnabled)
        {
            return new VerifyResult(VerifyOutcome.Fail, []);
        }

        var acceptable = BuildXrefLookup(xref);
        var expectedLabels = BuildExpected(expected, options, acceptable, blindLabel);
        var scannedLabels = FilterScanned(scanned, options, expectedLabels);

        var details = new List<VerifyLabelDetail>();

        // Source cursor (sdisp_TOOL_PA_VerifyLabel L246-395, owner ruling 2026-10-21): each scanned label is
        // compared against the still-unconsumed expected values of its name.
        foreach (var scan in scannedLabels)
        {
            var slot = expectedLabels.FirstOrDefault(
                e => !e.Consumed && string.Equals(e.LabelType, scan.LabelType, StringComparison.OrdinalIgnoreCase));

            if (slot is null)
            {
                // @CurrentVerifyLabel = '-': no (remaining) expected value of that name → the read is ignored
                // (L275-278), including a second read of a name already matched.
                details.Add(new VerifyLabelDetail(scan.LabelType, null, scan.ScannedValue, VerifyLabelReason.Extra));
                continue;
            }

            if (Matches(slot, scan.ScannedValue))
            {
                // PassVerify: every expected value of that name is removed (L389-393).
                slot.Consumed = true;
                details.Add(new VerifyLabelDetail(slot.LabelType, slot.Primary, scan.ScannedValue, VerifyLabelReason.Matched));
                continue;
            }

            var reason = Classify(scan.ScannedValue);
            slot.Consumed = true;
            details.Add(new VerifyLabelDetail(slot.LabelType, slot.Primary, scan.ScannedValue, reason));
            return Fail(details, ToOutcome(reason));
        }

        // Any expected label left over was never read → missing from the box (L397-410). This includes the
        // BlindLabel slot (blind label + every carton xref barcode) whenever the content label is verified.
        var missing = expectedLabels.FirstOrDefault(e => !e.Consumed);
        if (missing is not null)
        {
            details.Add(new VerifyLabelDetail(missing.LabelType, missing.Primary, null, VerifyLabelReason.Missing));
            return Fail(details, VerifyOutcome.Fail);
        }

        return new VerifyResult(VerifyOutcome.Pass, details);
    }
    private static VerifyResult VerifyBypass(
        IReadOnlyList<ScannedLabel> scanned,
        IReadOnlyList<LabelXref>? xref,
        string? blindLabel)
    {
        var cartonIds = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(blindLabel))
        {
            cartonIds.Add(blindLabel);
        }

        if (BuildXrefLookup(xref).TryGetValue(BlindLabelType, out var aliases))
        {
            cartonIds.UnionWith(aliases);
        }

        var primary = blindLabel ?? cartonIds.FirstOrDefault();

        // Any read of the carton id passes (the scanner may report it under any label name).
        foreach (var scan in scanned)
        {
            if (cartonIds.Contains(scan.ScannedValue))
            {
                return new VerifyResult(
                    VerifyOutcome.Pass,
                    [new VerifyLabelDetail(BlindLabelType, primary, scan.ScannedValue, VerifyLabelReason.Matched)]);
            }
        }

        // Carton id not read: fail with the carton-id read's own classification when there was one ...
        var idRead = scanned.FirstOrDefault(
            s => string.Equals(s.LabelType, BlindLabelType, StringComparison.OrdinalIgnoreCase));
        if (idRead is not null)
        {
            var reason = Classify(idRead.ScannedValue);
            return Fail([new VerifyLabelDetail(BlindLabelType, primary, idRead.ScannedValue, reason)], ToOutcome(reason));
        }

        // ... else with the first scanner glitch (no-read / no-data) ...
        foreach (var scan in scanned)
        {
            var glitch = Classify(scan.ScannedValue);
            if (glitch is VerifyLabelReason.NoRead or VerifyLabelReason.NoData)
            {
                return Fail([new VerifyLabelDetail(scan.LabelType, null, scan.ScannedValue, glitch)], ToOutcome(glitch));
            }
        }

        // ... else the carton id is missing (reason 14).
        return Fail([new VerifyLabelDetail(BlindLabelType, primary, null, VerifyLabelReason.Missing)], VerifyOutcome.Fail);
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
        Dictionary<string, HashSet<string>> xref,
        string? blindLabel)
    {
        // One expected slot per label NAME holding every acceptable value of that name (source @VerifyLabels
        // rows grouped by LabelName; a read matching any of them passes the whole name, L363-371).
        var result = new List<ExpectedLabel>();

        ExpectedLabel GetOrAdd(string type, string primary)
        {
            var slot = result.Find(e => string.Equals(e.LabelType, type, StringComparison.OrdinalIgnoreCase));
            if (slot is null)
            {
                slot = new ExpectedLabel(type, primary, new HashSet<string>(StringComparer.Ordinal));
                result.Add(slot);
            }

            return slot;
        }

        foreach (var label in expected.Labels)
        {
            // Drop orientation markers and "not in data" placeholders (L227-231).
            if (string.Equals(label.LabelType, "Orientation", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrEmpty(label.Lpn) || label.Lpn == "-")
            {
                continue;
            }

            var slot = GetOrAdd(label.LabelType, label.Lpn);
            slot.Acceptable.Add(label.Lpn);
            if (xref.TryGetValue(label.LabelType, out var alts))
            {
                slot.Acceptable.UnionWith(alts);
            }
        }

        // Source L191-195 UNIONs every PandaDataXRef barcode of the carton — the blind label itself ('BL')
        // plus its UPC/oLPN aliases — into the expected set under the name 'BlindLabel'. It is a REQUIRED
        // slot: a buffer with no matching blind-label read leaves it over → reason 14 (owner ruling 2026-10-21).
        if (!string.IsNullOrWhiteSpace(blindLabel))
        {
            var slot = GetOrAdd(BlindLabelType, blindLabel);
            slot.Acceptable.Add(blindLabel);
        }

        if (xref.TryGetValue(BlindLabelType, out var blindAlts) && blindAlts.Count > 0)
        {
            var slot = GetOrAdd(BlindLabelType, blindLabel ?? blindAlts.First());
            slot.Acceptable.UnionWith(blindAlts);
        }

        // VerifyContentLabel=0 keeps only the Shipping/Exception names (L233-238).
        if (!options.VerifyContentLabel)
        {
            result.RemoveAll(e => !AlwaysVerifiedTypes.Contains(e.LabelType));
        }

        return result;
    }

    private static List<ScannedLabel> FilterScanned(
        IReadOnlyList<ScannedLabel> scanned,
        VerifyOptions options,
        List<ExpectedLabel> expectedLabels)
    {
        if (options.VerifyContentLabel)
        {
            return [.. scanned];
        }

        // Source L236: with the content label off, only reads whose VALUE is an expected Shipping/Exception
        // barcode survive; anything else (a wrong or no-read shipping value) is dropped, so the Shipping slot
        // is left over and fails as missing (code 20).
        return [.. scanned.Where(s => expectedLabels.Exists(e => e.Acceptable.Contains(s.ScannedValue)))];
    }
    private sealed class ExpectedLabel(string labelType, string primary, HashSet<string> acceptable)
    {
        public string LabelType { get; } = labelType;

        public string Primary { get; } = primary;

        public HashSet<string> Acceptable { get; } = acceptable;

        public bool Consumed { get; set; }
    }
}
