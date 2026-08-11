namespace PandA.Core;

/// <summary>
/// The two firing events for one label on one printer: <b>print</b> (the printer emits the label)
/// then <b>apply</b> (the PLC fires the TAMP head to stick it on the carton). Print always precedes
/// apply by physical necessity. Ports a <c>PrinterFirePoints</c> row.
/// </summary>
public sealed record FirePoint
{
    /// <summary>Create a fire point, validating the source's tracking-device guard (device 0 → "no tracking devices").</summary>
    /// <param name="printTrackingDevice">Id of the physical photo-eye the print event is anchored to (must be &gt; 0).</param>
    /// <param name="printFirePoint">Raw encoder/device count where the label prints; usually static. A value &lt;= 0 means "neglect print" (apply-only).</param>
    /// <param name="applyTrackingDevice">Id of the physical photo-eye the apply event is anchored to (must be &gt; 0).</param>
    /// <param name="applyFirePoint">Where the applicator fires, in inch + edge notation.</param>
    /// <exception cref="ArgumentException">A tracking device id is not &gt; 0.</exception>
    public FirePoint(
        int printTrackingDevice,
        int printFirePoint,
        int applyTrackingDevice,
        ApplyPoint applyFirePoint)
    {
        ArgumentNullException.ThrowIfNull(applyFirePoint);

        if (printTrackingDevice <= 0)
        {
            throw new ArgumentException(
                $"PrintTrackingDevice must be > 0 (no print tracking device) but was {printTrackingDevice}.",
                nameof(printTrackingDevice));
        }

        if (applyTrackingDevice <= 0)
        {
            throw new ArgumentException(
                $"ApplyTrackingDevice must be > 0 (no apply tracking device) but was {applyTrackingDevice}.",
                nameof(applyTrackingDevice));
        }

        PrintTrackingDevice = printTrackingDevice;
        PrintFirePoint = printFirePoint;
        ApplyTrackingDevice = applyTrackingDevice;
        ApplyFirePoint = applyFirePoint;
    }

    /// <summary>Id of the physical photo-eye the print event is anchored to.</summary>
    public int PrintTrackingDevice { get; }

    /// <summary>Raw encoder/device count where the label prints; usually static.</summary>
    public int PrintFirePoint { get; }

    /// <summary>Id of the physical photo-eye the apply event is anchored to.</summary>
    public int ApplyTrackingDevice { get; }

    /// <summary>Where the applicator fires, in inch + edge notation.</summary>
    public ApplyPoint ApplyFirePoint { get; }

    /// <summary>
    /// True when the label should be applied but not printed (source guard
    /// <c>PrintFirePoint LIKE '0%'</c> → "neglect print").
    /// </summary>
    public bool NeglectPrint => PrintFirePoint <= 0;
}
