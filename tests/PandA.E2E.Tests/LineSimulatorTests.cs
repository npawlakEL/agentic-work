using Microsoft.Playwright;

namespace PandA.E2E.Tests;

[Collection("e2e")]
public sealed class LineSimulatorTests(DemoHostFixture fixture)
{
    [Fact]
    public async Task Sim_page_starts_and_spawns_a_carton()
    {
        var page = await fixture.Browser.NewPageAsync();

        var response = await page.GotoAsync(
            fixture.BaseUrl + "/sim",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.NotNull(response);
        Assert.True(response!.Ok, $"Expected 2xx for /sim, got {response.Status}.");

        await page.Locator("[data-testid=sim-scene-container]").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 10_000 });
        await page.Locator("[data-testid=sim-reset-view]").ClickAsync();
        await page.GetByText("Sim Settings").ClickAsync();
        await page.Locator("[data-testid=sim-eye-count]").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 10_000 });
        await page.Locator("[data-testid=sim-encoder-resolution]").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("[data-testid=sim-carton-count]"))
            .ToHaveTextAsync("Cartons: 0", new() { Timeout = 10_000 });

        await page.Locator("[data-testid=sim-start]").ClickAsync();
        await page.Locator("[data-testid=sim-spawn]").ClickAsync();

        await Assertions.Expect(page.Locator("[data-testid=sim-carton-count]"))
            .ToContainTextAsync("Cartons: 1", new() { Timeout = 10_000 });
        Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
    }
}
