using PandA.UI.Shell;

namespace PandA.UI.DemoHost.Sim;

/// <summary>
/// Host-specific nav entries for the DemoHost. The Line Simulator page lives in this host
/// (not the reusable PandA.UI library), so the host contributes its own nav link rather than
/// the library hardcoding a route to a page it does not ship.
/// </summary>
public sealed class DemoNavExtras : INavExtras
{
    public IReadOnlyList<NavModel.NavEntry> Entries { get; } =
    [
        new("Line Simulator", "/sim", MudBlazor.Icons.Material.Filled.ViewInAr),
    ];
}
