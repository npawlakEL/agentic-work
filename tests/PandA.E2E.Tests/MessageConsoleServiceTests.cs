using PandA.UI.DemoHost.Sim;

namespace PandA.E2E.Tests;

/// <summary>
/// Fast, browser-free coverage of the Message Console backend: a seeded barcode runs induct → print →
/// verify over real Core services, produces a wire transcript, and captures logger output.
/// </summary>
public sealed class MessageConsoleServiceTests
{
    [Fact]
    public async Task Run_prints_then_verifies_and_captures_logs()
    {
        var store = new DemoDataStore();
        var console = new MessageConsoleService(store);

        var lineId = console.DefaultLineId;
        var carton = console.CartonsForLine(lineId)[0];

        var result = await console.RunAsync(lineId, carton.CartonId);

        Assert.True(result.Success, $"Expected a verified run, got {result.FinalStatus}.");
        Assert.Equal("Verified", result.FinalStatus);

        // The transcript carries both inbound frames and the outbound print(s).
        Assert.Contains(result.Entries, e => e.Kind == ConsoleEntryKind.InboundFrame && e.Text.Contains("281"));
        Assert.Contains(result.Entries, e => e.Kind == ConsoleEntryKind.InboundFrame && e.Text.Contains("286"));
        Assert.Contains(result.Entries, e => e.Kind == ConsoleEntryKind.OutboundPrint);

        // The real services logged during the run.
        Assert.NotEmpty(result.Logs);
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
}
