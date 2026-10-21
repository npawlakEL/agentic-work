namespace PandA.Core.Induct;

/// <summary>
/// Optional fixed MinGap override (tests / Sim). When absent, induct reads the global <c>MinGap</c> setting
/// through <see cref="Settings.ISettingsProvider"/> on every scan.
/// <para>
/// Multi-host (decision-010): this hook is synchronous, so it must return a value it already holds (static
/// configuration). A production adapter should not implement it over the shared database — leave it unset so
/// MinGap comes from the read-through settings provider and an operator change reaches every host.
/// </para>
/// </summary>
public interface IMinGapProvider
{
    int GetMinGap();
}
