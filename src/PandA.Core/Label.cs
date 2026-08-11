namespace PandA.Core;

/// <summary>
/// A single typed, printable label. In source these are the denormalized LabelData/LabelBarcode/LabelType
/// slots (1..6). Phase 1 is pass-through: the host supplies ready <see cref="Zpl"/>.
/// </summary>
/// <param name="LabelType">The label type (e.g. "Shipping", "Content"); matched against a printer's LabelMap.</param>
/// <param name="Lpn">The label's barcode / license-plate number (source LabelBarcode1..6).</param>
/// <param name="Zpl">Ready-to-print ZPL payload (source LabelData1..6).</param>
public sealed record Label(string LabelType, string Lpn, string Zpl);
