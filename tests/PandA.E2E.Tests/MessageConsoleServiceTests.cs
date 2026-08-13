using PandA.UI.DemoHost.Sim;

namespace PandA.E2E.Tests;

/// <summary>
/// Fast, browser-free coverage of the Message Console backend: a seeded barcode runs induct → print →
/// verify over real Core services, produces a wire transcript, and captures logger output. Verify reads
/// only what physically printed, so a partial print (a Top label with no Phase-1 printer) fails honestly.
/// </summary>
public sealed class MessageConsoleServiceTests
{
    [Fact]
    public async Task Fully_printable_carton_prints_verifies_and_captures_logs()
    {
        var store = new DemoDataStore();
        var console = new MessageConsoleService(store);
        var lineId = console.DefaultLineId;

        // CTN2000 is Side-only (Shipping + Return) so every label has a printer and verifies clean.
        var result = await console.RunAsync(lineId, "CTN2000");

        Assert.True(result.Success, $"Expected a verified run, got {result.FinalStatus}.\n{Dump(result)}");
        Assert.Equal("Verified", result.FinalStatus);

        Assert.Contains(result.Entries, e => e.Kind == ConsoleEntryKind.InboundFrame && e.Text.Contains("281"));
        Assert.Contains(result.Entries, e => e.Kind == ConsoleEntryKind.InboundFrame && e.Text.Contains("286"));
        Assert.Equal(2, result.Entries.Count(e => e.Kind == ConsoleEntryKind.OutboundPrint));
        Assert.NotEmpty(result.Logs);
    }

    [Fact]
    public async Task Partially_printed_carton_fails_verify()
    {
        var store = new DemoDataStore();
        var console = new MessageConsoleService(store);
        var lineId = console.DefaultLineId;

        // CTN1000 carries a Content (Top) label the Phase-1 induct path can't print, so only Shipping
        // prints. The verify scanner reads only the printed label, so the missing Content fails verify.
        var result = await console.RunAsync(lineId, "CTN1000");

        Assert.False(result.Success, $"Expected a failed verify, got {result.FinalStatus}.\n{Dump(result)}");
        Assert.Single(result.Entries, e => e.Kind == ConsoleEntryKind.OutboundPrint);
        Assert.Contains(result.Entries, e => e.Text.Contains("Induct → PartiallyPrinted"));
    }

    [Fact]
    public void Describe_returns_advised_label_data_for_a_barcode()
    {
        var store = new DemoDataStore();
        var console = new MessageConsoleService(store);

        var carton = console.CartonsForLine(console.DefaultLineId)[0];
        var data = console.Describe(carton.CartonId);

        Assert.NotNull(data);
        Assert.Equal(carton.BlindLabel, data!.BlindLabel);
        Assert.NotEmpty(data.Labels);
    }

    private static string Dump(MessageRunResult result) =>
        string.Join("\n", result.Entries.Select(e => e.Text))
        + "\n--logs--\n"
        + string.Join("\n", result.Logs.Select(l => $"{l.Level} {l.Category} {l.Message}"));
}
