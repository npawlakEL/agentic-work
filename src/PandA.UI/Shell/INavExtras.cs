namespace PandA.UI.Shell;

/// <summary>
/// Host-supplied navigation entries appended after the core <see cref="NavModel.Entries"/>.
/// Lets a host contribute its own screens (e.g. the DemoHost's Line Simulator) without the
/// reusable <c>PandA.UI</c> library hardcoding routes to pages it does not own. Optional: when
/// no implementation is registered, only the core routes are shown.
/// </summary>
public interface INavExtras
{
    IReadOnlyList<NavModel.NavEntry> Entries { get; }
}
