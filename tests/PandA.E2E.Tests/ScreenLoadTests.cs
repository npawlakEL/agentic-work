using Microsoft.Playwright;

namespace PandA.E2E.Tests;

/// <summary>
/// Screen-load gate: every route must render server-side, hydrate the Blazor circuit,
/// and reach interactivity with no visible error UI and no browser console errors.
/// </summary>
[Collection("e2e")]
public sealed class ScreenLoadTests(DemoHostFixture fixture)
{
    public static TheoryData<string, string> Routes => new()
    {
        { "/", "Dashboard" },
        { "/lookup", "Label Data Lookup" },
        { "/rejects", "Reject Cartons" },
        { "/manda", "MandA Station" },
        { "/config", "Config Explorer" },
        { "/sim", "Line Simulator" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Route_loads_without_error_ui_or_console_errors(string route, string expectedHeading)
    {
        var consoleErrors = new List<string>();
        var page = await fixture.Browser.NewPageAsync();
        page.Console += (_, msg) =>
        {
            if (string.Equals(msg.Type, "error", StringComparison.Ordinal))
            {
                consoleErrors.Add(msg.Text);
            }
        };

        var response = await page.GotoAsync(
            fixture.BaseUrl + route,
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.NotNull(response);
        Assert.True(response!.Ok, $"Expected 2xx for {route}, got {response.Status}.");

        // The Blazor circuit hydrates; the error UI must stay hidden.
        var errorUi = page.Locator("#blazor-error-ui");
        Assert.False(await errorUi.IsVisibleAsync(), $"Blazor error UI is visible on {route}.");

        // The page's own heading proves the routed component rendered.
        await page.Locator("[data-testid=page-heading]").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 10_000 });
        var heading = await page.Locator("[data-testid=page-heading]").InnerTextAsync();
        Assert.True(string.Equals(expectedHeading, heading.Trim(), StringComparison.Ordinal),
            $"Expected heading '{expectedHeading}' on {route}, got '{heading.Trim()}'.");

        Assert.True(
            consoleErrors.Count == 0,
            $"Console errors on {route}:\n{string.Join("\n", consoleErrors)}");
    }

    [Fact]
    public async Task Theme_toggle_button_is_present_and_clickable()
    {
        var page = await fixture.Browser.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + "/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var toggle = page.Locator("[data-testid=theme-toggle]");
        await toggle.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        Assert.True(await toggle.IsEnabledAsync());

        // Clicking must not throw or surface the error UI.
        await toggle.ClickAsync();
        Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
    }
}
