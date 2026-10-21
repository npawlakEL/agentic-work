namespace PandA.Sim.Messaging;

/// <summary>
/// A raw PandA/PLC message frame: an angle-bracket-wrapped, comma-delimited CSV whose first field is the
/// numeric message code (e.g. <c>&lt;281,1,1,1,1,8,0,0154006001,0,0,0,0,0,0,24092,118&gt;</c>). This is the
/// bare-bones subset of the real VLC↔DB protocol (codes 280–299 route to Process_PA); the harness only
/// speaks 281 (induct scan) and 286 (verify scan) for now.
/// </summary>
public sealed record PaFrame(int Code, IReadOnlyList<string> Fields)
{
    /// <summary>Parse a wire string of the form <c>&lt;code,f1,f2,...&gt;</c> into a frame.</summary>
    public static PaFrame Parse(string wire)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wire);

        var trimmed = wire.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '<' || trimmed[^1] != '>')
        {
            throw new FormatException($"Frame must be wrapped in angle brackets: '{wire}'.");
        }

        var body = trimmed[1..^1];
        var tokens = body.Split(',');
        if (!int.TryParse(tokens[0], out var code))
        {
            throw new FormatException($"Frame code is not numeric: '{tokens[0]}'.");
        }

        return new PaFrame(code, tokens[1..]);
    }

    /// <summary>Field by 1-based param index (param01 = <see cref="Code"/>, param02 = Fields[0]).</summary>
    /// <exception cref="FormatException">The frame is too short to contain the requested field.</exception>
    public string Field(int paramIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(paramIndex, 2);
        if (paramIndex - 2 >= Fields.Count)
        {
            throw new FormatException($"Frame {Code} has {Fields.Count + 1} param(s); param{paramIndex:00} is missing.");
        }

        return Fields[paramIndex - 2];
    }

    /// <summary>Render back to the <c>&lt;code,f1,f2,...&gt;</c> wire form.</summary>
    /// <exception cref="ArgumentException">A field contains a frame delimiter (',', '&lt;' or '&gt;').</exception>
    public string ToWire()
    {
        for (var i = 0; i < Fields.Count; i++)
        {
            EnsureNoDelimiters(Fields[i], FrameDelimiters, $"Fields[{i}]");
        }

        return $"<{Code},{string.Join(',', Fields)}>";
    }

    private static readonly char[] FrameDelimiters = [',', '<', '>'];

    /// <summary>Reject a value containing any of <paramref name="delimiters"/> (the protocol has no escaping).</summary>
    internal static void EnsureNoDelimiters(string? value, char[] delimiters, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        if (value.IndexOfAny(delimiters) >= 0)
        {
            throw new ArgumentException(
                $"Value '{value}' contains a reserved frame delimiter ({string.Join(' ', delimiters)}).",
                paramName);
        }
    }

    /// <summary>Delimiters reserved inside a single label value: frame delimiters plus the label-buffer pipe.</summary>
    internal static readonly char[] LabelDelimiters = [',', '<', '>', '|'];
}
