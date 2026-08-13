namespace PandA.Core.Slots;

// Source: sdisp_TOOL_GetSlotNumber.sql creates dbo.CartonAssignSeq with MINVALUE 1 MAXVALUE 300 CYCLE.
public sealed class SlotIndexService
{
    private readonly ISlotIndexProvider _provider;

    public SlotIndexService(ISlotIndexProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public int NextSlot() => _provider.NextSlot();
}


