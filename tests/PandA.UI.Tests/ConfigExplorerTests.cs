using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using PandA.UI.Contracts.Common;
using PandA.UI.Contracts.Config;
using PandA.UI.Pages;

namespace PandA.UI.Tests;

/// <summary>bUnit render + interaction gate for the Config Explorer screen.</summary>
public sealed class ConfigExplorerTests : TestContext
{
    private readonly FakeConfig _config = new();

    public ConfigExplorerTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IConfigTreeQuery>(_config);
        Services.AddSingleton<ISettingsEditor>(_config);
        Services.AddSingleton<ILabelDefEditor>(_config);
        Services.AddSingleton<IMandaStationEditor>(_config);
        Services.AddSingleton<ILineEditor>(_config);
        Services.AddSingleton<IPrinterEditor>(_config);
        Services.AddSingleton<IFirePointEditor>(_config);
        Services.AddSingleton<IMapEditor>(_config);
        Services.AddSingleton<IOrientationEditor>(_config);
    }

    [Fact]
    public void Renders_heading_and_tabs()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<ConfigExplorer>();

        Assert.Equal("Config Explorer", cut.Find("[data-testid=page-heading]").TextContent.Trim());
        Assert.NotNull(cut.Find("[data-testid=config-tabs]"));

        var tabs = cut.FindAll(".mud-tab").Select(t => t.TextContent).ToList();
        Assert.Contains(tabs, t => t.Contains("Lines", StringComparison.Ordinal));
        Assert.Contains(tabs, t => t.Contains("Label Definitions", StringComparison.Ordinal));
        Assert.Contains(tabs, t => t.Contains("Settings", StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_tab_saves_roundtrip()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<ConfigExplorer>();

        cut.FindAll(".mud-tab")
            .First(t => t.TextContent.Contains("Settings", StringComparison.Ordinal))
            .Click();

        cut.Find("[data-testid=save-settings]").Click();

        Assert.Equal(1, _config.SettingsSaves);
        // Round-trip integrity: the draft->DTO mapping must carry the loaded values through Save.
        Assert.NotNull(_config.LastSavedSettings);
        Assert.Equal(3, _config.LastSavedSettings!.VerifyFailThreshold);
        Assert.Equal(0.25, _config.LastSavedSettings!.EncoderResolutionInchesPerPulse);
        Assert.True(_config.LastSavedSettings!.ReprintLabelsEnabled);
    }

    [Fact]
    public void Adding_a_new_label_saves_with_null_id()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<ConfigExplorer>();

        cut.FindAll(".mud-tab")
            .First(t => t.TextContent.Contains("Label Definitions", StringComparison.Ordinal))
            .Click();

        // "Add label" appends a blank, expanded editor card; fill the name and save.
        cut.Find("[data-testid=add-label]").Click();
        var newPanel = cut.FindAll("[data-testid=label-panel]").Last();
        newPanel.QuerySelector("input")!.Change("New Label");
        cut.FindAll("[data-testid=save-label]").Last().Click();

        Assert.Equal(1, _config.LabelSaves);
        Assert.NotNull(_config.LastSavedLabel);
        // A brand-new entity saves with a null id — the editor allocates the real one.
        Assert.Null(_config.LastSavedLabel!.LabelDefId);
    }

    [Fact]
    public void Adding_a_fire_point_saves_with_null_id()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<ConfigExplorer>();

        cut.FindAll(".mud-tab")
            .First(t => t.TextContent.Contains("Fire Points", StringComparison.Ordinal))
            .Click();

        // "Add fire point" appends a blank, expanded editor defaulting to the first label definition.
        cut.Find("[data-testid=add-firepoint]").Click();
        cut.Find("[data-testid=save-firepoint]").Click();

        Assert.Equal(1, _config.FirePointSaves);
        Assert.NotNull(_config.LastSavedFirePoint);
        // A brand-new normalized fire point saves with a null id and the first label definition.
        Assert.Null(_config.LastSavedFirePoint!.FirePointId);
        Assert.Equal("label-1", _config.LastSavedFirePoint!.LabelDefId);
    }

    [Fact]
    public void Lane_builder_walks_through_steps_and_creates_lane()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<ConfigExplorer>();

        // "Add lane" opens the step-by-step builder instead of saving immediately.
        cut.Find("[data-testid=add-lane]").Click();
        Assert.NotNull(cut.Find("[data-testid=lane-builder]"));

        // Step 1: name the lane, then advance to the printers step.
        cut.Find("[data-testid=lane-builder] input").Change("Pack Line 3");
        cut.Find("[data-testid=builder-next]").Click();

        // Step 2: add a printer, then finish.
        cut.Find("[data-testid=builder-add-printer]").Click();
        Assert.NotNull(cut.Find("[data-testid=builder-printer-panel]"));
        cut.Find("[data-testid=builder-finish]").Click();

        // The lane is created and the builder closes back to the toolbar.
        Assert.Equal(1, _config.LineSaves);
        Assert.NotNull(_config.LastSavedLine);
        Assert.Equal("Pack Line 3", _config.LastSavedLine!.Name);
        Assert.Null(_config.LastSavedLine!.LineId);
        Assert.Empty(cut.FindAll("[data-testid=lane-builder]"));
    }

    [Fact]
    public void Lane_builder_next_requires_a_name()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<ConfigExplorer>();

        cut.Find("[data-testid=add-lane]").Click();
        // Advancing without a name is blocked; still on step 1 (no printers step controls).
        cut.Find("[data-testid=builder-next]").Click();

        Assert.NotNull(cut.Find("[data-testid=builder-lane-name]"));
        Assert.Empty(cut.FindAll("[data-testid=builder-add-printer]"));
    }

    [Fact]
    public void Lane_builder_keeps_failed_printers_and_retries_only_them()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<ConfigExplorer>();
        _config.PrinterSaveResult = p => p.PlcNumber == 2 ? CommandResult.Fail("PLC 2 rejected.") : CommandResult.Ok();

        cut.Find("[data-testid=add-lane]").Click();
        cut.Find("[data-testid=lane-builder] input").Change("Pack Line 4");
        cut.Find("[data-testid=builder-next]").Click();
        cut.Find("[data-testid=builder-add-printer]").Click();
        cut.Find("[data-testid=builder-add-printer]").Click();
        cut.Find("[data-testid=builder-finish]").Click();

        // The lane was created once; the good printer saved against the id the editor returned; the failed one stays.
        Assert.Equal(1, _config.LineSaves);
        var saved = Assert.Single(_config.SavedPrinters);
        Assert.Equal("line-new-1", saved.LineId);
        Assert.Equal(1, saved.PlcNumber);
        Assert.NotNull(cut.Find("[data-testid=lane-builder]"));
        Assert.Single(cut.FindAll("[data-testid=builder-printer-panel]"));
        Assert.NotNull(cut.Find("[data-testid=builder-retry-note]"));

        // Retrying only re-saves the failed printer and does not create a second lane.
        _config.PrinterSaveResult = _ => CommandResult.Ok();
        cut.Find("[data-testid=builder-finish]").Click();

        Assert.Equal(1, _config.LineSaves);
        Assert.Equal(2, _config.SavedPrinters.Count);
        Assert.All(_config.SavedPrinters, p => Assert.Equal("line-new-1", p.LineId));
        Assert.Empty(cut.FindAll("[data-testid=lane-builder]"));
    }

    [Fact]
    public void Lane_builder_defaults_new_printer_plc_to_max_plus_one()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<ConfigExplorer>();

        cut.Find("[data-testid=add-lane]").Click();
        cut.Find("[data-testid=lane-builder] input").Change("Pack Line 5");
        cut.Find("[data-testid=builder-next]").Click();
        cut.Find("[data-testid=builder-add-printer]").Click();
        cut.Find("[data-testid=builder-add-printer]").Click();
        cut.Find("[data-testid=builder-add-printer]").Click();
        // Remove the middle printer (PLC 2): the next default must be max+1 = 4, not count+1 = 3.
        cut.FindAll("[data-testid=builder-remove-printer]")[1].Click();
        cut.Find("[data-testid=builder-add-printer]").Click();
        cut.Find("[data-testid=builder-finish]").Click();

        Assert.Equal([1, 3, 4], _config.SavedPrinters.Select(p => p.PlcNumber).OrderBy(n => n).ToArray());
    }

    [Fact]
    public void Set_active_saves_the_maps_line_with_the_map_as_active()
    {
        _config.Lines.Add(Line("L1", "Line 1", activeMapId: null));
        _config.Maps.Add(new MapDto("map-a", "L1", "Map A", []));
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<ConfigExplorer>();

        OpenMapsTab(cut);
        Assert.Empty(cut.FindAll("[data-testid=map-active]"));
        cut.Find("[data-testid=set-active-map]").Click();

        Assert.NotNull(_config.LastSavedLine);
        Assert.Equal("L1", _config.LastSavedLine!.LineId);
        Assert.Equal("map-a", _config.LastSavedLine!.ActiveMapId);
        Assert.Contains("Line 1", cut.Find("[data-testid=map-active]").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid=set-active-map]"));
    }

    [Fact]
    public async Task Changing_a_maps_line_clears_its_assignments()
    {
        _config.Lines.Add(Line("L1", "Line 1", activeMapId: null));
        _config.Lines.Add(Line("L2", "Line 2", activeMapId: null));
        _config.FirePoints.Add(new FirePointDto("fp-1", "label-1", "Middle", 0));
        _config.Maps.Add(new MapDto("map-a", "L1", "Map A", [new FirePointAssignment("fp-1", ["PR1"])]));
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<ConfigExplorer>();

        OpenMapsTab(cut);
        var lineSelect = cut.FindComponents<MudBlazor.MudSelect<string>>()
            .Single(c => c.Instance.UserAttributes.TryGetValue("data-testid", out var id) && Equals(id, "map-line"));
        await cut.InvokeAsync(() => lineSelect.Instance.ValueChanged.InvokeAsync("L2"));
        cut.Find("[data-testid=save-map]").Click();

        var savedMap = Assert.Single(_config.SavedMaps);
        Assert.Equal("L2", savedMap.LineId);
        Assert.Empty(savedMap.Assignments);
    }

    [Fact]
    public void Delete_lane_confirms_then_cascades()
    {
        _config.Lines.Add(Line("L1", "Line 1", activeMapId: null));
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var dialogs = RenderComponent<MudBlazor.MudDialogProvider>();
        var cut = RenderComponent<ConfigExplorer>();

        // Cancel leaves everything in place.
        cut.Find("[data-testid=delete-lane]").Click();
        dialogs.WaitForAssertion(() => Assert.NotEmpty(dialogs.FindAll(".mud-dialog")));
        Assert.Contains("printer", dialogs.Find(".mud-dialog").TextContent, StringComparison.Ordinal);
        dialogs.FindAll(".mud-dialog button").Single(b => b.TextContent.Contains("Cancel", StringComparison.Ordinal)).Click();
        Assert.Empty(_config.CascadeLineDeletes);

        // Confirming uses the cascading delete (never the refusing plain delete).
        cut.Find("[data-testid=delete-lane]").Click();
        dialogs.WaitForAssertion(() => Assert.NotEmpty(dialogs.FindAll(".mud-dialog")));
        dialogs.FindAll(".mud-dialog button").Single(b => b.TextContent.Contains("Delete", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.Equal(["L1"], _config.CascadeLineDeletes));
        Assert.Empty(_config.PlainLineDeletes);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=lane-empty]")));
    }

    private static LineDto Line(string id, string name, string? activeMapId) =>
        new(id, name, 1, [], [], activeMapId, LineControlPolicy.AllowDegraded, [], 0.25, 24, true, false);

    private static void OpenMapsTab(IRenderedComponent<ConfigExplorer> cut)
    {
        cut.FindAll(".mud-tab")
            .First(t => t.TextContent.Contains("Maps", StringComparison.Ordinal))
            .Click();
        cut.Find("[data-testid=map-panel] .mud-expand-panel-header").Click();
    }

    private sealed class FakeConfig
        : IConfigTreeQuery, ISettingsEditor, ILabelDefEditor, IMandaStationEditor,
          ILineEditor, IPrinterEditor, IFirePointEditor, IMapEditor, IOrientationEditor
    {
        public int SettingsSaves { get; private set; }
        public SettingsDto? LastSavedSettings { get; private set; }
        public LabelDefDto? LastSavedLabel { get; private set; }
        public int LabelSaves { get; private set; }
        public int FirePointSaves { get; private set; }
        public FirePointDto? LastSavedFirePoint { get; private set; }
        public int LineSaves { get; private set; }
        public LineDto? LastSavedLine { get; private set; }

        // Configurable backing data for interaction tests (empty by default).
        public List<LineDto> Lines { get; } = [];
        public List<MapDto> Maps { get; } = [];
        public List<FirePointDto> FirePoints { get; } = [];
        public List<PrinterDto> Printers { get; } = [new PrinterDto("PR1", "L1", "Printer 1", "1.1.1.1", 9100, "side-orient", 1, "", "", 0, 0, 0)];
        public List<PrinterDto> SavedPrinters { get; } = [];
        public List<MapDto> SavedMaps { get; } = [];
        public List<string> PlainLineDeletes { get; } = [];
        public List<string> CascadeLineDeletes { get; } = [];
        public Func<PrinterDto, CommandResult> PrinterSaveResult { get; set; } = _ => CommandResult.Ok();
        private int _lineSeq;

        public Task<ConfigTreeNode> GetTreeAsync(CancellationToken ct = default)
        {
            var root = new ConfigTreeNode(ConfigNodeKind.Root, "System", null,
            [
                new ConfigTreeNode(ConfigNodeKind.SettingsGroup, "Settings", "settings", []),
                ConfigTreeNode.Group(ConfigNodeKind.LabelDefinitions, "Label Definitions",
                [
                    new ConfigTreeNode(ConfigNodeKind.LabelDefinition, "Shipping", "label-1", []),
                ]),
            ]);
            return Task.FromResult(root);
        }

        // Settings
        public Task<SettingsDto> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new SettingsDto(0.25, true, 3, 2));

        public Task<CommandResult> SaveAsync(SettingsDto settings, CancellationToken ct = default)
        {
            SettingsSaves++;
            LastSavedSettings = settings;
            return Task.FromResult(CommandResult.Ok("saved"));
        }

        // Label
        Task<IReadOnlyList<LabelDefDto>> IConfigEditor<LabelDefDto>.ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LabelDefDto>>([new LabelDefDto("label-1", "Shipping", "Ship label", 4)]);
        Task<LabelDefDto?> IConfigEditor<LabelDefDto>.GetAsync(string id, CancellationToken ct) =>
            Task.FromResult<LabelDefDto?>(new LabelDefDto(id, "Shipping", "Ship label", 4));
        Task<CommandResult> IConfigEditor<LabelDefDto>.SaveAsync(LabelDefDto e, CancellationToken ct)
        {
            LabelSaves++;
            LastSavedLabel = e;
            return Task.FromResult(CommandResult.Ok());
        }
        Task<CommandResult> IConfigEditor<LabelDefDto>.DeleteAsync(string id, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());

        // MandA station
        Task<IReadOnlyList<MandaStationDto>> IConfigEditor<MandaStationDto>.ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MandaStationDto>>([]);
        Task<MandaStationDto?> IConfigEditor<MandaStationDto>.GetAsync(string id, CancellationToken ct) =>
            Task.FromResult<MandaStationDto?>(new MandaStationDto(id, "S", "1.1.1.1", 9100));
        Task<CommandResult> IConfigEditor<MandaStationDto>.SaveAsync(MandaStationDto e, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());
        Task<CommandResult> IConfigEditor<MandaStationDto>.DeleteAsync(string id, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());

        // Line
        Task<IReadOnlyList<LineDto>> IConfigEditor<LineDto>.ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LineDto>>(Lines.ToList());
        Task<LineDto?> IConfigEditor<LineDto>.GetAsync(string id, CancellationToken ct) =>
            Task.FromResult<LineDto?>(Lines.FirstOrDefault(l => l.LineId == id)
                ?? new LineDto(id, "L", 1, [], [], null, LineControlPolicy.AllowDegraded, [], 0.25, 24, true, false));
        Task<CommandResult> IConfigEditor<LineDto>.SaveAsync(LineDto e, CancellationToken ct)
        {
            LineSaves++;
            LastSavedLine = e;
            var id = e.LineId ?? $"line-new-{++_lineSeq}";
            Lines.RemoveAll(l => l.LineId == id);
            Lines.Add(e with { LineId = id });
            return Task.FromResult(CommandResult.Ok("Saved.", id));
        }
        Task<CommandResult> IConfigEditor<LineDto>.DeleteAsync(string id, CancellationToken ct)
        {
            PlainLineDeletes.Add(id);
            return Task.FromResult(CommandResult.Ok());
        }
        public Task<CommandResult> DeleteWithDependentsAsync(string lineId, CancellationToken ct = default)
        {
            CascadeLineDeletes.Add(lineId);
            Lines.RemoveAll(l => l.LineId == lineId);
            return Task.FromResult(CommandResult.Ok("Deleted lane."));
        }

        // Printer
        Task<IReadOnlyList<PrinterDto>> IConfigEditor<PrinterDto>.ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PrinterDto>>(Printers.ToList());
        Task<PrinterDto?> IConfigEditor<PrinterDto>.GetAsync(string id, CancellationToken ct) =>
            Task.FromResult<PrinterDto?>(new PrinterDto(id, "L1", "P", "1.1.1.1", 9100, "side-orient", 1, "", "", 0, 0, 0));
        Task<CommandResult> IConfigEditor<PrinterDto>.SaveAsync(PrinterDto e, CancellationToken ct)
        {
            var result = PrinterSaveResult(e);
            if (result.Success) { SavedPrinters.Add(e); }
            return Task.FromResult(result);
        }
        Task<CommandResult> IConfigEditor<PrinterDto>.DeleteAsync(string id, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());
        public Task<IReadOnlyList<PrinterDto>> ListForLineAsync(string lineId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PrinterDto>>(Printers.Where(p => p.LineId == lineId).ToList());

        // FirePoint
        Task<IReadOnlyList<FirePointDto>> IConfigEditor<FirePointDto>.ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FirePointDto>>(FirePoints.ToList());
        Task<FirePointDto?> IConfigEditor<FirePointDto>.GetAsync(string id, CancellationToken ct) =>
            Task.FromResult<FirePointDto?>(new FirePointDto(id, "label-1", "Middle", 0));
        Task<CommandResult> IConfigEditor<FirePointDto>.SaveAsync(FirePointDto e, CancellationToken ct)
        {
            FirePointSaves++;
            LastSavedFirePoint = e;
            return Task.FromResult(CommandResult.Ok());
        }
        Task<CommandResult> IConfigEditor<FirePointDto>.DeleteAsync(string id, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());

        // Map
        Task<IReadOnlyList<MapDto>> IConfigEditor<MapDto>.ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MapDto>>(Maps.ToList());
        Task<MapDto?> IConfigEditor<MapDto>.GetAsync(string id, CancellationToken ct) =>
            Task.FromResult<MapDto?>(Maps.FirstOrDefault(m => m.MapId == id) ?? new MapDto(id, "L1", "Map A", []));
        Task<CommandResult> IConfigEditor<MapDto>.SaveAsync(MapDto e, CancellationToken ct)
        {
            SavedMaps.Add(e);
            return Task.FromResult(CommandResult.Ok());
        }
        Task<CommandResult> IConfigEditor<MapDto>.DeleteAsync(string id, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());
        Task<IReadOnlyList<MapDto>> IMapEditor.ListForLineAsync(string lineId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MapDto>>(Maps.Where(m => m.LineId == lineId).ToList());

        // Orientation
        Task<IReadOnlyList<OrientationDto>> IConfigEditor<OrientationDto>.ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OrientationDto>>([new OrientationDto("side-orient", "Side", ApplyMotionKind.Side)]);
        Task<OrientationDto?> IConfigEditor<OrientationDto>.GetAsync(string id, CancellationToken ct) =>
            Task.FromResult<OrientationDto?>(new OrientationDto(id, "Side", ApplyMotionKind.Side));
        Task<CommandResult> IConfigEditor<OrientationDto>.SaveAsync(OrientationDto e, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());
        Task<CommandResult> IConfigEditor<OrientationDto>.DeleteAsync(string id, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());
    }
}
