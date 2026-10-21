namespace PandA.Core.Slots;

// Matches source proc sdisp_TOOL_GetSlotNumber behavior: slot numbers 1..300, wrapping.
public sealed class SlotIndexService
{
    private readonly ISlotIndexProvider _provider;

    public SlotIndexService(ISlotIndexProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public ValueTask<int> NextSlotAsync(CancellationToken cancellationToken = default) =>
        _provider.NextSlotAsync(cancellationToken);
}
