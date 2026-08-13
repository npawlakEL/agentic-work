using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using PandA.UI.Contracts.Common;
using PandA.UI.Contracts.Lookup;
using PandA.UI.Contracts.Reprint;
using PandA.UI.Pages;

namespace PandA.UI.Tests;

/// <summary>bUnit render + interaction gate for the Label Data Lookup screen.</summary>
public sealed class LabelLookupTests : TestContext
{
    private readonly FakeLookup _lookup = new();
    private readonly FakeReprint _reprint = new();
    private readonly FakeOperatorContext _operator = new("A. Rivera");

    public LabelLookupTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITransportOrderQuery>(_lookup);
        Services.AddSingleton<ICartonLabelDetailQuery>(_lookup);
        Services.AddSingleton<IReprintAuthorizationCommand>(_reprint);
        Services.AddSingleton<IOperatorContext>(_operator);
    }

    [Fact]
    public void Renders_heading_and_rows()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<LabelLookup>();

        Assert.Equal("Label Data Lookup", cut.Find("[data-testid=page-heading]").TextContent.Trim());
        Assert.NotNull(cut.Find("[data-testid=lookup-table]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=expand-row]"));
    }

    [Fact]
    public void Authorize_reprint_button_invokes_command()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<LabelLookup>();

        var button = cut.Find("[data-testid=authorize-reprint]");
        button.Click();

        Assert.Equal(1, _reprint.CartonCalls);
        Assert.Equal("A. Rivera", _reprint.LastOperator);
    }

    [Fact]
    public void Authorize_reprint_uses_current_operator_from_context()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<LabelLookup>();

        _operator.SetOperator("J. Chen");
        cut.Find("[data-testid=authorize-reprint]").Click();

        Assert.Equal("J. Chen", _reprint.LastOperator);
    }

    private sealed class FakeLookup : ITransportOrderQuery, ICartonLabelDetailQuery
    {
        public Task<PagedResult<TransportOrderRow>> QueryAsync(TransportOrderFilter filter, CancellationToken ct = default)
        {
            IReadOnlyList<TransportOrderRow> rows =
            [
                new("CTN1", "BLIND1", "Held", "Fail", DateTimeOffset.UtcNow, 1, "Map A", "Reject", "W1", true, "L1"),
                new("CTN2", "BLIND2", "Verified", "Pass", DateTimeOffset.UtcNow, 1, "Map A", "Ship", "W1", false, "L1"),
            ];
            return Task.FromResult(new PagedResult<TransportOrderRow>(rows, 2, 0, 200));
        }

        public Task<LookupFilterOptions> GetFilterOptionsAsync(CancellationToken ct = default) =>
            Task.FromResult(new LookupFilterOptions(["Held", "Verified"], ["W1"], ["Map A"], ["L1"], ["P1"]));

        public Task<IReadOnlyList<CartonLabelSlot>> GetLabelSlotsAsync(string cartonId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CartonLabelSlot>>(
            [
                new(1, "Shipping", "SHIP1", "LPN1", "^XA^XZ", true),
            ]);
    }

    private sealed class FakeReprint : IReprintAuthorizationCommand
    {
        public int CartonCalls { get; private set; }

        public string? LastOperator { get; private set; }

        public Task<CommandResult> AuthorizeReprintAsync(string cartonId, string operatorName, CancellationToken ct = default)
        {
            CartonCalls++;
            LastOperator = operatorName;
            return Task.FromResult(CommandResult.Ok("ok"));
        }

        public Task<CommandResult> AuthorizeSlotReprintAsync(string cartonId, int slot, string operatorName, CancellationToken ct = default) =>
            Task.FromResult(CommandResult.Ok("ok"));
    }
}
