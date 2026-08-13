using PandA.Core.Induct;
using PandA.Sim;
using Xunit;

namespace PandA.Tests;

public sealed class InductQualityClassifierTests
{
    [Theory]
    [InlineData("0154?006001", 25, 20, CartonStatus.NoRead)]
    [InlineData("-", 25, 20, CartonStatus.NoRead)]
    [InlineData("<", 25, 20, CartonStatus.NoRead)]
    [InlineData("0!", 25, 20, CartonStatus.NoData)]
    [InlineData("0", 25, 20, CartonStatus.NoData)]
    [InlineData("0154#006001", 25, 20, CartonStatus.LabelConflict)]
    [InlineData("0154006001", 10, 20, CartonStatus.GapError)]
    [InlineData("0154006001", 20, 20, CartonStatus.PrintReady)]
    [InlineData("?#conflict", 5, 20, CartonStatus.NoRead)]
    public void Classify_AppliesLookupCartonPriorityRules(string blindLabel, int gap, int minGap, CartonStatus expected)
    {
        Assert.Equal(expected, InductQualityClassifier.Classify(blindLabel, gap, minGap));
    }

    [Fact]
    public void SimMinGapProvider_ReturnsConfiguredMinimumGap()
    {
        var provider = new SimMinGapProvider(37);

        Assert.Equal(37, provider.GetMinGap());
    }

    [Fact]
    public void SimMinGapProvider_DefaultsToSourceMinGap()
    {
        var provider = new SimMinGapProvider();

        Assert.Equal(20, provider.GetMinGap());
    }
}
