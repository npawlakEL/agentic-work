using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using PandA.UI.Contracts.Common;
using PandA.UI.Contracts.Rejects;
using PandA.UI.Contracts.Reprint;
using PandA.UI.Pages;

namespace PandA.UI.Tests;

/// <summary>bUnit render + interaction gate for the Reject Cartons screen.</summary>
public sealed class RejectCartonsTests : TestContext
{
    private readonly FakeRejects _rejects = new();
    private readonly FakeReprint _reprint = new();
    private readonly FakeOperatorContext _operator = new("M. Okoro");

    public RejectCartonsTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IRejectCartonQuery>(_rejects);
        Services.AddSingleton<IReprintAuthorizationCommand>(_reprint);
        Services.AddSingleton<IOperatorContext>(_operator);
    }

    [Fact]
    public void Renders_heading_and_rows()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<RejectCartons>();

        Assert.Equal("Reject Cartons", cut.Find("[data-testid=page-heading]").TextContent.Trim());
        Assert.NotNull(cut.Find("[data-testid=rejects-table]"));
        Assert.Equal(2, cut.FindAll("[data-testid=authorize-reprint]").Count);
    }

    [Fact]
    public void Authorize_reprint_button_invokes_command()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<RejectCartons>();

        cut.Find("[data-testid=authorize-reprint]").Click();

        Assert.Equal(1, _reprint.CartonCalls);
        Assert.Equal("M. Okoro", _reprint.LastOperator);
    }

    private sealed class FakeRejects : IRejectCartonQuery
    {
        public Task<PagedResult<RejectCartonRow>> QueryAsync(RejectCartonFilter filter, CancellationToken ct = default)
        {
            IReadOnlyList<RejectCartonRow> rows =
            [
                new("CTN1", "BLIND1", "Verify fail", DateTimeOffset.UtcNow, "L1", "Map A", 1),
                new("CTN2", "BLIND2", "No scan", DateTimeOffset.UtcNow, "L2", "Map B", 2),
            ];
            return Task.FromResult(new PagedResult<RejectCartonRow>(rows, 2, 0, 200));
        }

        public Task<IReadOnlyList<string>> GetReasonsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(["No scan", "Verify fail"]);
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
