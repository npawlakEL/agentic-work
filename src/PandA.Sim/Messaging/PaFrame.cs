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
    public string Field(int paramIndex)
    {
        if (paramIndex < 2 || paramIndex - 2 >= Fields.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(paramIndex));
        }

        return Fields[paramIndex - 2];
    }

    /// <summary>Render back to the <c>&lt;code,f1,f2,...&gt;</c> wire form.</summary>
    public string ToWire() => $"<{Code},{string.Join(',', Fields)}>";
}
