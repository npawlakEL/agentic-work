namespace PandA.Core.Slots;

// Matches source proc sdisp_TOOL_GetSlotNumber behavior: slot numbers 1..300, wrapping — one counter per
// controller (owner ruling 2026-10-21, R2).
public sealed class SlotIndexService
{
    private readonly ISlotIndexProvider _provider;

    public SlotIndexService(ISlotIndexProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public ValueTask<int> NextSlotAsync(string controllerKey, CancellationToken cancellationToken = default) =>
        _provider.NextSlotAsync(controllerKey, cancellationToken);
}
