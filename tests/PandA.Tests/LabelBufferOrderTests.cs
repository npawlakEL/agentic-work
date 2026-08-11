using PandA.Core;
using PandA.Core.Verification;
using Xunit;

namespace PandA.Tests;

/// <summary>
/// Tests for <see cref="LabelBufferOrder"/> — the per-line port of <c>Settings_LabelBufferOrder</c>
/// that types a raw scanned buffer by position (source <c>sdisp_TOOL_PA_VerifyLabel</c>'s
/// <c>STRING_SPLIT</c> + <c>JOIN ON rownum = LabelNumber</c>).
/// </summary>
public sealed class LabelBufferOrderTests
{
    [Fact]
    public void Default_MatchesSeededPlantLayout()
    {
        var order = LabelBufferOrder.Default;

        Assert.Equal(
            [
                new LabelBufferPosition(1, "BlindLabel"),
                new LabelBufferPosition(2, "Shipping"),
                new LabelBufferPosition(3, "Content"),
                new LabelBufferPosition(4, "Parcel"),
            ],
            order.Positions);
        Assert.Equal(4, order.MaxPosition);
    }

    [Fact]
    public void Type_MapsEachPositionToItsConfiguredType()
    {
        var typed = LabelBufferOrder.Default.Type(["BL-1", "SHIP-1", "CONT-1", "PARC-1"]);

        Assert.Equal(
            [
                new ScannedLabel("BlindLabel", "BL-1"),
                new ScannedLabel("Shipping", "SHIP-1"),
                new ScannedLabel("Content", "CONT-1"),
                new ScannedLabel("Parcel", "PARC-1"),
            ],
            typed);
    }

    [Fact]
    public void Type_SkipsEmptyAndWhitespaceSlots()
    {
        // Positions 1 (blind) and 3 (content) physically absent → no phantom reads emitted.
        var typed = LabelBufferOrder.Default.Type(["", "SHIP-1", "  ", "PARC-1"]);

        Assert.Equal(
            [
                new ScannedLabel("Shipping", "SHIP-1"),
                new ScannedLabel("Parcel", "PARC-1"),
            ],
            typed);
    }

    [Fact]
    public void Type_DropsReadsPastConfiguredPositions()
    {
        // INNER JOIN semantics: a 5th read with no position-5 mapping is dropped.
        var order = new LabelBufferOrder([new LabelBufferPosition(1, "Shipping")]);

        var typed = order.Type(["SHIP-1", "EXTRA"]);

        Assert.Equal([new ScannedLabel("Shipping", "SHIP-1")], typed);
    }

    [Fact]
    public void Type_HonoursNonContiguousPositions()
    {
        // A line whose scanner only populates slots 2 and 4.
        var order = new LabelBufferOrder(
        [
            new LabelBufferPosition(2, "Shipping"),
            new LabelBufferPosition(4, "Parcel"),
        ]);

        var typed = order.Type(["", "SHIP-1", "", "PARC-1"]);

        Assert.Equal(
            [
                new ScannedLabel("Shipping", "SHIP-1"),
                new ScannedLabel("Parcel", "PARC-1"),
            ],
            typed);
    }

    [Fact]
    public void Type_EmptyBuffer_YieldsNothing()
    {
        Assert.Empty(LabelBufferOrder.Default.Type([]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ctor_RejectsNonPositivePosition(int position)
    {
        Assert.Throws<ArgumentException>(() =>
            new LabelBufferOrder([new LabelBufferPosition(position, "Shipping")]));
    }

    [Fact]
    public void Ctor_RejectsBlankLabelType()
    {
        Assert.Throws<ArgumentException>(() =>
            new LabelBufferOrder([new LabelBufferPosition(1, "  ")]));
    }

    [Fact]
    public void Ctor_RejectsDuplicatePosition()
    {
        Assert.Throws<ArgumentException>(() =>
            new LabelBufferOrder(
            [
                new LabelBufferPosition(1, "Shipping"),
                new LabelBufferPosition(1, "Content"),
            ]));
    }

    [Fact]
    public void LineConfig_DefaultsToDefaultBufferOrder()
    {
        var line = new LineConfig("L1", [new PrinterConfig("P1", "1.1.1.1", 9100, ["Shipping"], ApplyOrientation.Side, 0)]);

        Assert.Same(LabelBufferOrder.Default, line.BufferOrder);
    }

    [Fact]
    public void LineConfig_AcceptsPerLineBufferOrder()
    {
        var custom = new LabelBufferOrder(
        [
            new LabelBufferPosition(1, "Shipping"),
            new LabelBufferPosition(2, "Content"),
        ]);
        var line = new LineConfig(
            "L2",
            [new PrinterConfig("P1", "1.1.1.1", 9100, ["Shipping"], ApplyOrientation.Side, 0)],
            bufferOrder: custom);

        Assert.Same(custom, line.BufferOrder);
    }

    [Fact]
    public void TwoLines_WithDifferentLayouts_TypeSameBufferDifferently()
    {
        // Same physical reads, different per-line layout → different typing. Proves per-line config.
        var lineA = new LabelBufferOrder(
        [
            new LabelBufferPosition(1, "Shipping"),
            new LabelBufferPosition(2, "Content"),
        ]);
        var lineB = new LabelBufferOrder(
        [
            new LabelBufferPosition(1, "Content"),
            new LabelBufferPosition(2, "Shipping"),
        ]);

        string[] buffer = ["AAA", "BBB"];

        Assert.Equal(
            [new ScannedLabel("Shipping", "AAA"), new ScannedLabel("Content", "BBB")],
            lineA.Type(buffer));
        Assert.Equal(
            [new ScannedLabel("Content", "AAA"), new ScannedLabel("Shipping", "BBB")],
            lineB.Type(buffer));
    }

    [Fact]
    public void TypedBuffer_FeedsVerification_ToPass()
    {
        // Regression: buffer-order output plugs straight into the verify service and passes when aligned.
        var expected = new PandaLabelSet(
        [
            new Label("Shipping", "SHIP-1", "^XA^XZ"),
            new Label("Content", "CONT-1", "^XA^XZ"),
        ]);
        var typed = LabelBufferOrder.Default.Type(["", "SHIP-1", "CONT-1", ""]);

        var result = new VerificationService().Verify(expected, typed, new VerifyOptions());

        Assert.True(result.IsPass);
    }

    [Fact]
    public void TypedBuffer_WrongLayout_FailsVerification()
    {
        // A misconfigured layout that swaps Shipping/Content positions mistypes the reads → mismatch.
        var expected = new PandaLabelSet(
        [
            new Label("Shipping", "SHIP-1", "^XA^XZ"),
            new Label("Content", "CONT-1", "^XA^XZ"),
        ]);
        var swapped = new LabelBufferOrder(
        [
            new LabelBufferPosition(2, "Content"),
            new LabelBufferPosition(3, "Shipping"),
        ]);
        var typed = swapped.Type(["", "SHIP-1", "CONT-1"]);

        var result = new VerificationService().Verify(expected, typed, new VerifyOptions());

        Assert.False(result.IsPass);
    }
}
