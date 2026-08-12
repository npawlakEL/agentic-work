using System.Runtime.CompilerServices;
using PandA.UI.Contracts.Common;
using PandA.UI.Contracts.Manda;

namespace PandA.UI.DemoHost.Sim;

/// <summary>Sim implementation of the MandA station commands, streams, and queries.</summary>
public sealed class DemoMandaServices(DemoDataStore store)
    : IMandaStationQuery, IMandaScanCommand, IMandaPrintCommand, IMandaVerifyCommand, IMandaStationStream
{
    // Per-station in-progress carton + label state for the demo panel.
    private readonly Dictionary<string, MandaStationState> _state = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public Task<IReadOnlyList<MandaStation>> GetStationsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<MandaStation> stations = store.Stations.Values
            .Select(s => new MandaStation(s.StationId!, s.Name, s.Ip, s.Port))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(stations);
    }

    public Task<MandaScanResult> ScanAsync(string stationId, string scannedBarcode, CancellationToken ct = default)
    {
        var carton = FindCarton(scannedBarcode);
        if (carton is null)
        {
            return Task.FromResult(MandaScanResult.NotFound($"No carton matches '{scannedBarcode}'."));
        }

        var labels = carton.Slots
            .Select(s => new MandaLabel(s.Slot, s.LabelType, s.LabelBarcode, s.Zpl, Printed: false, Verified: false))
            .ToList();

        lock (_gate)
        {
            _state[stationId] = new MandaStationState(stationId, Online: true, carton.CartonId, labels);
        }

        return Task.FromResult(new MandaScanResult(true, carton.CartonId, carton.BlindLabel, labels));
    }

    public Task<CommandResult> PrintAsync(string stationId, string cartonId, IReadOnlyList<int> slots, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0)
        {
            return Task.FromResult(CommandResult.Fail("Select at least one label to print."));
        }

        lock (_gate)
        {
            if (!_state.TryGetValue(stationId, out var state) || !string.Equals(state.ActiveCartonId, cartonId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CommandResult.Fail("Scan a carton before printing."));
            }

            var updated = state.Labels
                .Select(l => slots.Contains(l.Slot) ? l with { Printed = true } : l)
                .ToList();
            _state[stationId] = state with { Labels = updated };
        }

        return Task.FromResult(CommandResult.Ok($"Printed {slots.Count} label(s) to station {stationId}."));
    }

    public Task<MandaVerifyResult> VerifyAsync(string stationId, string cartonId, string scannedBarcode, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_state.TryGetValue(stationId, out var state))
            {
                return Task.FromResult(new MandaVerifyResult(0, false, "No active carton at station."));
            }

            var index = state.Labels.ToList().FindIndex(l =>
                string.Equals(l.LabelBarcode, scannedBarcode, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                return Task.FromResult(new MandaVerifyResult(0, false, $"Scanned '{scannedBarcode}' does not match any printed label."));
            }

            var label = state.Labels[index];
            if (!label.Printed)
            {
                return Task.FromResult(new MandaVerifyResult(label.Slot, false, "Label was not printed at this station."));
            }

            var updated = state.Labels.ToList();
            updated[index] = label with { Verified = true };
            _state[stationId] = state with { Labels = updated };

            return Task.FromResult(new MandaVerifyResult(label.Slot, true, "Verified."));
        }
    }

    public Task<MandaStationState> GetStateAsync(string stationId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var state = _state.TryGetValue(stationId, out var s)
                ? s
                : new MandaStationState(stationId, Online: true, ActiveCartonId: "", []);
            return Task.FromResult(state);
        }
    }

    public async IAsyncEnumerable<MandaStationState> SubscribeAsync(string stationId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            yield return await GetStateAsync(stationId, ct).ConfigureAwait(false);
        }
    }

    private DemoCarton? FindCarton(string scannedBarcode) =>
        store.Cartons.Values.FirstOrDefault(c =>
            string.Equals(c.CartonId, scannedBarcode, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.BlindLabel, scannedBarcode, StringComparison.OrdinalIgnoreCase) ||
            c.Slots.Any(s => string.Equals(s.LabelBarcode, scannedBarcode, StringComparison.OrdinalIgnoreCase)));
}
