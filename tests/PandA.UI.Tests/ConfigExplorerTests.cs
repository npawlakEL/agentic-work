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
        cut.FindAll("[data-testid=label-panel] input").First().Change("New Label");
        cut.Find("[data-testid=save-label]").Click();

        Assert.Equal(1, _config.LabelSaves);
        Assert.NotNull(_config.LastSavedLabel);
        // A brand-new entity saves with a null id — the editor allocates the real one.
        Assert.Null(_config.LastSavedLabel!.LabelDefId);
    }

    [Fact]
    public void Adding_a_fire_point_saves_with_null_id_and_printer()
    {
        RenderComponent<MudBlazor.MudPopoverProvider>();
        var cut = RenderComponent<ConfigExplorer>();

        cut.FindAll(".mud-tab")
            .First(t => t.TextContent.Contains("Fire Points", StringComparison.Ordinal))
            .Click();

        // "Add fire point" appends a blank, expanded editor defaulting to the first printer.
        cut.Find("[data-testid=add-firepoint]").Click();
        cut.Find("[data-testid=save-firepoint]").Click();

        Assert.Equal(1, _config.FirePointSaves);
        Assert.NotNull(_config.LastSavedFirePoint);
        // A brand-new fire point saves with a null id and is associated to the first printer.
        Assert.Null(_config.LastSavedFirePoint!.FirePointId);
        Assert.Equal("PR1", _config.LastSavedFirePoint!.PrinterId);
    }

    private sealed class FakeConfig
        : IConfigTreeQuery, ISettingsEditor, ILabelDefEditor, IMandaStationEditor,
          ILineEditor, IPrinterEditor, IFirePointEditor, IMapEditor
    {
        public int SettingsSaves { get; private set; }
        public SettingsDto? LastSavedSettings { get; private set; }
        public LabelDefDto? LastSavedLabel { get; private set; }
        public int LabelSaves { get; private set; }
        public int FirePointSaves { get; private set; }
        public FirePointDto? LastSavedFirePoint { get; private set; }

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
            Task.FromResult(new SettingsDto(0.25, true, true, false, 3, 2));

        public Task<CommandResult> SaveAsync(SettingsDto settings, CancellationToken ct = default)
        {
            SettingsSaves++;
            LastSavedSettings = settings;
            return Task.FromResult(CommandResult.Ok("saved"));
        }

        // Label
        Task<IReadOnlyList<LabelDefDto>> IConfigEditor<LabelDefDto>.ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LabelDefDto>>([]);
        Task<LabelDefDto?> IConfigEditor<LabelDefDto>.GetAsync(string id, CancellationToken ct) =>
            Task.FromResult<LabelDefDto?>(new LabelDefDto(id, "Shipping", "Side", 0));
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
            Task.FromResult<IReadOnlyList<LineDto>>([]);
        Task<LineDto?> IConfigEditor<LineDto>.GetAsync(string id, CancellationToken ct) =>
            Task.FromResult<LineDto?>(new LineDto(id, "L", [], [], [], null, LineControlPolicy.AllowDegraded, 1));
        Task<CommandResult> IConfigEditor<LineDto>.SaveAsync(LineDto e, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());
        Task<CommandResult> IConfigEditor<LineDto>.DeleteAsync(string id, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());

        // Printer
        Task<IReadOnlyList<PrinterDto>> IConfigEditor<PrinterDto>.ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PrinterDto>>([new PrinterDto("PR1", "L1", "Printer 1", "1.1.1.1", 9100, "Side", [], 0, 0.25, 4, true, 0, 0, 0)]);
        Task<PrinterDto?> IConfigEditor<PrinterDto>.GetAsync(string id, CancellationToken ct) =>
            Task.FromResult<PrinterDto?>(new PrinterDto(id, "L1", "P", "1.1.1.1", 9100, "Side", [], 0, 0.25, 4, true, 0, 0, 0));
        Task<CommandResult> IConfigEditor<PrinterDto>.SaveAsync(PrinterDto e, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());
        Task<CommandResult> IConfigEditor<PrinterDto>.DeleteAsync(string id, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());
        public Task<IReadOnlyList<PrinterDto>> ListForLineAsync(string lineId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PrinterDto>>([]);

        // FirePoint
        Task<IReadOnlyList<FirePointDto>> IConfigEditor<FirePointDto>.ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FirePointDto>>([]);
        Task<FirePointDto?> IConfigEditor<FirePointDto>.GetAsync(string id, CancellationToken ct) =>
            Task.FromResult<FirePointDto?>(new FirePointDto(id, "P1", "Shipping", "TD1", 0, "TD2", "0M", false));
        Task<CommandResult> IConfigEditor<FirePointDto>.SaveAsync(FirePointDto e, CancellationToken ct)
        {
            FirePointSaves++;
            LastSavedFirePoint = e;
            return Task.FromResult(CommandResult.Ok());
        }
        Task<CommandResult> IConfigEditor<FirePointDto>.DeleteAsync(string id, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());
        public Task<IReadOnlyList<FirePointDto>> ListForPrinterAsync(string printerId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FirePointDto>>([]);

        // Map
        Task<IReadOnlyList<MapDto>> IConfigEditor<MapDto>.ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MapDto>>([]);
        Task<MapDto?> IConfigEditor<MapDto>.GetAsync(string id, CancellationToken ct) =>
            Task.FromResult<MapDto?>(new MapDto(id, "L1", "Map A", []));
        Task<CommandResult> IConfigEditor<MapDto>.SaveAsync(MapDto e, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());
        Task<CommandResult> IConfigEditor<MapDto>.DeleteAsync(string id, CancellationToken ct) =>
            Task.FromResult(CommandResult.Ok());
        Task<IReadOnlyList<MapDto>> IMapEditor.ListForLineAsync(string lineId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MapDto>>([]);
    }
}
