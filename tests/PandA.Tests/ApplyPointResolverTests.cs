using PandA.Core;

namespace PandA.Tests;

/// <summary>
/// Tests decision-016 DynamicApplyPoint pulse math ported from
/// <c>sdisp_TOOL_CUSTOM_DynamicApplyPoint.sql</c>.
/// </summary>
public sealed class ApplyPointResolverTests
{
    private readonly ApplyPointResolver _resolver = new();

    public static TheoryData<ApplyPoint, CartonDimensions, ApplyPointConfig, int> SideCases { get; } = new()
    {
        { new ApplyPoint(1m, Edge.Leading), new CartonDimensions(100), new ApplyPointConfig(), 4 },
        { new ApplyPoint(1m, Edge.Leading), new CartonDimensions(100), new ApplyPointConfig(DefaultApplyDistance: 7), 11 },
        { new ApplyPoint(1m, Edge.Trailing), new CartonDimensions(100), new ApplyPointConfig(), 80 },
        { new ApplyPoint(0m, Edge.Middle), new CartonDimensions(100), new ApplyPointConfig(), 42 },
        { new ApplyPoint(-0.5m, Edge.Middle), new CartonDimensions(100), new ApplyPointConfig(), 40 },
        { new ApplyPoint(0m, Edge.Middle), new CartonDimensions(101), new ApplyPointConfig(), 43 },
    };

    [Theory]
    [MemberData(nameof(SideCases))]
    public void Side_ResolvesConfiguredFormula(
        ApplyPoint applyPoint,
        CartonDimensions carton,
        ApplyPointConfig config,
        int expected)
    {
        var result = _resolver.Resolve(applyPoint, ApplyOrientation.Side, carton, config);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Leading_IsCartonLengthIndependent()
    {
        var applyPoint = new ApplyPoint(1m, Edge.Leading);

        var shortCarton = _resolver.Resolve(applyPoint, ApplyOrientation.Side, new CartonDimensions(100), new ApplyPointConfig());
        var longCarton = _resolver.Resolve(applyPoint, ApplyOrientation.Side, new CartonDimensions(250), new ApplyPointConfig());

        Assert.Equal(4, shortCarton);
        Assert.Equal(shortCarton, longCarton);
    }

    [Fact]
    public void Trailing_DependsOnCartonLength()
    {
        var applyPoint = new ApplyPoint(1m, Edge.Trailing);

        var shortCarton = _resolver.Resolve(applyPoint, ApplyOrientation.Side, new CartonDimensions(100), new ApplyPointConfig());
        var longCarton = _resolver.Resolve(applyPoint, ApplyOrientation.Side, new CartonDimensions(120), new ApplyPointConfig());

        Assert.Equal(80, shortCarton);
        Assert.Equal(100, longCarton);
        Assert.NotEqual(shortCarton, longCarton);
    }

    [Fact]
    public void Middle_DependsOnCartonLength()
    {
        var applyPoint = new ApplyPoint(0m, Edge.Middle);

        var shortCarton = _resolver.Resolve(applyPoint, ApplyOrientation.Side, new CartonDimensions(100), new ApplyPointConfig());
        var longCarton = _resolver.Resolve(applyPoint, ApplyOrientation.Side, new CartonDimensions(120), new ApplyPointConfig());

        Assert.Equal(42, shortCarton);
        Assert.Equal(52, longCarton);
        Assert.NotEqual(shortCarton, longCarton);
    }

    [Fact]
    public void Top_TallerCartonFiresLaterThanShorterCarton()
    {
        var applyPoint = new ApplyPoint(1m, Edge.Leading);
        var config = new ApplyPointConfig(
            DefaultApplyDistance: 20,
            TampMountHeightInches: 10m,
            BeltSpeed: 2m,
            TampSpeed: 4m);

        var shorterCarton = _resolver.Resolve(applyPoint, ApplyOrientation.Top, new CartonDimensions(100, 6m), config);
        var tallerCarton = _resolver.Resolve(applyPoint, ApplyOrientation.Top, new CartonDimensions(100, 8m), config);

        Assert.Equal(16, shorterCarton);
        Assert.Equal(20, tallerCarton);
        Assert.True(tallerCarton > shorterCarton);
    }

    [Fact]
    public void Top_WithoutTampParameters_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => _resolver.Resolve(
                new ApplyPoint(1m, Edge.Leading),
                ApplyOrientation.Top,
                new CartonDimensions(100, 6m),
                new ApplyPointConfig()));
    }

    [Fact]
    public void Top_WithZeroTampSpeed_Throws()
    {
        var config = new ApplyPointConfig(TampMountHeightInches: 10m, BeltSpeed: 2m, TampSpeed: 0m);

        Assert.Throws<ArgumentException>(
            () => _resolver.Resolve(
                new ApplyPoint(1m, Edge.Leading),
                ApplyOrientation.Top,
                new CartonDimensions(100, 6m),
                config));
    }

    [Fact]
    public void NonPositiveEncoderResolution_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _resolver.Resolve(
                new ApplyPoint(1m, Edge.Leading),
                ApplyOrientation.Side,
                new CartonDimensions(100),
                new ApplyPointConfig(EncoderResolution: 0m)));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _resolver.Resolve(
                new ApplyPoint(1m, Edge.Leading),
                ApplyOrientation.Side,
                new CartonDimensions(100),
                new ApplyPointConfig(EncoderResolution: -0.25m)));
    }

    [Fact]
    public void LabelWidth_IsConfigurableForTrailingAndMiddle()
    {
        var defaultTrailing = _resolver.Resolve(
            new ApplyPoint(1m, Edge.Trailing),
            ApplyOrientation.Side,
            new CartonDimensions(100),
            new ApplyPointConfig());
        var wideTrailing = _resolver.Resolve(
            new ApplyPoint(1m, Edge.Trailing),
            ApplyOrientation.Side,
            new CartonDimensions(100),
            new ApplyPointConfig(LabelWidthInches: 6m));
        var defaultMiddle = _resolver.Resolve(
            new ApplyPoint(0m, Edge.Middle),
            ApplyOrientation.Side,
            new CartonDimensions(100),
            new ApplyPointConfig());
        var wideMiddle = _resolver.Resolve(
            new ApplyPoint(0m, Edge.Middle),
            ApplyOrientation.Side,
            new CartonDimensions(100),
            new ApplyPointConfig(LabelWidthInches: 6m));

        Assert.Equal(80, defaultTrailing);
        Assert.Equal(72, wideTrailing);
        Assert.NotEqual(defaultTrailing, wideTrailing);
        Assert.Equal(42, defaultMiddle);
        Assert.Equal(38, wideMiddle);
        Assert.NotEqual(defaultMiddle, wideMiddle);
    }
}
