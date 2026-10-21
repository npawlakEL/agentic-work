using PandA.Core;
using PandA.Sim;

namespace PandA.Tests;

/// <summary>
/// Line fixtures. Since owner ruling 2026-10-21 (R9) a line/carton with no fire-point profile prints nothing,
/// so tests that exercise printing register their lines "commissioned": with a generated active profile that
/// has a usable fire point for every printer × mapped label type.
/// </summary>
internal static class TestLines
{
    public const string CommissionedProfileName = "TEST-COMMISSIONED";

    /// <summary>The fire point every generated slot gets.</summary>
    public static FirePoint DefaultFirePoint { get; } = new(1, 100, 2, ApplyPoint.Parse("1L"));

    /// <summary>
    /// Register <paramref name="config"/> with a generated active profile when it has none (a config that
    /// already carries an active profile is registered as-is).
    /// </summary>
    public static InMemoryLineProvider AddCommissioned(
        this InMemoryLineProvider lines,
        LineConfig config,
        IEnumerable<PrinterState>? states = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Add(Commissioned(config), states);
    }

    /// <summary>A copy of <paramref name="config"/> with a generated active profile (if it has none).</summary>
    public static LineConfig Commissioned(LineConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.ActiveProfile is not null)
        {
            return config;
        }

        var profile = new FirePointProfile(
            CommissionedProfileName,
            config.Printers
                .SelectMany(p => p.LabelMap
                    .Append("Exception")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(type => ((p.PrinterId, type), DefaultFirePoint))));

        return new LineConfig(
            config.LineId,
            config.Printers,
            config.LoadBalance,
            config.PlaceId,
            config.BufferOrder,
            profile,
            config.PrinterPolicies,
            config.Lanes,
            config.PrinterStatusSuffix,
            config.PlcZone,
            config.SorterPlcRecId,
            config.PlcDbName,
            config.EncoderResolution,
            config.DynamicPrintPoint,
            config.ProfileRegistry,
            config.DefaultProfile,
            config.FilterLabels,
            config.PrintExceptionLabels,
            config.TwoPrinterRule,
            config.PlcNumber);
    }
}
