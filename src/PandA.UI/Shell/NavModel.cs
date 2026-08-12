using Microsoft.AspNetCore.Components;

namespace PandA.UI.Shell;

/// <summary>
/// The fixed navigation model for the PandA operator UI. Kept in one place so the
/// layout and any tests share a single source of truth for routes.
/// </summary>
public static class NavModel
{
    /// <summary>A single left-nav destination in the PandA shell.</summary>
    /// <param name="Label">Human-readable menu text.</param>
    /// <param name="Href">Route the entry navigates to.</param>
    /// <param name="Icon">MudBlazor/Axon icon markup (SVG path constant).</param>
    public sealed record NavEntry(string Label, string Href, string Icon);

    public static readonly IReadOnlyList<NavEntry> Entries =
    [
        new("Dashboard", "/", MudBlazor.Icons.Material.Filled.Dashboard),
        new("Config Explorer", "/config", MudBlazor.Icons.Material.Filled.AccountTree),
        new("Label Lookup", "/lookup", MudBlazor.Icons.Material.Filled.Search),
        new("MandA Station", "/manda", MudBlazor.Icons.Material.Filled.QrCodeScanner),
        new("Reject Cartons", "/rejects", MudBlazor.Icons.Material.Filled.Report),
    ];
}
