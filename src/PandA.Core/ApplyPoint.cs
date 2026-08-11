using System.Globalization;

namespace PandA.Core;

/// <summary>
/// The edge a label's apply fire point is measured from. Ports <c>LabelPrintLocations</c>
/// (<c>CharacterPrefix</c> → meaning).
/// </summary>
public enum Edge
{
    /// <summary>Leading edge — source code <c>L</c>.</summary>
    Leading,

    /// <summary>Trailing edge — source code <c>T</c>.</summary>
    Trailing,

    /// <summary>Middle (no edge offset) — source code <c>M</c>. The only edge that allows a negative offset.</summary>
    Middle,
}

/// <summary>
/// A label's <b>apply</b> fire point in the source's human notation: a signed decimal number of
/// inches plus an edge letter, e.g. <c>1T</c> (1" from trailing), <c>1L</c> (1" from leading),
/// <c>0M</c> (dead-centre), <c>.4M</c>, <c>-.4M</c>, <c>5.25L</c>. Ports the
/// <c>PrinterFirePoints.ApplyFirePoint varchar</c> column and its decode in
/// <c>sdivw_LabelProfiles</c> (<c>SUBSTRING</c> for the number, <c>RIGHT(...,1)</c> for the edge).
/// </summary>
public sealed record ApplyPoint
{
    /// <summary>Create an apply point, validating the source's sign/edge rule.</summary>
    /// <param name="inches">Distance from the edge, in inches. Negative is only valid for <see cref="Edge.Middle"/>.</param>
    /// <param name="edge">The edge the distance is measured from.</param>
    /// <exception cref="ArgumentException">A negative distance is used with a non-Middle edge.</exception>
    public ApplyPoint(decimal inches, Edge edge)
    {
        if (inches < 0 && edge != Edge.Middle)
        {
            throw new ArgumentException(
                $"A negative apply point ({inches}) is only valid for the Middle edge, not {edge}.",
                nameof(inches));
        }

        Inches = inches;
        Edge = edge;
    }

    /// <summary>Distance from the edge, in inches. Negative only for <see cref="Edge.Middle"/>.</summary>
    public decimal Inches { get; }

    /// <summary>The edge the distance is measured from.</summary>
    public Edge Edge { get; }

    /// <summary>Parse the source notation (<c>&lt;signed-decimal&gt;&lt;edge-letter&gt;</c>) into an <see cref="ApplyPoint"/>.</summary>
    /// <exception cref="FormatException">The text is empty, has an unknown edge letter, or a non-numeric distance.</exception>
    public static ApplyPoint Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var trimmed = text.Trim();
        var edge = char.ToUpperInvariant(trimmed[^1]) switch
        {
            'L' => Edge.Leading,
            'T' => Edge.Trailing,
            'M' => Edge.Middle,
            _ => throw new FormatException($"Unknown apply-point edge letter in '{text}'; expected L, T, or M."),
        };

        var numberPart = trimmed[..^1];
        if (!decimal.TryParse(numberPart, NumberStyles.Number, CultureInfo.InvariantCulture, out var inches))
        {
            throw new FormatException($"Invalid apply-point distance '{numberPart}' in '{text}'.");
        }

        return new ApplyPoint(inches, edge);
    }

    /// <summary>Render back to the source notation (e.g. <c>1T</c>, <c>-.4M</c>), round-tripping <see cref="Parse"/>.</summary>
    public override string ToString()
    {
        var letter = Edge switch
        {
            Edge.Leading => 'L',
            Edge.Trailing => 'T',
            _ => 'M',
        };

        return Inches.ToString(CultureInfo.InvariantCulture) + letter;
    }
}
