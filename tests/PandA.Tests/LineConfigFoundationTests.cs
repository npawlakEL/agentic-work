using PandA.Core;

namespace PandA.Tests;

public sealed class LineConfigFoundationTests
{
    [Fact]
    public void Defaults_AreBackwardCompatibleForWave0Fields()
    {
        var config = new LineConfig("L1", [new PrinterConfig("P1", "10.0.0.1", 9100, ["Shipping"])]);

        Assert.Empty(config.Lanes);
        Assert.False(config.PrinterStatusSuffix);
        Assert.Null(config.PlcZone);
        Assert.Null(config.SorterPlcRecId);
        Assert.Null(config.PlcDbName);
        Assert.Equal(0.2m, config.EncoderResolution);
        Assert.True(config.DynamicPrintPoint);
        Assert.Empty(config.ProfileRegistry);
        Assert.Null(config.DefaultProfile);
        Assert.True(config.FilterLabels);
    }

    [Fact]
    public void Constructor_StoresWave0Fields()
    {
        var lane = new LaneDef("Verify - Pass", 12);
        var profile = new FirePointProfile("Generic", []);
        var config = new LineConfig(
            "L1",
            [new PrinterConfig("P1", "10.0.0.1", 9100, ["Shipping"])],
            lanes: [lane],
            printerStatusSuffix: true,
            plcZone: "ZoneA",
            sorterPlcRecId: 7,
            plcDbName: "SorterDb",
            encoderResolution: 0.25m,
            dynamicPrintPoint: false,
            profileRegistry: new Dictionary<string, FirePointProfile> { ["Generic"] = profile },
            defaultProfile: "Generic",
            filterLabels: false);

        Assert.Equal([lane], config.Lanes);
        Assert.True(config.PrinterStatusSuffix);
        Assert.Equal("ZoneA", config.PlcZone);
        Assert.Equal(7, config.SorterPlcRecId);
        Assert.Equal("SorterDb", config.PlcDbName);
        Assert.Equal(0.25m, config.EncoderResolution);
        Assert.False(config.DynamicPrintPoint);
        Assert.Same(profile, config.ProfileRegistry["Generic"]);
        Assert.Equal("Generic", config.DefaultProfile);
        Assert.False(config.FilterLabels);
    }
}
