namespace PandA.Core.Domain;

public sealed record XRef(string Barcode, BarcodeType Type, int Priority = 1);
