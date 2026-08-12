using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using PandA.UI.Contracts.Common;
using PandA.UI.Contracts.Manda;
using PandA.UI.Pages;

namespace PandA.UI.Tests;

/// <summary>bUnit render + interaction gate for the MandA station screen.</summary>
public sealed class MandaStationTests : TestContext
{
    private readonly FakeManda _manda = new();

    public MandaStationTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IMandaStationQuery>(_manda);
        Services.AddSingleton<IMandaScanCommand>(_manda);
        Services.AddSingleton<IMandaPrintCommand>(_manda);
        Services.AddSingleton<IMandaVerifyCommand>(_manda);
    }

    [Fact]
    public void Renders_heading_and_station_select()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<MandaStationScreen>();

        Assert.Equal("MandA Station", cut.Find("[data-testid=page-heading]").TextContent.Trim());
        Assert.NotNull(cut.Find("[data-testid=station-select]"));
        Assert.NotNull(cut.Find("[data-testid=idle-hint]"));
    }

    [Fact]
    public void Scan_resolves_carton_and_shows_labels()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<MandaStationScreen>();

        cut.Find("[data-testid=scan-input] input").Change("CTN1");
        cut.Find("[data-testid=scan-button]").Click();

        Assert.NotNull(cut.Find("[data-testid=label-table]"));
        Assert.Equal(2, cut.FindAll("[data-testid=slot-check]").Count);
        Assert.Equal(1, _manda.ScanCalls);
    }

    [Fact]
    public void Print_selected_marks_slot_printed()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<MandaStationScreen>();

        cut.Find("[data-testid=scan-input] input").Change("CTN1");
        cut.Find("[data-testid=scan-button]").Click();

        // Select the first slot, then print.
        cut.FindAll("[data-testid=slot-check] input").First().Change(true);
        cut.Find("[data-testid=print-button]").Click();

        Assert.Equal(1, _manda.PrintCalls);
        Assert.Single(_manda.LastPrintedSlots);
        Assert.Equal(1, _manda.LastPrintedSlots[0]);
        // Optimistic UI: the printed slot now shows the "Printed" chip.
        Assert.Contains(cut.FindAll(".mud-chip"), c => c.TextContent.Contains("Printed", StringComparison.Ordinal));
    }

    private sealed class FakeManda : IMandaStationQuery, IMandaScanCommand, IMandaPrintCommand, IMandaVerifyCommand
    {
        public int ScanCalls { get; private set; }
        public int PrintCalls { get; private set; }
        public IReadOnlyList<int> LastPrintedSlots { get; private set; } = [];

        public Task<IReadOnlyList<MandaStation>> GetStationsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MandaStation>>(
            [
                new("S1", "Station 1", "10.0.0.1", 9100),
            ]);

        public Task<MandaScanResult> ScanAsync(string stationId, string scannedBarcode, CancellationToken ct = default)
        {
            ScanCalls++;
            IReadOnlyList<MandaLabel> labels =
            [
                new(1, "Shipping", "SHIP1", "^XA1^XZ", false, false),
                new(2, "Content", "CONT1", "^XA2^XZ", false, false),
            ];
            return Task.FromResult(new MandaScanResult(true, "CTN1", "BLIND1", labels));
        }

        public Task<CommandResult> PrintAsync(string stationId, string cartonId, IReadOnlyList<int> slots, CancellationToken ct = default)
        {
            PrintCalls++;
            LastPrintedSlots = slots;
            return Task.FromResult(CommandResult.Ok("printed"));
        }

        public Task<MandaVerifyResult> VerifyAsync(string stationId, string cartonId, string scannedBarcode, CancellationToken ct = default) =>
            Task.FromResult(new MandaVerifyResult(1, true, "verified"));
    }
}
