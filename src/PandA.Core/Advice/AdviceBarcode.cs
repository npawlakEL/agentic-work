using PandA.Core.Domain;

namespace PandA.Core.Advice;

/// <summary>
/// A secondary barcode carried on a host advice (oLPN, UPC, GTIN, item id, non-unique LPN…) to be
/// cross-referenced to the carton's blind label at advice time so a later non-BL induct scan can resolve
/// back to the transport order (F18).
/// </summary>
public sealed record AdviceBarcode(string Barcode, BarcodeType Type);
