using System.Runtime.CompilerServices;
using PandA.UI.Contracts.Status;

namespace PandA.UI.DemoHost.Sim;

/// <summary>Sim implementation of the dashboard status feeds; pushes light periodic mutations.</summary>
public sealed class DemoStatusStreams(DemoDataStore store) : ILineStatusStream, IPrinterStatusStream
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    Task<IReadOnlyList<LineStatus>> ILineStatusStream.GetSnapshotAsync(CancellationToken ct) =>
        Task.FromResult(SnapshotLines());

    async IAsyncEnumerable<LineStatus> ILineStatusStream.SubscribeAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(Interval, ct).ConfigureAwait(false);
            foreach (var line in SnapshotLines())
            {
                yield return line;
            }
        }
    }

    Task<IReadOnlyList<PrinterStatus>> IPrinterStatusStream.GetSnapshotAsync(CancellationToken ct) =>
        Task.FromResult(SnapshotPrinters());

    async IAsyncEnumerable<PrinterStatus> IPrinterStatusStream.SubscribeAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(Interval, ct).ConfigureAwait(false);

            foreach (var printer in SnapshotPrinters())
            {
                yield return printer;
            }
        }
    }

    private IReadOnlyList<LineStatus> SnapshotLines()
    {
        var result = new List<LineStatus>();
        foreach (var line in store.Lines.Values)
        {
            var printers = store.Printers.Values.Where(p => string.Equals(p.LineId, line.LineId, StringComparison.OrdinalIgnoreCase)).ToList();
            var online = printers.Count(p => store.PrinterRuntime.TryGetValue(p.PrinterId!, out var rt) && rt.Online);
            var mapName = line.ActiveMapId is not null && store.Maps.TryGetValue(line.ActiveMapId, out var map) ? map.Name : "(none)";

            var (state, reason) = online == 0
                ? (LineRunState.Stopped, "No printers online")
                : online < printers.Count
                    ? (LineRunState.Slow, "Running degraded")
                    : (LineRunState.Running, "");

            result.Add(new LineStatus(line.LineId!, line.Name, state, reason, mapName, printers.Count, online));
        }

        return result;
    }

    private IReadOnlyList<PrinterStatus> SnapshotPrinters()
    {
        var result = new List<PrinterStatus>();
        foreach (var printer in store.Printers.Values)
        {
            var rt = store.PrinterRuntime.TryGetValue(printer.PrinterId!, out var r) ? r : new DemoPrinterRuntime();
            var labelTypes = store.Lines.TryGetValue(printer.LineId, out var line)
                ? LineSimulationFactory.LabelTypesForPrinter(store, line, printer.PrinterId!)
                : [];
            result.Add(new PrinterStatus(
                printer.PrinterId!,
                printer.Name,
                printer.LineId,
                rt.Online,
                rt.IsSpare,
                store.Orientations.TryGetValue(printer.OrientationId, out var o) ? o.Name : printer.OrientationId,
                rt.VerifyFailCount,
                store.Settings.VerifyFailThreshold,
                labelTypes,
                rt.LastPrintedUtc));
        }

        return result;
    }
}
