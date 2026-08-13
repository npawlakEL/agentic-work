using PandA.Core;
using PandA.Core.Advice;
using PandA.Core.Settings;
using PandA.Sim;

namespace PandA.Tests;

/// <summary>PROFSW — per-carton fire-point profile selection + GAP F19 (unknown profile → NoProfile).</summary>
public sealed class ProfileSwitchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTransportOrderStore _store = new();
    private readonly InMemoryLineProvider _lines = new();
    private readonly CapturingPrinterGateway _gateway = new();
    private readonly TestClock _clock = new(T0);
    private readonly CartonAdviceService _advice;
    private readonly InductService _induct;

    public ProfileSwitchTests()
    {
        _advice = new CartonAdviceService(_store, _clock, new InMemorySettingsProvider());
        _induct = new InductService(_store, _lines, new PrinterSelectionService(), _gateway, _clock, new InMemorySettingsProvider());
    }

    private static PrinterConfig Printer(string id, string[] map, int order) =>
        new(id, "10.0.0." + order, 9100, map, ApplyOrientation.Side, order);

    private static PandaLabelSet Labels(params string[] types) =>
        new(types.Select(t => new Label(t, $"LPN-{t}", $"^XA{t}^XZ")));

    private static FirePointProfile Profile(string name, int printFirePoint) =>
        new(name, [(("Ship1", "Shipping"), new FirePoint(2, printFirePoint, 3, ApplyPoint.Parse("1T")))]);

    private LineConfig LineWithRegistry(params FirePointProfile[] profiles) =>
        new("L1",
            [Printer("Ship1", ["Shipping"], 0)],
            profileRegistry: profiles.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public async Task Induct_CartonWithKnownProfileName_ResolvesThatProfilesFirePoint()
    {
        _lines.Add(LineWithRegistry(Profile("A10A51D10", 500), Profile("A20A61000", 900)));
        await _advice.AdviseAsync(new AdviceMessage("L1", "BLIND1", Labels("Shipping"), ProfileName: "A20A61000"));

        var result = await _induct.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.Printed, result.Status);
        var job = _gateway.Jobs.Single(j => j.LabelType == "Shipping");
        Assert.Equal(900, job.FirePoint!.PrintFirePoint);
    }

    [Fact]
    public async Task Induct_ProfileNameMatchIsCaseInsensitive()
    {
        _lines.Add(LineWithRegistry(Profile("A10A51D10", 500)));
        await _advice.AdviseAsync(new AdviceMessage("L1", "BLIND1", Labels("Shipping"), ProfileName: "a10a51d10"));

        var result = await _induct.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.Printed, result.Status);
        Assert.Equal(500, _gateway.Jobs.Single(j => j.LabelType == "Shipping").FirePoint!.PrintFirePoint);
    }

    [Fact]
    public async Task Induct_UnknownProfileName_ReturnsNoProfile_AndPrintsNothing()
    {
        _lines.Add(LineWithRegistry(Profile("A10A51D10", 500)));
        await _advice.AdviseAsync(new AdviceMessage("L1", "BLIND1", Labels("Shipping"), ProfileName: "DOES_NOT_EXIST"));

        var result = await _induct.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.NoProfile, result.Status);
        Assert.Empty(_gateway.Jobs);
        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(0, stored!.PrintCount);
        Assert.Equal(TransportOrderStatus.Advised, stored.Status);
    }

    [Fact]
    public async Task Induct_NoProfileName_FallsBackToLineDefaultProfile()
    {
        var fallback = Profile("Generic", 700);
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)],
            activeProfile: fallback));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping")); // no ProfileName

        var result = await _induct.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.Printed, result.Status);
        Assert.Equal(700, _gateway.Jobs.Single(j => j.LabelType == "Shipping").FirePoint!.PrintFirePoint);
    }

    [Fact]
    public async Task ProfileStore_ActivateDeactivate_TogglesActiveSet()
    {
        var store = new InMemoryProfileStore();
        store.Register("L1", Profile("A10A51D10", 500));
        store.Register("L1", Profile("A20A61000", 900));

        Assert.Equal(2, (await store.GetActiveProfilesAsync("L1")).Count);

        await store.DeactivateAsync("L1", "A20A61000");
        var active = await store.GetActiveProfilesAsync("L1");
        Assert.Single(active);
        Assert.Equal("A10A51D10", active[0].Name);

        await store.ActivateAsync("L1", "A20A61000");
        Assert.Equal(2, (await store.GetActiveProfilesAsync("L1")).Count);
    }

    [Fact]
    public async Task ProfileStore_UnknownLine_ReturnsEmpty()
    {
        var store = new InMemoryProfileStore();
        Assert.Empty(await store.GetActiveProfilesAsync("NOPE"));
    }
}
