using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using PandA.UI.Pages;
using PandA.UI.Shell;

namespace PandA.UI.Tests;

/// <summary>
/// bUnit render gate: routed components must render their content and controls without throwing.
/// </summary>
public sealed class DashboardTests : TestContext
{
    public DashboardTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Dashboard_renders_heading_and_status_cards()
    {
        var cut = RenderComponent<Dashboard>();

        var heading = cut.Find("[data-testid=page-heading]");
        Assert.Equal("Dashboard", heading.TextContent.Trim());

        var cards = cut.FindAll("[data-testid=dashboard-card]");
        Assert.Equal(3, cards.Count);
    }

    [Fact]
    public void NavModel_exposes_the_five_screen_routes()
    {
        Assert.Equal(5, NavModel.Entries.Count);
        Assert.Contains(NavModel.Entries, e => string.Equals(e.Href, "/", StringComparison.Ordinal));
        Assert.Contains(NavModel.Entries, e => string.Equals(e.Href, "/config", StringComparison.Ordinal));
        Assert.Contains(NavModel.Entries, e => string.Equals(e.Href, "/manda", StringComparison.Ordinal));
    }
}
