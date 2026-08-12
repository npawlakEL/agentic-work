using PandA.UI.Contracts.Common;
using PandA.UI.Contracts.Reprint;

namespace PandA.UI.DemoHost.Sim;

/// <summary>Sim implementation of the audited reprint authorization command.</summary>
public sealed class DemoReprintAuthorization(DemoDataStore store) : IReprintAuthorizationCommand
{
    public Task<CommandResult> AuthorizeReprintAsync(string cartonId, string operatorName, CancellationToken ct = default)
    {
        if (!store.Settings.ReprintLabelsEnabled)
        {
            return Task.FromResult(CommandResult.Fail("Reprint Labels is disabled in Settings."));
        }

        if (!store.Cartons.TryGetValue(cartonId, out var carton))
        {
            return Task.FromResult(CommandResult.Fail($"Carton {cartonId} not found."));
        }

        carton.IsHeld = false;
        carton.CartonStatus = "Reprint authorized";
        carton.PrintedCount++;
        carton.RejectReason = "";
        carton.RejectedUtc = null;

        return Task.FromResult(CommandResult.Ok($"Reprint authorized for {cartonId} by {operatorName}."));
    }

    public Task<CommandResult> AuthorizeSlotReprintAsync(string cartonId, int slot, string operatorName, CancellationToken ct = default)
    {
        if (!store.Settings.ReprintLabelsEnabled)
        {
            return Task.FromResult(CommandResult.Fail("Reprint Labels is disabled in Settings."));
        }

        if (!store.Cartons.TryGetValue(cartonId, out var carton))
        {
            return Task.FromResult(CommandResult.Fail($"Carton {cartonId} not found."));
        }

        var index = carton.Slots.FindIndex(s => s.Slot == slot);
        if (index < 0)
        {
            return Task.FromResult(CommandResult.Fail($"Slot {slot} not found on {cartonId}."));
        }

        carton.Slots[index] = carton.Slots[index] with { Printed = true };
        carton.IsHeld = false;
        carton.PrintedCount++;

        return Task.FromResult(CommandResult.Ok($"Slot {slot} reprint authorized for {cartonId} by {operatorName}."));
    }
}
