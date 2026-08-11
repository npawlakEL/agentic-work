using PandA.Core;
using PandA.Core.Verification;
using Xunit;

namespace PandA.Tests;

public sealed class VerificationServiceTests
{
    private readonly VerificationService _sut = new();

    private static PandaLabelSet Expected(params (string Type, string Barcode)[] labels) =>
        new(labels.Select(l => new Label(l.Type, l.Barcode, $"^XA{l.Type}^XZ")));

    private static ScannedLabel[] Scanned(params (string Type, string Value)[] labels) =>
        [.. labels.Select(l => new ScannedLabel(l.Type, l.Value))];

    private static readonly VerifyOptions Default = new();

    [Fact]
    public void AllLabelsMatch_Passes()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1"), ("Content", "CON1")),
            Scanned(("Shipping", "SHIP1"), ("Content", "CON1")),
            Default);

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
        Assert.True(result.IsPass);
        Assert.All(result.Details, d => Assert.Equal(VerifyLabelReason.Matched, d.Reason));
    }

    [Fact]
    public void MatchOrderIndependent_Passes()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1"), ("Content", "CON1")),
            Scanned(("Content", "CON1"), ("Shipping", "SHIP1")),
            Default);

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void GenuineMismatch_Fails()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scanned(("Shipping", "WRONG")),
            Default);

        Assert.Equal(VerifyOutcome.Fail, result.Outcome);
        Assert.Contains(result.Details, d => d.Reason == VerifyLabelReason.Mismatch);
    }

    [Fact]
    public void NoReadSentinel_YieldsNoRead()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scanned(("Shipping", "?NOREAD?")),
            Default);

        Assert.Equal(VerifyOutcome.NoRead, result.Outcome);
    }

    [Theory]
    [InlineData("!ERR")]
    [InlineData("~ERR")]
    [InlineData("0")]
    public void NoDataSentinels_YieldNoData(string scanned)
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scanned(("Shipping", scanned)),
            Default);

        Assert.Equal(VerifyOutcome.NoData, result.Outcome);
    }

    [Fact]
    public void ConflictSentinel_YieldsConflict()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scanned(("Shipping", "#CONFLICT#")),
            Default);

        Assert.Equal(VerifyOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public void ExpectedLabelNeverScanned_FailsMissing()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1"), ("Content", "CON1")),
            Scanned(("Shipping", "SHIP1")),
            Default);

        Assert.Equal(VerifyOutcome.Fail, result.Outcome);
        Assert.Contains(result.Details, d => d.Reason == VerifyLabelReason.Missing && d.LabelType == "Content");
    }

    [Fact]
    public void ScannedLabelNotInData_YieldsNoRead()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scanned(("Shipping", "SHIP1"), ("Parcel", "PAR1")),
            Default);

        Assert.Equal(VerifyOutcome.NoRead, result.Outcome);
        Assert.Contains(result.Details, d => d.Reason == VerifyLabelReason.Extra && d.LabelType == "Parcel");
    }

    [Fact]
    public void XrefBackupValue_CountsAsMatch()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scanned(("Shipping", "BACKUP1")),
            Default,
            xref: [new LabelXref("Shipping", "BACKUP1")]);

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void FirstFailureShortCircuits_LaterLabelsNotProcessed()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1"), ("Content", "CON1")),
            Scanned(("Shipping", "WRONG"), ("Content", "CON1")),
            Default);

        Assert.Equal(VerifyOutcome.Fail, result.Outcome);
        Assert.DoesNotContain(result.Details, d => d.LabelType == "Content");
    }

    [Fact]
    public void ContentToggleOff_OnlyVerifiesShippingAndException()
    {
        // Content label is wrong, but with VerifyContentLabel=false it is not checked.
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1"), ("Content", "CON1")),
            Scanned(("Shipping", "SHIP1"), ("Content", "WRONG")),
            new VerifyOptions(VerifyContentLabel: false));

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void ContentToggleOn_VerifiesContent()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1"), ("Content", "CON1")),
            Scanned(("Shipping", "SHIP1"), ("Content", "WRONG")),
            Default);

        Assert.Equal(VerifyOutcome.Fail, result.Outcome);
    }

    [Fact]
    public void Disabled_WithBypass_Ignores()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scanned(("Shipping", "WRONG")),
            new VerifyOptions(VerifyEnabled: false, Bypass: true));

        Assert.Equal(VerifyOutcome.Ignore, result.Outcome);
    }

    [Fact]
    public void Disabled_WithoutBypass_Fails()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1")),
            Scanned(("Shipping", "SHIP1")),
            new VerifyOptions(VerifyEnabled: false, Bypass: false));

        Assert.Equal(VerifyOutcome.Fail, result.Outcome);
    }

    [Fact]
    public void OrientationExpected_IsNotRequired()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1"), ("Orientation", "TOP")),
            Scanned(("Shipping", "SHIP1")),
            Default);

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void ExpectedDashPlaceholder_IsNotRequired()
    {
        var result = _sut.Verify(
            Expected(("Shipping", "SHIP1"), ("Content", "-")),
            Scanned(("Shipping", "SHIP1")),
            Default);

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }
}
