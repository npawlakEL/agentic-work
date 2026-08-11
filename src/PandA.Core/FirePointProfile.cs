namespace PandA.Core;

/// <summary>
/// A named set of fire points — one <see cref="FirePoint"/> per <c>(printer, label type)</c> slot.
/// Ports <c>LabelProfileHeader</c> + <c>LabelProfileDetail</c> → <c>PrinterFirePoints</c>: the header
/// names the profile, the details collect the printer/label fire points. Multiple profiles ride on
/// top of the same line map (<c>LabelProfileMap</c>); this slice carries one static profile per line.
/// </summary>
public sealed class FirePointProfile
{
    private readonly Dictionary<(string PrinterId, string LabelType), FirePoint> _byKey;

    /// <summary>Build a profile from its per-printer-per-label fire points.</summary>
    /// <param name="name">The profile name (source <c>LabelProfileHeader.ProfileName</c>).</param>
    /// <param name="firePoints">One entry per <c>(printerId, labelType)</c> slot. Keys are case-insensitive.</param>
    /// <exception cref="ArgumentException">The name is blank or a <c>(printer, label)</c> slot is duplicated.</exception>
    public FirePointProfile(string name, IEnumerable<((string PrinterId, string LabelType) Key, FirePoint FirePoint)> firePoints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(firePoints);

        Name = name;
        _byKey = new Dictionary<(string, string), FirePoint>(FirePointKeyComparer.Instance);
        foreach (var (key, firePoint) in firePoints)
        {
            ArgumentNullException.ThrowIfNull(firePoint);
            ArgumentException.ThrowIfNullOrWhiteSpace(key.PrinterId);
            ArgumentException.ThrowIfNullOrWhiteSpace(key.LabelType);

            if (!_byKey.TryAdd(key, firePoint))
            {
                throw new ArgumentException(
                    $"Duplicate fire point for printer '{key.PrinterId}' + label '{key.LabelType}' in profile '{name}'.",
                    nameof(firePoints));
            }
        }
    }

    /// <summary>The profile's name.</summary>
    public string Name { get; }

    /// <summary>The configured <c>(printer, label)</c> slots.</summary>
    public IReadOnlyCollection<(string PrinterId, string LabelType)> Slots => _byKey.Keys;

    /// <summary>Look up the fire point for a printer + label type. Case-insensitive on both.</summary>
    public bool TryGet(string printerId, string labelType, out FirePoint firePoint) =>
        _byKey.TryGetValue((printerId, labelType), out firePoint!);

    private sealed class FirePointKeyComparer : IEqualityComparer<(string PrinterId, string LabelType)>
    {
        public static FirePointKeyComparer Instance { get; } = new();

        public bool Equals((string PrinterId, string LabelType) x, (string PrinterId, string LabelType) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.PrinterId, y.PrinterId)
            && StringComparer.OrdinalIgnoreCase.Equals(x.LabelType, y.LabelType);

        public int GetHashCode((string PrinterId, string LabelType) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PrinterId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.LabelType));
    }
}
