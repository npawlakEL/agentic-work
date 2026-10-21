using Microsoft.Extensions.Logging;
using PandA.Core;
using PandA.Core.Concurrency;
using PandA.Core.Induct;
using PandA.Core.Labels;
using PandA.Core.Plc;
using PandA.Core.Ports;
using PandA.Core.Services;
using PandA.Core.Settings;
using PandA.Core.Slots;
using PandA.Core.Verification;
using PandA.Sim;

namespace PandA.Tests;

/// <summary>
/// Composition helpers for single-host tests: builds Core services over the contract-faithful in-memory
/// adapters (lease lock, shared printer state, run ids, slot sequence), defaulting every multi-host dependency
/// to a fresh private instance so a test only names what it asserts on.
/// </summary>
internal static class TestServices
{
    /// <summary>A fresh in-memory lease lock provider (one simulated host over a private lease table).</summary>
    public static InMemoryLeaseLockProvider Locks() => new();

    /// <summary>A verify-fail threshold tracker over a fresh in-memory counter store.</summary>
    public static VerifyThresholdTracker Threshold(bool resetOnTrip = true) =>
        new(new InMemoryVerifyFailCounterStore(), resetOnTrip);

    public static InductService Induct(
        ITransportOrderStore store,
        InMemoryLineProvider lines,
        IPrinterGateway gateway,
        IClock clock,
        ISettingsProvider settings,
        IMinGapProvider? minGap = null,
        IXRefStore? xref = null,
        IExceptionLabelSource? exceptionLabels = null,
        ICartonRunRepository? runs = null,
        ISlotIndexProvider? slots = null,
        ILockProvider? locks = null,
        IPrinterSelectionService? selection = null,
        ILogger<InductService>? logger = null) =>
        new(
            store,
            lines,
            selection ?? new PrinterSelectionService(),
            gateway,
            clock,
            settings,
            lines.PrinterStates,
            locks ?? Locks(),
            slots ?? new InMemorySlotIndexProvider(),
            runs ?? new InMemoryCartonRunRepository(),
            minGap,
            xref,
            exceptionLabels,
            logger);

    public static VerifyStationService Verify(
        ITransportOrderStore store,
        IVerifyThresholdTracker? threshold,
        IClock clock,
        ILockProvider? locks = null,
        ILogger<VerifyStationService>? logger = null,
        IRejectHistoryRepository? rejects = null,
        IXRefStore? xref = null,
        ISettingsProvider? settings = null) =>
        new(store, new VerificationService(), threshold ?? Threshold(), clock, locks ?? Locks(), settings ?? new InMemorySettingsProvider(), logger, rejects, xref);

    public static CartonAdviceService Advice(
        ITransportOrderStore store,
        IClock clock,
        ISettingsProvider? settings = null,
        XRefService? xref = null,
        ILockProvider? locks = null) =>
        new(store, clock, settings ?? new InMemorySettingsProvider(), locks ?? Locks(), xref);

    public static LaneEvalService LaneEval(
        IPrinterStateStore printerStates,
        ISettingsProvider? settings = null,
        ILockProvider? locks = null) =>
        new(locks ?? Locks(), printerStates, settings);
}
