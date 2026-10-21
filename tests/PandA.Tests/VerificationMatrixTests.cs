using PandA.Core;
using PandA.Core.Verification;
using Xunit;

namespace PandA.Tests;

/// <summary>
/// Driver tests for <see cref="VerificationService"/> — the A7 × A6 rows of architecture-log 008.
/// The classification theory pins the scanned-value → outcome table; the remaining facts pin the
/// content-label toggle interactions, empty-expected semantics, xref backups, and order sensitivity
/// (all corrected/added per the senior review).
/// </summary>
public sealed class VerificationMatrixTests
{
    private readonly VerificationService _sut = new();

    private static PandaLabelSet Expected(params (string Type, string Lpn)[] labels) =>
        new(labels.Select(l => new Label(l.Type, l.Lpn, $"^XA{l.Type}^XZ")));

    private static ScannedLabel[] Scan(params (string Type, string Value)[] labels) =>
        [.. labels.Select(l => new ScannedLabel(l.Type, l.Value))];

    // ---- Classification table (single Shipping label, verify enabled) ---------------------------

    public static IEnumerable<object[]> ClassificationCases()
    {
        // scannedValue, expectedOutcome
        yield return ["exact match", "SHIP1", VerifyOutcome.Pass];
        yield return ["plain mismatch", "OTHER", VerifyOutcome.Fail];
        yield return ["no-read '?'", "SH?P1", VerifyOutcome.NoRead];
        yield return ["no-data '!'", "SH!P", VerifyOutcome.NoData];
        yield return ["no-data '~'", "SH~P", VerifyOutcome.NoData];
        yield return ["no-data '0'", "0", VerifyOutcome.NoData];
        yield return ["conflict '#'", "SH#P", VerifyOutcome.Conflict];
    }

    [Theory]
    [MemberData(nameof(ClassificationCases))]
    public void Classify_SingleLabel_MapsScannedValueToOutcome(
        string because, string scanned, VerifyOutcome expected)
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scan(("Shipping", scanned)),
            new VerifyOptions());

        Assert.True(expected == result.Outcome, $"{because}: expected {expected} but was {result.Outcome}");
    }

    [Fact]
    public void Missing_ExpectedLabelNeverScanned_Fails()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scan(),
            new VerifyOptions());

        Assert.Equal(VerifyOutcome.Fail, result.Outcome);
        Assert.Contains(result.Details, d => d.Reason == VerifyLabelReason.Missing);
    }

    [Fact]
    public void Extra_ScannedTypeNotExpected_IsIgnored_ButLeftoverExpectedFails()
    {
        // Source TOOL_PA_VerifyLabel: a read with no expected value of that name is ignored (owner ruling 2026-10-21).
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scan(("Parcel", "P1")),
            new VerifyOptions());

        Assert.Equal(VerifyOutcome.Fail, result.Outcome);
        Assert.Contains(result.Details, d => d.Reason == VerifyLabelReason.Extra);
        Assert.Contains(result.Details, d => d.Reason == VerifyLabelReason.Missing && d.LabelType == "Shipping");
        Assert.Equal(VerifyReasonCode.ShippingLabelNoRead, VerifyReasonCodes.For(result));
    }

    // ---- Xref backup barcodes -------------------------------------------------------------------

    [Fact]
    public void Xref_BackupBarcodeMatches_Passes()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scan(("Shipping", "ALT1")),
            new VerifyOptions(),
            xref: [new LabelXref("Shipping", "ALT1")]);

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void Xref_ForDifferentType_DoesNotHelp()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scan(("Shipping", "ALT1")),
            new VerifyOptions(),
            xref: [new LabelXref("Content", "ALT1")]);

        Assert.Equal(VerifyOutcome.Fail, result.Outcome);
    }

    // ---- Verify enabled / bypass (A6) -----------------------------------------------------------

    [Fact]
    public void Disabled_WithBypass_ToteIdRead_Passes()
    {
        // Owner ruling 2026-10-21 (R4; was Ignore/code 3): the tote id "C123" read under any label name passes.
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scan(("Shipping", "C123")),
            new VerifyOptions(VerifyEnabled: false, Bypass: true),
            blindLabel: "C123");

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
        Assert.Equal(VerifyLabelReason.Matched, Assert.Single(result.Details).Reason);
        Assert.Equal(VerifyReasonCode.VerificationPassed, VerifyReasonCodes.For(result));
    }

    [Fact]
    public void Enabled_WithBypass_ToteIdRead_Passes_WithoutComparingLabels()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scan(("BlindLabel", "C123")),
            new VerifyOptions(VerifyEnabled: true, Bypass: true),
            blindLabel: "C123");

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void Disabled_WithoutBypass_Fails()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scan(("Shipping", "SHIP1")),
            new VerifyOptions(VerifyEnabled: false, Bypass: false));

        Assert.Equal(VerifyOutcome.Fail, result.Outcome);
        Assert.Empty(result.Details);
    }

    // ---- Content-label toggle interactions (senior additions) -----------------------------------

    [Fact]
    public void ToggleOff_WrongContentScan_Passes_BecauseContentIsFilteredOut()
    {
        // Only Shipping/Exception are verified when the toggle is off; a bad Content scan is ignored.
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1"), ("Content", "CON1")),
            Scan(("Shipping", "SHIP1"), ("Content", "WRONG")),
            new VerifyOptions(VerifyContentLabel: false));

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void ToggleOff_ContentExpected_EmptyScans_Passes_NotMissing()
    {
        // Content is the only expected label but is filtered out ⇒ empty expected set ⇒ Pass.
        var result = _sut.Verify(
            Expected(("Content", "CON1")),
            Scan(),
            new VerifyOptions(VerifyContentLabel: false));

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void ToggleOff_ShippingExpected_ExtraContentScan_Passes()
    {
        // The extra Content scan is filtered out, so it cannot be an Extra/NoRead.
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scan(("Shipping", "SHIP1"), ("Content", "CON1")),
            new VerifyOptions(VerifyContentLabel: false));

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void ToggleOff_ShippingExpected_UnexpectedExceptionScan_IsFilteredOut_Passes()
    {
        // Source TOOL_PA_VerifyLabel L236: with VerifyContentLabel=0 only reads whose value is in the expected
        // set are kept, so an unexpected Exception value is dropped (owner ruling 2026-10-21).
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scan(("Shipping", "SHIP1"), ("Exception", "EX1")),
            new VerifyOptions(VerifyContentLabel: false));

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    // ---- Empty expected-set semantics (senior: intended Pass) -----------------------------------

    [Fact]
    public void EmptyExpected_OrientationOnly_EmptyScans_Passes()
    {
        // Orientation is dropped from the expected set → nothing to verify → Pass.
        var result = _sut.Verify(
            Expected(("Orientation", "SIDE")),
            Scan(),
            new VerifyOptions());

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void EmptyExpected_PlaceholderLpn_EmptyScans_Passes()
    {
        // "-" and empty Lpn are dropped from expected → Pass.
        var result = _sut.Verify(
            Expected(("Shipping", "-"), ("Content", "")),
            Scan(),
            new VerifyOptions());

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void EmptyExpected_ButAScanArrives_IsIgnored_Passes()
    {
        // Source TOOL_PA_VerifyLabel: a read with no expected value of that name is ignored (owner ruling 2026-10-21).
        var result = _sut.Verify(
            Expected(("Orientation", "SIDE")),
            Scan(("Shipping", "SHIP1")),
            new VerifyOptions());

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    // ---- Order sensitivity / duplicate expected (senior) ----------------------------------------

    [Fact]
    public void DuplicateExpected_MatchingOrder_Passes()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "A"), ("Shipping", "B")),
            Scan(("Shipping", "A"), ("Shipping", "B")),
            new VerifyOptions());

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void DuplicateExpected_ReversedOrder_Passes_LikeSourceNameSlot()
    {
        // Source TOOL_PA_VerifyLabel: expected values are grouped by label name; a read matching any value
        // of the name passes it and clears that name (owner ruling 2026-10-21), so order no longer matters.
        var result = _sut.Verify(
            Expected(("Shipping", "A"), ("Shipping", "B")),
            Scan(("Shipping", "B"), ("Shipping", "A")),
            new VerifyOptions());

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void FirstFailure_ShortCircuits_Outcome()
    {
        // Shipping fails (no-read) before Content is ever considered → outcome is the first failure.
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1"), ("Content", "CON1")),
            Scan(("Shipping", "SH?P"), ("Content", "CON1")),
            new VerifyOptions());

        Assert.Equal(VerifyOutcome.NoRead, result.Outcome);
    }

    [Fact]
    public void MultiType_AllMatch_Passes()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "S1"), ("Content", "C1"), ("Parcel", "P1")),
            Scan(("Shipping", "S1"), ("Content", "C1"), ("Parcel", "P1")),
            new VerifyOptions());

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }
}
