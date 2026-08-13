namespace PandA.Core.Zpl;

public static class ZplSanitizer
{
    // Source: sdisp_TOOL_PA_VetLabel.sql lines 33-66. Keep the whole-label removes before ^XA substitution.
    private static readonly string[] StripTokens =
    [
        "^XA^MCY^XZ",
        "^XA^MD-7^XZ",
        "^SZ2",
        "^PRA",
        "^PRB",
        "^PRC",
        "^PRD",
        "^PRE",
        "^PR2",
        "^PR3",
        "^PR4",
        "^PR5",
        "^PR6",
        "^PR8",
        "^PR9",
        "^PON",
        "^PMN",
        "^CI0",
        "^LRN",
        "^JSN",
        "^MMT",
        "^MTT",
        "^MTD",
        "^MD16",
        "^MD0",
        "^MD2",
        "^MNY",
        "^TA000",
        "~TA000",
        "~JSN",
        "^MCN",
        "^PQ1,0,0,N",
    ];

    public static string Sanitize(string? zpl)
    {
        if (string.IsNullOrEmpty(zpl))
        {
            return string.Empty;
        }

        var sanitized = zpl;
        foreach (var token in StripTokens)
        {
            sanitized = sanitized.Replace(token, string.Empty, StringComparison.Ordinal);
        }

        sanitized = sanitized.Replace("^XA", "^XA^LH13,0", StringComparison.Ordinal);
        return sanitized.Replace("^POI^FS", string.Empty, StringComparison.Ordinal);
    }
}
