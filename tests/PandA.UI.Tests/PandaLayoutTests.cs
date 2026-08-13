using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using PandA.UI.Contracts.Common;
using PandA.UI.Shell;

namespace PandA.UI.Tests;

/// <summary>bUnit gate for the shell chrome: operator selector and theme toggle wiring.</summary>
public sealed class PandaLayoutTests : TestContext
{
    private readonly FakeOperatorContext _operator = new("A. Rivera");

    public PandaLayoutTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IOperatorContext>(_operator);
    }

    [Fact]
    public void Renders_shell_chrome_with_operator_and_theme_toggle()
    {
        var cut = RenderComponent<PandaLayout>();

        Assert.NotNull(cut.Find("[data-testid=operator-selector]"));
        Assert.NotNull(cut.Find("[data-testid=theme-toggle]"));
        Assert.NotNull(cut.Find("[data-testid=nav-toggle]"));
    }

    [Fact]
    public void Editing_operator_selector_updates_the_context()
    {
        var cut = RenderComponent<PandaLayout>();

        var input = cut.Find("[data-testid=operator-selector] input");
        input.Input("J. Chen");

        Assert.Equal("J. Chen", _operator.CurrentOperator);
    }

    [Fact]
    public void Theme_toggle_flips_the_layout_dark_state()
    {
        var cut = RenderComponent<PandaLayout>();

        var root = cut.Find("[data-testid=app-root]");
        Assert.Equal("false", root.GetAttribute("data-dark"));

        cut.Find("[data-testid=theme-toggle]").Click();

        Assert.Equal("true", cut.Find("[data-testid=app-root]").GetAttribute("data-dark"));
    }

    [Fact]
    public void Context_change_refreshes_the_selector_value()
    {
        var cut = RenderComponent<PandaLayout>();

        _operator.SetOperator("S. Patel");
        cut.Render();

        var input = cut.Find("[data-testid=operator-selector] input");
        Assert.Equal("S. Patel", input.GetAttribute("value"));
    }
}
