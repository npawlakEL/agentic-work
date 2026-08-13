using PandA.Core;

namespace PandA.Tests;

public sealed class ZplStatusSuffixTests
{
    [Fact]
    public void Append_AddsHostStatusSuffix()
    {
        const string zpl = "^XA^FO50,50^ADN,36,20^FDHELLO^FS^XZ";

        var suffixed = ZplStatusSuffix.Append(zpl);

        Assert.Equal("^XA^FO50,50^ADN,36,20^FDHELLO^FS^XZ~HS", suffixed);
    }

    [Fact]
    public void Append_WhenCalledTwice_AppendsSuffixTwice()
    {
        const string zpl = "^XA^FO50,50^ADN,36,20^FDHELLO^FS^XZ";

        var suffixed = ZplStatusSuffix.Append(ZplStatusSuffix.Append(zpl));

        Assert.EndsWith("~HS~HS", suffixed);
    }

    [Fact]
    public void Append_PlacesSuffixAfterFinalEndOfFormat()
    {
        const string zpl = "^XA^FDOne^FS^XZ^XA^FDTwo^FS^XZ";

        var suffixed = ZplStatusSuffix.Append(zpl);

        Assert.Equal(ZplStatusSuffix.Suffix, suffixed[^3..]);
        Assert.True(suffixed.LastIndexOf("^XZ", StringComparison.Ordinal) < suffixed.LastIndexOf(ZplStatusSuffix.Suffix, StringComparison.Ordinal));
    }
}
