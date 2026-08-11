using PandA.Core;
using Xunit;

namespace PandA.Tests;

/// <summary>
/// Tests for the fire-point model (architecture-log 010): <see cref="ApplyPoint"/>,
/// <see cref="FirePoint"/>, <see cref="FirePointProfile"/>, and <see cref="FirePointResolver"/>.
/// </summary>
public sealed class FirePointTests
{
    // ----- ApplyPoint: parse / format / guards -----

    [Theory]
    [InlineData("1T", 1, Edge.Trailing)]
    [InlineData("1L", 1, Edge.Leading)]
    [InlineData("0M", 0, Edge.Middle)]
    [InlineData("5.25L", 5.25, Edge.Leading)]
    [InlineData(".4M", 0.4, Edge.Middle)]
    [InlineData("-.4M", -0.4, Edge.Middle)]
    [InlineData("-1M", -1, Edge.Middle)]
    [InlineData("0L", 0, Edge.Leading)]
    public void ApplyPoint_Parse_DecodesInchAndEdge(string text, double inches, Edge edge)
    {
        var point = ApplyPoint.Parse(text);

        Assert.Equal((decimal)inches, point.Inches);
        Assert.Equal(edge, point.Edge);
    }

    [Theory]
    [InlineData("1t", Edge.Trailing)]
    [InlineData("2m", Edge.Middle)]
    [InlineData("3l", Edge.Leading)]
    public void ApplyPoint_Parse_IsCaseInsensitiveOnEdgeLetter(string text, Edge expected)
    {
        var point = ApplyPoint.Parse(text);

        Assert.Equal(expected, point.Edge);
    }

    [Theory]
    [InlineData("1T")]
    [InlineData("-.4M")]
    [InlineData("5.25L")]
    [InlineData("0M")]
    public void ApplyPoint_Parse_RoundTripsThroughToString(string text)
    {
        var point = ApplyPoint.Parse(text);

        Assert.Equal(point, ApplyPoint.Parse(point.ToString()));
    }

    [Theory]
    [InlineData("1X")]
    [InlineData("T")]
    [InlineData("abcM")]
    [InlineData("")]
    public void ApplyPoint_Parse_RejectsMalformed(string text)
    {
        Assert.ThrowsAny<System.Exception>(() => ApplyPoint.Parse(text));
    }

    [Theory]
    [InlineData(Edge.Leading)]
    [InlineData(Edge.Trailing)]
    public void ApplyPoint_NegativeInches_RejectedForNonMiddleEdge(Edge edge)
    {
        Assert.Throws<System.ArgumentException>(() => new ApplyPoint(-1m, edge));
    }

    [Fact]
    public void ApplyPoint_NegativeInches_AllowedForMiddle()
    {
        var point = new ApplyPoint(-0.4m, Edge.Middle);

        Assert.Equal(-0.4m, point.Inches);
    }

    [Fact]
    public void ApplyPoint_Parse_NegativeLeading_Rejected()
    {
        Assert.Throws<System.ArgumentException>(() => ApplyPoint.Parse("-1L"));
    }

    // ----- FirePoint: guards / neglect-print -----

    [Fact]
    public void FirePoint_Valid_ExposesFields()
    {
        var fp = new FirePoint(2, 800, 3, ApplyPoint.Parse("1T"));

        Assert.Equal(2, fp.PrintTrackingDevice);
        Assert.Equal(800, fp.PrintFirePoint);
        Assert.Equal(3, fp.ApplyTrackingDevice);
        Assert.Equal(ApplyPoint.Parse("1T"), fp.ApplyFirePoint);
        Assert.False(fp.NeglectPrint);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(-1, 3)]
    [InlineData(2, 0)]
    [InlineData(2, -1)]
    public void FirePoint_ZeroOrNegativeTrackingDevice_Rejected(int printDevice, int applyDevice)
    {
        Assert.Throws<System.ArgumentException>(
            () => new FirePoint(printDevice, 800, applyDevice, ApplyPoint.Parse("1T")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void FirePoint_NonPositivePrintFirePoint_MeansNeglectPrint(int printFirePoint)
    {
        var fp = new FirePoint(2, printFirePoint, 3, ApplyPoint.Parse("1T"));

        Assert.True(fp.NeglectPrint);
    }

    // ----- FirePointProfile -----

    [Fact]
    public void Profile_TryGet_IsCaseInsensitive()
    {
        var profile = new FirePointProfile("Generic",
        [
            (("Ship1", "Shipping"), new FirePoint(2, 800, 3, ApplyPoint.Parse("1T"))),
        ]);

        Assert.True(profile.TryGet("ship1", "shipping", out var fp));
        Assert.Equal(3, fp.ApplyTrackingDevice);
    }

    [Fact]
    public void Profile_TryGet_MissReturnsFalse()
    {
        var profile = new FirePointProfile("Generic",
        [
            (("Ship1", "Shipping"), new FirePoint(2, 800, 3, ApplyPoint.Parse("1T"))),
        ]);

        Assert.False(profile.TryGet("Cont1", "Content", out _));
    }

    [Fact]
    public void Profile_DuplicateSlot_Rejected()
    {
        Assert.Throws<System.ArgumentException>(() => new FirePointProfile("Dup",
        [
            (("Ship1", "Shipping"), new FirePoint(2, 800, 3, ApplyPoint.Parse("1T"))),
            (("SHIP1", "shipping"), new FirePoint(2, 800, 4, ApplyPoint.Parse("1L"))),
        ]));
    }

    [Fact]
    public void Profile_BlankName_Rejected()
    {
        Assert.Throws<System.ArgumentException>(() => new FirePointProfile("  ", []));
    }

    // ----- FirePointResolver -----

    [Fact]
    public void Resolver_Resolved_ReturnsFirePoint()
    {
        var profile = new FirePointProfile("Generic",
        [
            (("Par1", "Parcel"), new FirePoint(2, 800, 5, ApplyPoint.Parse("0M"))),
        ]);

        var result = new FirePointResolver().Resolve(profile, "Par1", "Parcel");

        Assert.Equal(FirePointStatus.Resolved, result.Status);
        Assert.True(result.IsResolved);
        Assert.Equal(5, result.FirePoint!.ApplyTrackingDevice);
    }

    [Fact]
    public void Resolver_NotInProfile_ReturnsReason()
    {
        var profile = new FirePointProfile("Generic",
        [
            (("Par1", "Parcel"), new FirePoint(2, 800, 5, ApplyPoint.Parse("0M"))),
        ]);

        var result = new FirePointResolver().Resolve(profile, "Ship1", "Shipping");

        Assert.Equal(FirePointStatus.NotInProfile, result.Status);
        Assert.False(result.IsResolved);
        Assert.Null(result.FirePoint);
        Assert.NotNull(result.Message);
    }

    // ----- LineConfig wiring -----

    [Fact]
    public void LineConfig_ActiveProfile_DefaultsToNull()
    {
        var config = new LineConfig("L1", [new PrinterConfig("Ship1", "10.0.0.1", 9100, ["Shipping"])]);

        Assert.Null(config.ActiveProfile);
    }

    [Fact]
    public void LineConfig_CarriesActiveProfile()
    {
        var profile = new FirePointProfile("Generic",
        [
            (("Ship1", "Shipping"), new FirePoint(2, 800, 3, ApplyPoint.Parse("1T"))),
        ]);
        var config = new LineConfig(
            "L1",
            [new PrinterConfig("Ship1", "10.0.0.1", 9100, ["Shipping"])],
            activeProfile: profile);

        Assert.Same(profile, config.ActiveProfile);
    }
}
