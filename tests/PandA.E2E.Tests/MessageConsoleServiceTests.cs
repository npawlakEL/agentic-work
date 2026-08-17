using PandA.UI.DemoHost.Sim;

namespace PandA.E2E.Tests;

/// <summary>
/// Fast, browser-free coverage of the Message Console backend: a seeded barcode runs induct → print →
/// verify over real Core services, produces a wire transcript, and captures logger output. Verify reads
/// only what physically printed, so a partial print (a label whose only printer is offline) fails honestly.
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
    public async Task Content_top_label_routes_to_the_top_printer_and_verifies()
    {
        var store = new DemoDataStore();
        var console = new MessageConsoleService(store);
        var lineId = console.DefaultLineId;

        // CTN1000 carries a Content (Top) label. Selection is orientation-agnostic, so it routes to the
        // line's Top printer alongside the Shipping label — both print and the carton verifies clean.
        var result = await console.RunAsync(lineId, "CTN1000");

        Assert.True(result.Success, $"Expected a verified run, got {result.FinalStatus}.\n{Dump(result)}");
        Assert.Equal("Verified", result.FinalStatus);
        Assert.Equal(2, result.Entries.Count(e => e.Kind == ConsoleEntryKind.OutboundPrint));
        Assert.Contains(result.Entries, e =>
            e.Kind == ConsoleEntryKind.OutboundPrint && e.Text.Contains("Content"));
    }

    [Fact]
    public async Task Partially_printed_carton_fails_verify_when_a_printer_is_offline()
    {
        var store = new DemoDataStore();
        var console = new MessageConsoleService(store);
        var lineId = console.DefaultLineId;

        // Take the default line's only Content (Top) printer offline so its label can't print. Shipping
        // still prints, so the carton prints partially and the verify scan — reading only what printed — fails.
        var topOrientationId = store.Orientations.Values
            .First(o => o.MotionKind == PandA.UI.Contracts.Config.ApplyMotionKind.Top).OrientationId;
        var offline = store.Printers.Values.First(p =>
            string.Equals(p.LineId, lineId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.OrientationId, topOrientationId, StringComparison.OrdinalIgnoreCase));
        store.PrinterRuntime[offline.PrinterId!].Online = false;

        var result = await console.RunAsync(lineId, "CTN1000");

        Assert.False(result.Success, $"Expected a failed verify, got {result.FinalStatus}.\n{Dump(result)}");
        Assert.Single(result.Entries, e => e.Kind == ConsoleEntryKind.OutboundPrint);
        Assert.Contains(result.Entries, e => e.Text.Contains("Induct → PartiallyPrinted"));
    }

    [Fact]
    public async Task Advise_names_the_cartons_real_profile_in_the_transcript()
    {
        var store = new DemoDataStore();
        var console = new MessageConsoleService(store);
        var lineId = console.DefaultLineId;

        // CTN1000 carries the line's map name as its ProfileName; the advise transcript now names it
        // (not "line default") and the run still prints + verifies through the named-profile lookup.
        var profileName = store.Cartons["CTN1000"].ProfileName;
        var result = await console.RunAsync(lineId, "CTN1000");

        Assert.True(result.Success, $"Expected a verified run, got {result.FinalStatus}.\n{Dump(result)}");
        Assert.Contains(result.Entries, e =>
            e.Kind == ConsoleEntryKind.Info && e.Text.Contains($"profile {profileName}"));
    }

    [Fact]
    public async Task Unknown_profile_prints_nothing_and_fails()
    {
        var store = new DemoDataStore();

        // Advise a profile name the line's registry doesn't know (PROFSW/F19): induct resolves NoProfile
        // and prints nothing, so the run stops before verify and fails.
        store.Cartons["CTN1000"].ProfileName = "Nonexistent Profile";

        var console = new MessageConsoleService(store);
        var result = await console.RunAsync(console.DefaultLineId, "CTN1000");

        Assert.False(result.Success, $"Expected a failed run, got {result.FinalStatus}.\n{Dump(result)}");
        Assert.DoesNotContain(result.Entries, e => e.Kind == ConsoleEntryKind.OutboundPrint);
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

    [Fact]
    public async Task Non_default_line_also_has_a_green_runnable_carton()
    {
        var store = new DemoDataStore();
        var console = new MessageConsoleService(store);
        var lines = console.Lines();

        // Every line must have its OWN seeded, green-runnable carton — not just the default line. Regression
        // for the old .Values.First() seed that put all cartons (and a single line's profile) on one line,
        // leaving other lines dead-ending at NoProfile.
        Assert.True(lines.Count >= 2, "Expected at least two selectable lines.");
        foreach (var (lineId, _) in lines)
        {
            var barcodes = console.CartonsForLine(lineId);
            var sideOnly = barcodes.First(c =>
                store.Cartons[c.CartonId].Slots.All(s =>
                    !string.Equals(s.LabelType, "Content", StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(store.Cartons[c.CartonId].LineId, lineId, StringComparison.OrdinalIgnoreCase));

            var result = await console.RunAsync(lineId, sideOnly.CartonId);

            Assert.True(result.Success, $"Line {lineId} carton {sideOnly.CartonId} did not verify.\n{Dump(result)}");
            Assert.Equal("Verified", result.FinalStatus);
        }
    }

    [Fact]
    public void Every_seeded_carton_carries_its_own_lines_active_profile()
    {
        var store = new DemoDataStore();

        // Guards the line/profile-agreement invariant: a carton's ProfileName must be its own line's
        // active-map name, or induct resolves NoProfile and nothing prints.
        foreach (var carton in store.Cartons.Values)
        {
            var line = store.Lines.Values.First(l =>
                string.Equals(l.LineId, carton.LineId, StringComparison.OrdinalIgnoreCase));
            var activeMap = store.Maps[line.ActiveMapId!];

            Assert.Equal(activeMap.Name, carton.ProfileName);
        }
    }

    private static string Dump(MessageRunResult result) =>
        string.Join("\n", result.Entries.Select(e => e.Text))
        + "\n--logs--\n"
        + string.Join("\n", result.Logs.Select(l => $"{l.Level} {l.Category} {l.Message}"));
}
