using PandA.Core;
using PandA.Core.Advice;
using PandA.Sim;

namespace PandA.Tests;

public sealed class AdviceMessageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WaveIdParser_ExtractsWaveIdAfterLdMarker()
    {
        var waveId = WaveIdParser.ParseFromFilename("ULW_INBOUND_LD12345_20260101.txt");

        Assert.Equal("12345", waveId);
    }

    [Fact]
    public async Task AdviceMessage_PopulatesInboundFieldsOnTransportOrder()
    {
        var store = new InMemoryTransportOrderStore();
        var service = new CartonAdviceService(store, new TestClock(T0), new InMemorySettingsProvider());
        var labels = new PandaLabelSet([new Label("Shipping", "SHIP1", "^XA^XZ")]);

        var order = await service.AdviseAsync(new AdviceMessage(
            "L1",
            "BLIND1",
            labels,
            WaveId: "W1",
            ProfileName: "Fragile",
            Bypass: true,
            VerifyEnabled: false,
            VerifyPassDest: "Verify - Pass",
            VerifyFailDest: "Verify - FAIL"));

        Assert.Equal("W1", order.WaveId);
        Assert.Equal("Fragile", order.ProfileName);
        Assert.True(order.Bypass);
        Assert.False(order.VerifyEnabled);
        Assert.Equal("Verify - Pass", order.VerifyPassDest);
        Assert.Equal("Verify - FAIL", order.VerifyFailDest);
    }

    [Fact]
    public void TransportOrder_DefaultInboundFields_AreBackwardCompatible()
    {
        var order = new TransportOrder("BLIND1", "L1", new PandaLabelSet([]), T0);

        Assert.Null(order.WaveId);
        Assert.Null(order.ProfileName);
        Assert.False(order.Bypass);
        Assert.True(order.VerifyEnabled);
        Assert.Null(order.VerifyPassDest);
        Assert.Null(order.VerifyFailDest);
    }
}
