namespace PandA.Core;

/// <summary>
/// Physical apply orientation of a printer / the orientation a carton's labels require.
/// Source PandA extracts an "Orientation" label per carton (defaults to Side); printers carry a
/// PrinterType attribute (TOP/SIDE). See architecture-log 005.
/// </summary>
public enum ApplyOrientation
{
    Side = 0,
    Top = 1,
}
