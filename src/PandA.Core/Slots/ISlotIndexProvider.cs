namespace PandA.Core.Slots;

/// <summary>
/// Source of the PLC carton-assign slot — matches source proc <c>sdisp_TOOL_GetSlotNumber</c> behavior
/// (values 1..300, wrapping). Async so a multi-host deployment can back the port with a shared
/// (out-of-process) adapter.
/// </summary>
public interface ISlotIndexProvider
{
    ValueTask<int> NextSlotAsync(CancellationToken cancellationToken = default);
}
