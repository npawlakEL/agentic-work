using System.Runtime.CompilerServices;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using PandA.UI.Contracts.Status;
using PandA.UI.Pages;
using PandA.UI.Shell;

namespace PandA.UI.Tests;

/// <summary>
/// bUnit render gate for the Status Dashboard: the routed component must render the injected
/// line/printer status without throwing.
/// </summary>
public sealed class DashboardTests : TestContext
{
    public DashboardTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var streams = new FakeStatusStreams();
        Services.AddSingleton<ILineStatusStream>(streams);
        Services.AddSingleton<IPrinterStatusStream>(streams);
    }

    [Fact]
    public void Dashboard_renders_heading_and_line_cards()
    {
        var cut = RenderComponent<Dashboard>();

        var heading = cut.Find("[data-testid=page-heading]");
        Assert.Equal("Dashboard", heading.TextContent.Trim());

        var cards = cut.FindAll("[data-testid=line-card]");
        Assert.Equal(2, cards.Count);

        Assert.NotNull(cut.Find("[data-testid=printer-table]"));
    }

    [Fact]
    public void NavModel_exposes_the_five_screen_routes()
    {
        Assert.Equal(5, NavModel.Entries.Count);
        Assert.Contains(NavModel.Entries, e => string.Equals(e.Href, "/", StringComparison.Ordinal));
        Assert.Contains(NavModel.Entries, e => string.Equals(e.Href, "/config", StringComparison.Ordinal));
        Assert.Contains(NavModel.Entries, e => string.Equals(e.Href, "/manda", StringComparison.Ordinal));
    }

    private sealed class FakeStatusStreams : ILineStatusStream, IPrinterStatusStream
    {
        public Task<IReadOnlyList<LineStatus>> GetSnapshotAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LineStatus>>(
            [
                new LineStatus("L1", "Line 1", LineRunState.Running, "", "Map A", 2, 2),
                new LineStatus("L2", "Line 2", LineRunState.Slow, "Degraded", "Map B", 2, 1),
            ]);

        Task<IReadOnlyList<PrinterStatus>> IPrinterStatusStream.GetSnapshotAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PrinterStatus>>(
            [
                new PrinterStatus("P1", "L1-Ship1", "L1", true, false, "Side", 0, 3, ["Shipping"], DateTimeOffset.UtcNow),
                new PrinterStatus("P2", "L1-Cont1", "L1", true, true, "Top", 0, 3, ["Content"], null),
            ]);

        public async IAsyncEnumerable<LineStatus> SubscribeAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        async IAsyncEnumerable<PrinterStatus> IPrinterStatusStream.SubscribeAsync([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
