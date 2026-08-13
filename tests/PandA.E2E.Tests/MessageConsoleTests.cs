using Microsoft.Playwright;

namespace PandA.E2E.Tests;

[Collection("e2e")]
public sealed class MessageConsoleTests(DemoHostFixture fixture)
{
    [Fact]
    public async Task Console_runs_a_barcode_end_to_end_and_shows_transcript_and_logs()
    {
        var page = await fixture.Browser.NewPageAsync();

        var response = await page.GotoAsync(
            fixture.BaseUrl + "/console",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.NotNull(response);
        Assert.True(response!.Ok, $"Expected 2xx for /console, got {response.Status}.");

        // The barcode data panel renders once a carton is auto-selected.
        await page.Locator("[data-testid=console-data]").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 10_000 });

        // Run the end-to-end message flow.
        await page.Locator("[data-testid=console-run]").ClickAsync();

        // The transcript must render the induct + verify steps, and the captured logs panel must appear.
        var transcript = page.Locator("[data-testid=console-transcript-body]");
        await transcript.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await Assertions.Expect(transcript).ToContainTextAsync("281", new() { Timeout = 10_000 });
        await Assertions.Expect(transcript).ToContainTextAsync("Induct", new() { Timeout = 10_000 });

        await page.Locator("[data-testid=console-logs]").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 10_000 });

        Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
    }
}
