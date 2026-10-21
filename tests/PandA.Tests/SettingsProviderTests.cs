using PandA.Core.Settings;
using PandA.Sim;

namespace PandA.Tests;

public sealed class SettingsProviderTests
{
    [Fact]
    public async Task SeededProvider_ReturnsKnownSeedDefaults()
    {
        var provider = new InMemorySettingsProvider();

        var reprint = await provider.GetAsync(KnownSettings.ReprintLabels);
        var minGap = await provider.GetAsync(KnownSettings.MinGap);

        // Source LookupCarton falls back to '1' when 'Reprint Labels' is unset (owner ruling 2026-10-21).
        Assert.True(reprint);
        Assert.Equal(20, minGap);
    }

    [Fact]
    public async Task SetAsync_PersistsValue()
    {
        var provider = new InMemorySettingsProvider();

        await provider.SetAsync(KnownSettings.ReprintLabels, true);

        Assert.True(await provider.GetAsync("Reprint Labels", defaultValue: false));
    }

    [Fact]
    public async Task AbsentSetting_ReturnsCallerDefault()
    {
        var provider = new InMemorySettingsProvider(seedKnownSettings: false);

        var value = await provider.GetAsync("Missing", defaultValue: 42);

        Assert.Equal(42, value);
    }
}
