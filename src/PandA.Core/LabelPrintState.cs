namespace PandA.Core;

/// <summary>
/// Mutable per-label-type print outcome tracked on a <see cref="TransportOrder"/> (separate from the
/// immutable advised <see cref="Label"/> data — decision-003). Lets us see which label types actually
/// printed, to which printer, and when; enables reprint-only-missing later.
/// </summary>
public sealed class LabelPrintState
{
    public LabelPrintState(string labelType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labelType);
        LabelType = labelType;
    }

    public string LabelType { get; }

    public bool Printed { get; private set; }

    public string? PrinterId { get; private set; }

    public DateTimeOffset? PrintedAt { get; private set; }

    public void MarkPrinted(string printerId, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        Printed = true;
        PrinterId = printerId;
        PrintedAt = at;
    }

    /// <summary>Clear print outcome for a fresh run (advised set changed).</summary>
    public void Reset()
    {
        Printed = false;
        PrinterId = null;
        PrintedAt = null;
    }
}
