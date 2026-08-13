namespace PandA.Core;

/// <summary>
/// Appends the Zebra host-status request suffix to a complete ZPL payload.
/// </summary>
public static class ZplStatusSuffix
{
    /// <summary>
    /// Zebra host-status request command appended after the label format.
    /// </summary>
    public const string Suffix = "~HS";

    /// <summary>
    /// Appends <see cref="Suffix"/> at the very end of the ZPL string to request printer host status.
    /// This is not idempotent by design and must be called exactly once per print.
    /// </summary>
    /// <param name="zpl">The complete ZPL payload, typically ending with <c>^XZ</c>.</param>
    /// <returns>The ZPL payload with <see cref="Suffix"/> appended.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zpl"/> is <see langword="null"/>.</exception>
    public static string Append(string zpl)
    {
        ArgumentNullException.ThrowIfNull(zpl);

        return zpl + Suffix;
    }
}
