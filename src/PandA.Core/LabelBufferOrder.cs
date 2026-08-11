using PandA.Core.Verification;

namespace PandA.Core;

/// <summary>
/// One row of a line's label-buffer-order map: the scanner-buffer <paramref name="Position"/> (1-based)
/// and the <paramref name="LabelType"/> that the value at that position represents. Port of a
/// <c>Settings_LabelBufferOrder</c> row (columns <c>LabelNumber</c>, <c>LabelName</c>).
/// </summary>
/// <param name="Position">1-based position of this label in the scanned buffer.</param>
/// <param name="LabelType">The label type the value at this position represents (e.g. "Shipping").</param>
public sealed record LabelBufferPosition(int Position, string LabelType);

/// <summary>
/// A line's scanner-buffer layout: maps each 1-based buffer position to a label type. This is the C#
/// port of <c>Settings_LabelBufferOrder</c> (keyed by <c>PandaRecID</c> in the source) — configurable
/// <b>per line</b>. It drives verify: the raw scanned buffer (a delimited string of reads) is turned
/// into typed <see cref="ScannedLabel"/>s by joining each read's position to this map, exactly as
/// <c>sdisp_TOOL_PA_VerifyLabel</c> does with <c>STRING_SPLIT</c> + <c>JOIN ON rownum = LabelNumber</c>.
/// </summary>
public sealed class LabelBufferOrder
{
    /// <summary>
    /// The default plant layout seeded in <c>Settings_LabelBufferOrder</c>:
    /// 1=BlindLabel, 2=Shipping, 3=Content, 4=Parcel.
    /// </summary>
    public static LabelBufferOrder Default { get; } = new(
    [
        new LabelBufferPosition(1, "BlindLabel"),
        new LabelBufferPosition(2, "Shipping"),
        new LabelBufferPosition(3, "Content"),
        new LabelBufferPosition(4, "Parcel"),
    ]);

    private readonly Dictionary<int, string> _byPosition;

    /// <summary>Build a buffer-order map from its positions. Positions must be unique and >= 1.</summary>
    /// <exception cref="ArgumentException">A position is &lt; 1, has a blank type, or is duplicated.</exception>
    public LabelBufferOrder(IEnumerable<LabelBufferPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        _byPosition = [];
        foreach (var p in positions)
        {
            if (p.Position < 1)
            {
                throw new ArgumentException($"Buffer position must be >= 1 but was {p.Position}.", nameof(positions));
            }

            if (string.IsNullOrWhiteSpace(p.LabelType))
            {
                throw new ArgumentException($"Buffer position {p.Position} has a blank label type.", nameof(positions));
            }

            if (!_byPosition.TryAdd(p.Position, p.LabelType))
            {
                throw new ArgumentException($"Duplicate buffer position {p.Position}.", nameof(positions));
            }
        }

        Positions = [.. _byPosition
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new LabelBufferPosition(kvp.Key, kvp.Value))];
    }

    /// <summary>The configured positions, ordered by position.</summary>
    public IReadOnlyList<LabelBufferPosition> Positions { get; }

    /// <summary>The highest configured position, or 0 when empty.</summary>
    public int MaxPosition => Positions.Count == 0 ? 0 : Positions[^1].Position;

    /// <summary>
    /// Type a raw scanned buffer (values in 1-based position order) into <see cref="ScannedLabel"/>s.
    /// <para>
    /// Faithful to the source join: a read is emitted only when this map has an entry for its position
    /// (INNER JOIN on <c>rownum = LabelNumber</c>) — reads past the configured positions are dropped.
    /// Empty / whitespace slots represent a scanner position with nothing physically read and are skipped,
    /// so absent labels never inject phantom reads into verify.
    /// </para>
    /// </summary>
    public IReadOnlyList<ScannedLabel> Type(IReadOnlyList<string> buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var result = new List<ScannedLabel>();
        for (var i = 0; i < buffer.Count; i++)
        {
            var value = buffer[i];
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (_byPosition.TryGetValue(i + 1, out var labelType))
            {
                result.Add(new ScannedLabel(labelType, value));
            }
        }

        return result;
    }
}
