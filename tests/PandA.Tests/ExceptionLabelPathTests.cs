using PandA.Core;
using PandA.Core.Induct;
using PandA.Core.Labels;
using PandA.Sim;

namespace PandA.Tests;

/// <summary>
/// F10 exception-label path coverage: unadvised-but-readable scans, gap faults, Top-printer fallback, and
/// the no-active-template outcome.
/// </summary>
public sealed class ExceptionLabelPathTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTransportOrderStore _store = new();
    private readonly InMemoryLineProvider _lines = new();
    private readonly CapturingPrinterGateway _gateway = new();
    private readonly TestClock _clock = new(T0);

    // The seeded "Not Received" template has no <LPN> slot; use one that does so LPN substitution is observable.
    private static readonly LabelTemplate NotReceivedWithLpn =
        new("Not Received", "^XA^FDNot Received^FS^FD<CartonID>^FS^FD<LPN>^FS^XZ", true);

    private static PrinterConfig Printer(string id, ApplyOrientation orientation, int order) =>
        new(id, "10.0.0." + (order + 1), 9100, ["Shipping"], orientation, order);

    private InductService Induct(
        IEnumerable<LabelTemplate>? templates = null,
        IMinGapProvider? minGap = null,
        ICartonRunRepository? runs = null) =>
        new(_store, _lines, new PrinterSelectionService(), _gateway, _clock,
            new InMemorySettingsProvider(seedKnownSettings: false),
            minGap: minGap,
            runs: runs,
            exceptionLabels: new LocalTemplateSource(new InMemoryLabelTemplateRepository(templates ?? [NotReceivedWithLpn])));

    [Fact]
    public async Task ReadableButUnadvisedBlindLabel_EmitsNotReceived_WithScannedLabelAsLpn()
    {
        _lines.Add(new LineConfig("L1", [Printer("Side1", ApplyOrientation.Side, 0)], printExceptionLabels: true));

        var result = await Induct().InductAsync(new InductScan("L1", "BL-UNADVISED-77", SeqNum: 12));

        Assert.Equal(InductStatus.ExceptionLabel, result.Status);
        Assert.StartsWith("NotReceived-000012-", result.ExceptionCartonId, StringComparison.Ordinal);
        Assert.Equal("Fail", result.Routing!.Value);
        var job = Assert.Single(_gateway.Jobs);
        Assert.Equal(ExceptionBuildResult.ExceptionLabelType, job.LabelType);
        Assert.Contains("BL-UNADVISED-77", job.Zpl, StringComparison.Ordinal);
        Assert.Contains(result.ExceptionCartonId!, job.Zpl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GapError_MapsToNotReceived_AndIsRecordedWithGapStatus()
    {
        var runs = new InMemoryCartonRunRepository();
        _lines.Add(new LineConfig("L1", [Printer("Side1", ApplyOrientation.Side, 0)], printExceptionLabels: true));

        var result = await Induct(minGap: new FixedMinGap(10), runs: runs)
            .InductAsync(new InductScan("L1", "BL-GAP-1", FrontGap: 2, SeqNum: 3));

        Assert.Equal(InductStatus.ExceptionLabel, result.Status);
        Assert.StartsWith("NotReceived-000003-", result.ExceptionCartonId, StringComparison.Ordinal);
        Assert.Contains("BL-GAP-1", Assert.Single(_gateway.Jobs).Zpl, StringComparison.Ordinal);
        Assert.Equal(CartonStatus.GapError, Assert.Single(runs.Records).StatusAtInduct);
    }

    [Fact]
    public async Task LineWithoutSidePrinter_FallsBackToTopPrinter()
    {
        _lines.Add(new LineConfig("L1",
            [Printer("Top1", ApplyOrientation.Top, 0), Printer("Top2", ApplyOrientation.Top, 1)],
            printExceptionLabels: true));

        var result = await Induct().InductAsync(new InductScan("L1", "BL-UNADVISED-1", SeqNum: 1));

        Assert.Equal(InductStatus.ExceptionLabel, result.Status);
        Assert.StartsWith("Top", Assert.Single(_gateway.Jobs).PrinterId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LineWithSideAndTopPrinters_PrefersSide()
    {
        _lines.Add(new LineConfig("L1",
            [Printer("Top1", ApplyOrientation.Top, 0), Printer("Side1", ApplyOrientation.Side, 1)],
            printExceptionLabels: true));

        await Induct().InductAsync(new InductScan("L1", "BL-UNADVISED-1", SeqNum: 1));

        Assert.Equal("Side1", Assert.Single(_gateway.Jobs).PrinterId);
    }

    [Fact]
    public async Task NoActiveTemplate_PrintsNothing_AndFallsBackToNoActiveOrder()
    {
        _lines.Add(new LineConfig("L1", [Printer("Side1", ApplyOrientation.Side, 0)], printExceptionLabels: true));
        var inactiveOnly = new[] { NotReceivedWithLpn with { Active = false } };

        var result = await Induct(inactiveOnly).InductAsync(new InductScan("L1", "BL-UNADVISED-9", SeqNum: 4));

        Assert.Equal(InductStatus.NoActiveOrder, result.Status);
        Assert.Null(result.ExceptionCartonId);
        Assert.Empty(_gateway.Jobs);
    }

    private sealed class FixedMinGap(int value) : IMinGapProvider
    {
        public int GetMinGap() => value;
    }
}
