namespace PandA.Core.Slots;

/// <summary>
/// Source of the PLC carton-assign slot — matches source proc <c>sdisp_TOOL_GetSlotNumber</c> behavior
/// (one shared SQL SEQUENCE, values 1..<see cref="SlotIndex.SourceMaxSlots"/>, CYCLE).
/// <para>
/// Multi-host (decision-010): implementations are a <b>shared atomic counter</b> — every host draws from the
/// same sequence, so two concurrent inducts on any hosts never get the same value in one cycle. Scope: one
/// counter per PLC when the adapter knows a PLC identifier; otherwise a single global counter (the source's
/// behavior). A per-process counter is not a valid implementation.
/// </para>
/// </summary>
public interface ISlotIndexProvider
{
    ValueTask<int> NextSlotAsync(CancellationToken cancellationToken = default);
}

/// <summary>Slot-sequence constants.</summary>
public static class SlotIndex
{
    /// <summary>The source sequence wraps after this value (1..300 CYCLE).</summary>
    public const int SourceMaxSlots = 300;
}
