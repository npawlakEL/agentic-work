namespace PandA.Core.Slots;

/// <summary>
/// Source of the PLC carton-assign slot — matches source proc <c>sdisp_TOOL_GetSlotNumber</c> behavior
/// (values 1..<see cref="SlotIndex.SourceMaxSlots"/>, CYCLE).
/// <para>
/// Owner ruling 2026-10-21 (R2): the counter is scoped <b>per PLC/controller</b>, not global. Each
/// <c>controllerKey</c> (see <see cref="SlotControllerKey"/>) has its own independent 1..300 cycle; lines on the
/// same controller share one counter.
/// </para>
/// <para>
/// Multi-host (decision-010): implementations are a <b>shared atomic counter per key</b> — every host draws from
/// the same per-controller sequence, so two concurrent inducts for one controller on any hosts never get the same
/// value in one cycle. A per-process counter is not a valid implementation.
/// </para>
/// </summary>
public interface ISlotIndexProvider
{
    /// <summary>Draw the next slot (1..300 cycle) from the counter of <paramref name="controllerKey"/>.</summary>
    ValueTask<int> NextSlotAsync(string controllerKey, CancellationToken cancellationToken = default);
}

/// <summary>Slot-sequence constants.</summary>
public static class SlotIndex
{
    /// <summary>The source sequence wraps after this value (1..300 CYCLE).</summary>
    public const int SourceMaxSlots = 300;
}

/// <summary>
/// The controller identity a line's slot counter is keyed by (owner ruling 2026-10-21, R2). The configuration
/// model has no controller entity, so a line's controller is identified by its PLC number
/// (<see cref="LineConfig.PlcNumber"/>, the UI's <c>LineDto.PlcNumber</c>): lines configured with the same PLC
/// number share one counter. A line without a PLC number is its own controller (keyed by its line id).
/// </summary>
public static class SlotControllerKey
{
    /// <summary>The slot-counter key for <paramref name="config"/>.</summary>
    public static string For(LineConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.PlcNumber is { } plc ? ForPlc(plc) : ForLine(config.LineId);
    }

    /// <summary>Key for a controller identified by its PLC number.</summary>
    public static string ForPlc(int plcNumber) =>
        "PLC:" + plcNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Key for a line with no PLC number configured (and fallback when the line is unknown).</summary>
    public static string ForLine(string lineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        return "LINE:" + lineId.ToUpperInvariant();
    }
}
