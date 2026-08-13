using PandA.Core.Zpl;
using Xunit;

namespace PandA.Tests;

public sealed class ZplSanitizerTests
{
    [Theory]
    [InlineData("^XA^SZ2^PR4^FO0,0^FDHello^FS^XZ", "^XA^LH13,0^FO0,0^FDHello^FS^XZ")]
    [InlineData("^XA^MCY^XZ^XA^FO0,0^FDTest^FS^XZ", "^XA^LH13,0^FO0,0^FDTest^FS^XZ")]
    [InlineData("^XA^MD-7^XZ^XA^PON^LRN^FDLabel^FS^XZ", "^XA^LH13,0^FDLabel^FS^XZ")]
    [InlineData("^XA^PQ1,0,0,N^POI^FS^FDFoo^FS^XZ", "^XA^LH13,0^FDFoo^FS^XZ")]
    [InlineData("^XA~TA000~JSN^FDBar^FS^XZ", "^XA^LH13,0^FDBar^FS^XZ")]
    [InlineData("^XA^MD2^MD0^MD16^FDTest^FS^XZ", "^XA^LH13,0^FDTest^FS^XZ")]
    [InlineData("^XA^PR2^PR2^FDFoo^FS^XZ", "^XA^LH13,0^FDFoo^FS^XZ")]
    public void Sanitize_StripsSourceVetLabelTokensAndInjectsLabelHome(string input, string expected)
    {
        Assert.Equal(expected, ZplSanitizer.Sanitize(input));
    }

    [Fact]
    public void Sanitize_InjectsLabelHomeIntoEachLabelBlock()
    {
        var sanitized = ZplSanitizer.Sanitize("^XA^FDCopy1^FS^XZ^XA^FDCopy2^FS^XZ");

        Assert.Equal("^XA^LH13,0^FDCopy1^FS^XZ^XA^LH13,0^FDCopy2^FS^XZ", sanitized);
    }

    [Fact]
    public void Sanitize_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ZplSanitizer.Sanitize(null));
        Assert.Equal(string.Empty, ZplSanitizer.Sanitize(string.Empty));
    }
}
