using PandA.Core.Settings;
using PandA.Sim;

namespace PandA.Tests;

/// <summary>Shared settings fixtures for tests.</summary>
internal static class TestSettings
{
    /// <summary>
    /// Seeded provider with 'Reprint Labels' = 0, so an already-printed carton needs operator authorization.
    /// The unset/seeded default is now on (source LookupCarton L173-178 fallback '1'; owner ruling 2026-10-21),
    /// so tests that exercise the authorization gate opt out explicitly.
    /// </summary>
    public static InMemorySettingsProvider ReprintOff()
    {
        var settings = new InMemorySettingsProvider();
        settings.SetAsync(KnownSettings.ReprintLabels, false).AsTask().GetAwaiter().GetResult();
        return settings;
    }
}