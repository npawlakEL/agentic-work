namespace PandA.Core.Settings;

public static class KnownSettings
{
    public static readonly SettingsDescriptor<bool> ReprintLabels =
        new("Reprint Labels", false, "Allow labels to be reprinted globally.");

    public static readonly SettingsDescriptor<bool> OverwriteLabelData =
        new("OverwriteLabelData", false, "Overwrite existing host label advice.");

    public static readonly SettingsDescriptor<bool> PrintExceptionLabels =
        new("PrintExceptionLabels", false, "Print local exception labels.");

    public static readonly SettingsDescriptor<bool> DcmsExceptions =
        new("DCMSExceptions", false, "Request exception labels from DCMS.");

    public static readonly SettingsDescriptor<int> MinGap =
        new("MinGap", 20, "Minimum allowed carton gap.");

    public static readonly SettingsDescriptor<bool> ForcedReplen =
        new("ForcedReplen", false, "Bypass normal lookup logic.");

    public static readonly SettingsDescriptor<int> HeightCheckLabelField =
        new("HeightCheckLabelField", 2, "Label slot carrying height measurement.");

    public static readonly SettingsDescriptor<bool> HeightCheckEnabled =
        new("HeightCheckEnabled", true, "Height sensor enabled.");

    public static readonly SettingsDescriptor<bool> LoadBalanceEnabled =
        new("LoadBalanceEnabled", false, "Enable printer load balancing.");

    public static readonly SettingsDescriptor<bool> TwoPrinterRule =
        new("2 Printer Rule", false, "Enable two-printer spare rule.");

    public static readonly SettingsDescriptor<bool> FilterLabels =
        new("FilterLabels", true, "Strip disallowed ZPL configuration commands.");

    public static readonly SettingsDescriptor<bool> PrinterStatusSuffix =
        new("PrinterStatusSuffix", false, "Append Zebra ~HS status suffix.");

    public static readonly SettingsDescriptor<int> PurgeSettingExceptionData =
        new("PurgeSetting_ExceptionData", 7, "Days to keep exception PandaData.");

    public static readonly SettingsDescriptor<int> PurgeSettingInactiveData =
        new("PurgeSetting_InactiveData", 21, "Days to keep inactive data.");

    public static readonly SettingsDescriptor<int> PurgeSettingUnUsedData =
        new("PurgeSetting_UnUsedData", 14, "Days to keep unused data.");

    public static readonly SettingsDescriptor<int> PurgeSettingUsedData =
        new("PurgeSetting_UsedData", 7, "Days to keep used wave data.");

    public static readonly SettingsDescriptor<bool> DynamicPrintPoint =
        new("DynamicPrintPoint", true, "Enable dynamic apply-point calculation.");

    public static readonly SettingsDescriptor<int> DefaultHeight =
        new("DefaultHeight", 10, "Default carton height when no sensor is present.");

    public static readonly SettingsDescriptor<int> DefaultDimension =
        new("DefaultDimension", 20, "Default box dimension.");

    public static readonly SettingsDescriptor<decimal> EncoderResolution =
        new("EncoderResolution", 0.2m, "Encoder resolution in inches per step pulse.");

    public static IReadOnlyList<ISettingsDescriptor> All { get; } =
    [
        ReprintLabels,
        OverwriteLabelData,
        PrintExceptionLabels,
        DcmsExceptions,
        MinGap,
        ForcedReplen,
        HeightCheckLabelField,
        HeightCheckEnabled,
        LoadBalanceEnabled,
        TwoPrinterRule,
        FilterLabels,
        PrinterStatusSuffix,
        PurgeSettingExceptionData,
        PurgeSettingInactiveData,
        PurgeSettingUnUsedData,
        PurgeSettingUsedData,
        DynamicPrintPoint,
        DefaultHeight,
        DefaultDimension,
        EncoderResolution,
    ];
}
