using Microsoft.Playwright;

namespace PandA.E2E.Tests;

/// <summary>
/// Whole-app flows: navigating between every screen via the shell nav, applying the theme,
/// and carrying operator identity across navigation — the interactions that unit gates can't cover.
/// </summary>
[Collection("e2e")]
public sealed class AppFlowTests(DemoHostFixture fixture)
{
    private static readonly (string Label, string Heading)[] NavTargets =
    [
        ("Dashboard", "Dashboard"),
        ("Config Explorer", "Config Explorer"),
        ("Line Simulator", "Line Simulator"),
        ("Label Lookup", "Label Data Lookup"),
        ("MandA Station", "MandA Station"),
        ("Reject Cartons", "Reject Cartons"),
    ];

    [Fact]
    public async Task Nav_menu_reaches_every_screen_without_error()
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

        await page.GotoAsync(fixture.BaseUrl + "/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        foreach (var (label, heading) in NavTargets)
        {
            await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = label }).ClickAsync();

            // All screens share the page-heading testid, so wait for the TEXT to become the target.
            await Assertions.Expect(page.Locator("[data-testid=page-heading]"))
                .ToHaveTextAsync(heading, new() { Timeout = 10_000 });
            Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync(), $"Error UI after nav to {label}.");
        }

        Assert.True(consoleErrors.Count == 0, $"Console errors during nav:\n{string.Join("\n", consoleErrors)}");
    }

    [Fact]
    public async Task Theme_toggle_flips_dark_mode_state()
    {
        var page = await fixture.Browser.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + "/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var toggle = page.Locator("[data-testid=theme-toggle]");
        await toggle.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        var root = page.Locator("[data-testid=app-root]");
        Assert.Equal("false", await root.GetAttributeAsync("data-dark"));

        await toggle.ClickAsync();
        await Assertions.Expect(root).ToHaveAttributeAsync("data-dark", "true", new() { Timeout = 10_000 });

        Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
    }

    [Fact]
    public async Task Operator_identity_persists_across_navigation()
    {
        var page = await fixture.Browser.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + "/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var input = page.Locator("[data-testid=operator-selector] input");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await input.FillAsync("QA Operator");
        await input.BlurAsync();

        // Navigate away and back; the selector must still show the chosen operator (scoped per circuit).
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Reject Cartons" }).ClickAsync();
        await page.Locator("[data-testid=page-heading]").WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        await Assertions.Expect(page.Locator("[data-testid=operator-selector] input"))
            .ToHaveValueAsync("QA Operator", new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task Config_explorer_creates_a_new_entity_end_to_end()
    {
        var page = await fixture.Browser.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + "/config", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("[data-testid=page-heading]").WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        // Select the "Label Definitions" group, add a new label, fill the name, and save.
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Label Definitions" }).ClickAsync();

        var newButton = page.Locator("[data-testid=new-button]");
        await newButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await newButton.ClickAsync();

        var name = page.Locator("[data-testid=detail-panel] input").First;
        await name.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await name.FillAsync("E2E Test Label");

        await page.Locator("[data-testid=save-button]").ClickAsync();

        // On success the detail returns to the empty state and the new label appears in the tree.
        await page.Locator("[data-testid=detail-empty]").WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "E2E Test Label" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
    }
}
