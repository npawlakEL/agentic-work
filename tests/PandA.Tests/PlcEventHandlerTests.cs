using PandA.Core;
using PandA.Core.Induct;
using PandA.Core.Plc;
using PandA.Core.Settings;
using PandA.Sim;

namespace PandA.Tests;

/// <summary>F15 / decision-009 — PLC pre-verify carton recovery.</summary>
public sealed class PlcEventHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Line = "L1";
    private const string Tu = "BLIND1";

    private readonly InMemoryTransportOrderStore _store = new();
    private readonly InMemoryCartonRunRepository _runs = new();
    private readonly InMemoryVerifyDeviceProvider _verify = new();
    private readonly InMemorySettingsProvider _settings = new();

    private PlcEventHandlerService Handler() =>
        new(_store, _runs, _verify, _settings);

    private static PandaLabelSet Labels(params string[] types) =>
        new(types.Select(t => new Label(t, $"LPN-{t}", $"^XA{t}^XZ")));

    private static TransportOrder PrintedOrder()
    {
        var order = new TransportOrder(Tu, Line, Labels("Shipping"), T0);
        order.ForceMarkPrinted(T0); // Status = Printed, PrintCount = 1
        return order;
    }

    private async Task<long> SeedRunFor(string tuId)
    {
        var record = CartonRunRecord.Create(
            tuId, pandaDataId: 100, lineId: Line, sorterNumber: 1, sorterMode: 0, deviceId: 1,
            seqNum: 1, labelStatus: 0, scannedLabels: [tuId], length: 12, width: 8, height: 6,
            weight: 5, frontGap: 20, statusAtInduct: CartonStatus.PrintReady, assignedPrinter: "Ship1",
            destinationLane: null);
        return await _runs.CreateRunAsync(record);
    }

    private async Task Arrange(TransportOrder order, int verifyDeviceId = 2, bool reprint = true)
    {
        await _store.UpsertAsync(order);
        await SeedRunFor(order.TuId);
        _verify.Set(Line, verifyDeviceId);
        await _settings.SetAsync(KnownSettings.ReprintLabels, reprint);
    }

    private async Task<TransportOrder> Stored() => (await _store.FindActiveByTuIdAsync(Tu))!;

    [Fact]
    public async Task PreVerifyEvent_ReprintEnabled_ReArmsPrintedToAdvised()
    {
        var order = PrintedOrder();
        await Arrange(order);

        await Handler().HandleAsync(new PlcEvent((int)PlcEventCode.CartonLost, CartonListId: 1, DeviceId: 1, Line));

        var stored = await Stored();
        Assert.Equal(TransportOrderStatus.Advised, stored.Status);
        Assert.True(stored.CanPrint);
        Assert.Equal(1, stored.PrintCount); // monotonic, not zeroed
    }

    [Fact]
    public async Task EventAtVerifyDevice_NoRecovery()
    {
        var order = PrintedOrder();
        await Arrange(order);

        await Handler().HandleAsync(new PlcEvent((int)PlcEventCode.CartonLost, 1, DeviceId: 2, Line));

        Assert.Equal(TransportOrderStatus.Printed, (await Stored()).Status);
    }

    [Fact]
    public async Task EventBeyondVerifyDevice_NoRecovery()
    {
        var order = PrintedOrder();
        await Arrange(order);

        await Handler().HandleAsync(new PlcEvent((int)PlcEventCode.CartonLost, 1, DeviceId: 3, Line));

        Assert.Equal(TransportOrderStatus.Printed, (await Stored()).Status);
    }

    [Fact]
    public async Task NoCartonListId_NoRecovery()
    {
        var order = PrintedOrder();
        await Arrange(order);

        await Handler().HandleAsync(new PlcEvent((int)PlcEventCode.CartonLost, CartonListId: 0, DeviceId: 1, Line));

        Assert.Equal(TransportOrderStatus.Printed, (await Stored()).Status);
    }

    [Fact]
    public async Task VerifiedOrder_NoRecovery()
    {
        var order = PrintedOrder();
        order.MarkVerified(T0);
        await Arrange(order);

        await Handler().HandleAsync(new PlcEvent((int)PlcEventCode.CartonLost, 1, DeviceId: 1, Line));

        Assert.Equal(TransportOrderStatus.Verified, (await Stored()).Status);
    }

    [Fact]
    public async Task HeldOrder_NoRecovery()
    {
        var order = PrintedOrder();
        order.MarkVerifyFailed(T0);
        await Arrange(order);

        await Handler().HandleAsync(new PlcEvent((int)PlcEventCode.CartonLost, 1, DeviceId: 1, Line));

        Assert.Equal(TransportOrderStatus.HeldForIntervention, (await Stored()).Status);
    }

    [Fact]
    public async Task NoVerifyScannerConfigured_NoRecovery()
    {
        var order = PrintedOrder();
        await Arrange(order, verifyDeviceId: 0);

        await Handler().HandleAsync(new PlcEvent((int)PlcEventCode.CartonLost, 1, DeviceId: 1, Line));

        Assert.Equal(TransportOrderStatus.Printed, (await Stored()).Status);
    }

    [Fact]
    public async Task IgnoredCode217_NoRecovery()
    {
        var order = PrintedOrder();
        await Arrange(order);

        await Handler().HandleAsync(new PlcEvent(217, 1, DeviceId: 1, Line));

        Assert.Equal(TransportOrderStatus.Printed, (await Stored()).Status);
    }

    [Fact]
    public async Task UnmappedCode_StillRecoversWhenPreVerify()
    {
        var order = PrintedOrder();
        await Arrange(order);

        await Handler().HandleAsync(new PlcEvent(9999, 1, DeviceId: 1, Line));

        Assert.Equal(TransportOrderStatus.Advised, (await Stored()).Status);
        Assert.Equal("Undefined [Event Code: 9999]", PlcEventCodes.Describe(9999));
    }

    [Fact]
    public async Task TwoConsecutiveRecoveryEvents_AreIdempotent()
    {
        var order = PrintedOrder();
        await Arrange(order);
        var handler = Handler();

        await handler.HandleAsync(new PlcEvent((int)PlcEventCode.CartonLost, 1, DeviceId: 1, Line));
        await handler.HandleAsync(new PlcEvent((int)PlcEventCode.CartonLost, 1, DeviceId: 1, Line));

        var stored = await Stored();
        Assert.Equal(TransportOrderStatus.Advised, stored.Status);
        Assert.Equal(1, stored.PrintCount);
    }

    [Fact]
    public async Task PreVerifyEvent_ReprintDisabled_HoldsForIntervention()
    {
        var order = PrintedOrder();
        await Arrange(order, reprint: false);

        await Handler().HandleAsync(new PlcEvent((int)PlcEventCode.CartonLost, 1, DeviceId: 1, Line));

        Assert.Equal(TransportOrderStatus.HeldForIntervention, (await Stored()).Status);
    }
}
