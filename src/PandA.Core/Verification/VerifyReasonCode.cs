namespace PandA.Core.Verification;

// Source: sdisp_CUSTOM_RejectHistory_Insert.sql lines 44-65 maps VerifyCode values to GUI-facing reason text.
public enum VerifyReasonCode
{
    /// <summary>Source code 0: PandA Logic Error.</summary>
    PandaLogicError = 0,

    /// <summary>Source code 1: Verification passed.</summary>
    VerificationPassed = 1,

    /// <summary>Source code 3: Pass through Carton.</summary>
    PassThroughCarton = 3,

    /// <summary>Source code 4: Gap Error.</summary>
    GapError = 4,

    /// <summary>Source code 5: No data from inbound scanner.</summary>
    NoDataFromInboundScanner = 5,

    /// <summary>Source code 6: Inbound Scanner Overflow.</summary>
    InboundScannerOverflow = 6,

    /// <summary>Source code 7: Inbound Scanner No Read.</summary>
    InboundScannerNoRead = 7,

    /// <summary>Source code 8: Inbound Scanner Label Conflict.</summary>
    InboundScannerLabelConflict = 8,

    /// <summary>Source code 11: No information for scanned carton.</summary>
    NoInformationForScannedCarton = 11,

    /// <summary>Source code 12: Verify scan error.</summary>
    VerifyScanError = 12,

    /// <summary>Source code 14: Blind Label not detected on verify scan.</summary>
    BlindLabelNotDetected = 14,

    /// <summary>Source code 15: Blind Label conflict at verify scan.</summary>
    BlindLabelConflict = 15,

    /// <summary>Source code 20: Shipping label no read.</summary>
    ShippingLabelNoRead = 20,

    /// <summary>Source code 21: Incorrect shipping label applied.</summary>
    IncorrectShippingLabelApplied = 21,

    /// <summary>Source code 22: Content Label no read.</summary>
    ContentLabelNoRead = 22,

    /// <summary>Source code 23: Incorrect content label applied.</summary>
    IncorrectContentLabelApplied = 23,

    /// <summary>Source code 27: Parcel Label no read.</summary>
    ParcelLabelNoRead = 27,

    /// <summary>Source code 28: Parcel Label conflict.</summary>
    ParcelLabelConflict = 28,

    /// <summary>Source code 29: Incorrect Parcel label applied.</summary>
    IncorrectParcelLabelApplied = 29,

    /// <summary>Source code 33: Shipping Label label conflict.</summary>
    ShippingLabelConflict = 33,

    /// <summary>Source code 34: Content Label Label conflict.</summary>
    ContentLabelConflict = 34,
}

public static class VerifyReasonCodeExtensions
{
    public static string ToDescription(this VerifyReasonCode code) => code switch
    {
        VerifyReasonCode.PandaLogicError => "PandA Logic Error",
        VerifyReasonCode.VerificationPassed => "Verification passed",
        VerifyReasonCode.PassThroughCarton => "Pass through Carton",
        VerifyReasonCode.GapError => "Gap Error",
        VerifyReasonCode.NoDataFromInboundScanner => "No data from inbound scanner",
        VerifyReasonCode.InboundScannerOverflow => "Inbound Scanner Overflow",
        VerifyReasonCode.InboundScannerNoRead => "Inbound Scanner No Read",
        VerifyReasonCode.InboundScannerLabelConflict => "Inbound Scanner Label Conflict",
        VerifyReasonCode.NoInformationForScannedCarton => "No information for scanned carton",
        VerifyReasonCode.VerifyScanError => "Verify scan error",
        VerifyReasonCode.BlindLabelNotDetected => "Blind Label not detected on verify scan",
        VerifyReasonCode.BlindLabelConflict => "Blind Label conflict at verify scan",
        VerifyReasonCode.ShippingLabelNoRead => "Shipping label no read",
        VerifyReasonCode.IncorrectShippingLabelApplied => "Incorrect shipping label applied",
        VerifyReasonCode.ContentLabelNoRead => "Content Label no read",
        VerifyReasonCode.IncorrectContentLabelApplied => "Incorrect content label applied",
        VerifyReasonCode.ParcelLabelNoRead => "Parcel Label no read",
        VerifyReasonCode.ParcelLabelConflict => "Parcel Label conflict",
        VerifyReasonCode.IncorrectParcelLabelApplied => "Incorrect Parcel label applied",
        VerifyReasonCode.ShippingLabelConflict => "Shipping Label label conflict",
        VerifyReasonCode.ContentLabelConflict => "Content Label Label conflict",
        _ => $"Verify Code {(int)code} is unknown",
    };
}


