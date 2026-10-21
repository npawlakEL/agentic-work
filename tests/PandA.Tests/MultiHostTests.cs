using System.Collections.Concurrent;
using PandA.Core;
using PandA.Core.Concurrency;
using PandA.Core.Induct;
using PandA.Core.Labels;
using PandA.Core.Settings;
using PandA.Core.Verification;
using PandA.Sim;
using PandA.Sim.Stores;

namespace PandA.Tests;

/// <summary>
/// Multi-host simulation (decision-010): two independent Core service graphs — "host A" and "host B" — composed
/// over ONE <see cref="SimSharedDatabase"/> (shared order table, lease table, run ids, slot sequence, printer
/// state, verify-fail counters), exactly as two PandA processes sharing the eController database. Any host may
/// process any line; correctness must come from the shared stores and leases, never from per-process state.
/// </summary>
public sealed class MultiHostTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static PrinterConfig Printer(string id, string[] map, int order) =>
        new(id, "10.0.0." + (order + 1), 9100, map, ApplyOrientation.Side, order);

    private static PandaLabelSet Labels(params (string Type, string Lpn)[] labels) =>
        new(labels.Select(l => new Label(l.Type, l.Lpn, $"^XA^FD{l.Lpn}^FS^XZ")));

    private static ScannedLabel[] Scan(string blind, params (string Type, string Value)[] labels) =>
        [new ScannedLabel("BlindLabel", blind), .. labels.Select(l => new ScannedLabel(l.Type, l.Value))];

    /// <summary>Two lanes, one printer each; both lines are configured on every host (any host, any line).</summary>
    private static readonly LineConfig[] TwoLanes =
    [
        new("L1", [Printer("Ship1", ["Shipping"], 0)], printExceptionLabels: true),
        new("L2", [Printer("Ship2", ["Shipping"], 1)], printExceptionLabels: true),
    ];

    /// <summary>One PandA process: its own service instances and line-config view, sharing only the database.</summary>
    private sealed class Host
    {
        public Host(string name, SimSharedDatabase db, IPrinterGateway gateway, ISettingsProvider settings, ILockProvider? locks = null)
        {
            Locks = locks ?? db.Locks.ForHost(name);
            Lines = new InMemoryLineProvider(db.PrinterStates);
            foreach (var line in TwoLanes)
            {
                Lines.AddCommissioned(line);
            }

            var clock = new TestClock(T0);
            Induct = TestServices.Induct(
                db.Orders, Lines, gateway, clock, settings,
                exceptionLabels: new LocalTemplateSource(new InMemoryLabelTemplateRepository()),
                runs: db.Runs, slots: db.Slots, locks: Locks);
            Verify = TestServices.Verify(db.Orders, db.CreateThresholdTracker(), clock, Locks);
            Advice = TestServices.Advice(db.Orders, clock, settings, locks: Locks);
            LaneEval = TestServices.LaneEval(db.PrinterStates, settings, Locks);
        }

        public ILockProvider Locks { get; }

        public InMemoryLineProvider Lines { get; }

        public InductService Induct { get; }

        public VerifyStationService Verify { get; }

        public CartonAdviceService Advice { get; }

        public LaneEvalService LaneEval { get; }
    }

    private static (Host A, Host B) Hosts(SimSharedDatabase db, IPrinterGateway gateway, ISettingsProvider settings) =>
        (new Host("A", db, gateway, settings), new Host("B", db, gateway, settings));

    /// <summary>A gateway whose dispatches block until <see cref="Release"/> — holds a print "in flight".</summary>
    private sealed class GatedGateway(bool released = false) : IPrinterGateway
    {
        private readonly TaskCompletionSource _release = NewSignal(released);
        private readonly TaskCompletionSource _entered = NewSignal(false);
        private readonly ConcurrentQueue<PrintJob> _jobs = new();

        public IReadOnlyCollection<PrintJob> Jobs => _jobs;

        public Task Entered => _entered.Task;

        public void Release() => _release.TrySetResult();

        public async ValueTask SendAsync(PrintJob job, CancellationToken cancellationToken = default)
        {
            _jobs.Enqueue(job);
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private static TaskCompletionSource NewSignal(bool set)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (set)
            {
                tcs.SetResult();
            }

            return tcs;
        }
    }

    /// <summary>Forces a short acquire timeout so a contended lease surfaces as a timeout quickly.</summary>
    private sealed class ShortTimeoutLocks(ILockProvider inner, TimeSpan timeout) : ILockProvider
    {
        public ValueTask<ILease> AcquireAsync(string resourceName, TimeSpan? timeout1 = null, CancellationToken cancellationToken = default) =>
            inner.AcquireAsync(resourceName, timeout, cancellationToken);
    }

    /// <summary>Records the peak number of lane evaluations reading printer state at the same time.</summary>
    private sealed class ConcurrencyProbePrinterStates(IPrinterStateStore inner) : IPrinterStateStore
    {
        private int _inFlight;
        private int _peak;

        public int Peak => Volatile.Read(ref _peak);

        public async ValueTask<IReadOnlyDictionary<string, PrinterState>> GetSnapshotAsync(
            IEnumerable<string> printerIds,
            CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _inFlight);
            InterlockedMax(ref _peak, now);
            try
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                return await inner.GetSnapshotAsync(printerIds, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public ValueTask SetStatusAsync(string printerId, bool plcOnline, bool engineOnline, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            inner.SetStatusAsync(printerId, plcOnline, engineOnline, at, cancellationToken);

        public ValueTask SetSpareAsync(string printerId, bool isSpare, DateTimeOffset? statusUpdatedAt, CancellationToken cancellationToken = default) =>
            inner.SetSpareAsync(printerId, isSpare, statusUpdatedAt, cancellationToken);

        public ValueTask StampLastPrintedAsync(string printerId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            inner.StampLastPrintedAsync(printerId, at, cancellationToken);

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while (value > (current = Volatile.Read(ref target))
                && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }

    /// <summary>
    /// Simulates a writer that bypasses the carton lease: just before the next conditional update it commits a
    /// competing write to the same row, so the caller's expected version is stale.
    /// </summary>
    private sealed class RogueWriterStore(InMemoryTransportOrderStore inner) : ITransportOrderStore
    {
        private int _armed;

        public Action<TransportOrder>? RogueMutation { get; set; }

        public int Conflicts { get; private set; }

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public ValueTask<TransportOrder?> FindActiveByTuIdAsync(string tuId, CancellationToken cancellationToken = default) =>
            inner.FindActiveByTuIdAsync(tuId, cancellationToken);

        public ValueTask InsertAsync(TransportOrder transportOrder, CancellationToken cancellationToken = default) =>
            inner.InsertAsync(transportOrder, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tuId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(tuId, cancellationToken);

        public ValueTask<IReadOnlyList<string>> FindTuIdsByWaveAsync(string waveId, CancellationToken cancellationToken = default) =>
            inner.FindTuIdsByWaveAsync(waveId, cancellationToken);

        public async ValueTask UpdateAsync(TransportOrder transportOrder, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1 && RogueMutation is { } rogue)
            {
                var other = (await inner.FindActiveByTuIdAsync(transportOrder.TuId, cancellationToken).ConfigureAwait(false))!;
                rogue(other);
                await inner.UpdateAsync(other, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await inner.UpdateAsync(transportOrder, cancellationToken).ConfigureAwait(false);
            }
            catch (ConcurrencyConflictException)
            {
                Conflicts++;
                throw;
            }
        }
    }

    // ---- (a) same carton inducted concurrently on two lanes prints exactly once -------------------------

    [Fact]
    public async Task A_SameCarton_InductedOnTwoLanesByTwoHosts_WhileFirstIsPrinting_PrintsExactlyOnce()
    {
        var db = new SimSharedDatabase();
        var gateway = new GatedGateway();
        var (a, b) = Hosts(db, gateway, TestSettings.ReprintOff());
        await a.Advice.AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));

        var first = a.Induct.InductAsync("L1", "BL1").AsTask();
        await gateway.Entered.WaitAsync(Wait);

        // Host A is mid-dispatch holding the carton lease; host B's induct on another lane must wait for it.
        var second = b.Induct.InductAsync("L2", "BL1").AsTask();
        await Task.Delay(150);
        Assert.False(second.IsCompleted);
        Assert.StartsWith("A:", db.Locks.CurrentOwner(LockNames.Carton("BL1")), StringComparison.Ordinal);

        gateway.Release();
        var results = await Task.WhenAll(first, second).WaitAsync(Wait);

        Assert.Equal(InductStatus.Printed, results[0].Status);
        Assert.Equal(InductStatus.NoReprint, results[1].Status);
        Assert.Single(gateway.Jobs);
        var stored = await db.Orders.FindActiveByTuIdAsync("BL1");
        Assert.Equal(1, stored!.PrintCount);
    }

    [Fact]
    public async Task A_ManyCartons_InductedSimultaneouslyOnBothHosts_EachPrintsExactlyOnce()
    {
        var db = new SimSharedDatabase();
        var gateway = new GatedGateway(released: true);
        var (a, b) = Hosts(db, gateway, TestSettings.ReprintOff());
        var cartons = Enumerable.Range(1, 25).Select(i => $"BL{i:00}").ToArray();
        foreach (var tu in cartons)
        {
            await a.Advice.AdviseAsync("L1", tu, Labels(("Shipping", "S-" + tu)));
        }

        var inducts = cartons.SelectMany(tu => new[]
        {
            Task.Run(() => a.Induct.InductAsync("L1", tu).AsTask()),
            Task.Run(() => b.Induct.InductAsync("L2", tu).AsTask()),
        });
        var results = await Task.WhenAll(inducts).WaitAsync(Wait);

        Assert.Equal(cartons.Length, results.Count(r => r.Status == InductStatus.Printed));
        Assert.Equal(cartons.Length, results.Count(r => r.Status == InductStatus.NoReprint));
        Assert.Equal(cartons.Length, gateway.Jobs.Count);
        foreach (var tu in cartons)
        {
            Assert.Equal(1, (await db.Orders.FindActiveByTuIdAsync(tu))!.PrintCount);
        }
    }

    // ---- (b) verify racing induct on one carton: no lost update ----------------------------------------

    [Fact]
    public async Task B_VerifyOnHostB_RacingInductOnHostA_SerializesAndLosesNoUpdate()
    {
        var db = new SimSharedDatabase();
        var gateway = new GatedGateway();
        var (a, b) = Hosts(db, gateway, TestSettings.ReprintOff());
        await a.Advice.AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));

        var induct = a.Induct.InductAsync("L1", "BL1").AsTask();
        await gateway.Entered.WaitAsync(Wait);
        var verify = b.Verify.VerifyAsync("BL1", Scan("BL1", ("Shipping", "S1")), new VerifyOptions(), failThreshold: 3).AsTask();
        await Task.Delay(150);
        Assert.False(verify.IsCompleted); // verify waits on the carton lease held by host A

        gateway.Release();
        await Task.WhenAll(induct, verify).WaitAsync(Wait);

        Assert.Equal(InductStatus.Printed, (await induct).Status);
        Assert.Equal(VerifyStationStatus.Verified, (await verify).Status);
        var stored = (await db.Orders.FindActiveByTuIdAsync("BL1"))!;
        Assert.Equal(TransportOrderStatus.Verified, stored.Status); // verify's write kept…
        Assert.Equal(1, stored.PrintCount);                          // …on top of induct's print, not over it
        Assert.True(stored.HasPrinted);
    }

    // ---- (c) re-advice during print does not reset the print count -------------------------------------

    [Fact]
    public async Task C_ReAdviceOnHostB_DuringPrintOnHostA_KeepsPrintCount_AndAppliesNewLabelData()
    {
        var db = new SimSharedDatabase();
        var gateway = new GatedGateway();
        var (a, b) = Hosts(db, gateway, TestSettings.ReprintOff());
        await a.Advice.AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));

        var induct = a.Induct.InductAsync("L1", "BL1").AsTask();
        await gateway.Entered.WaitAsync(Wait);
        var readvice = b.Advice.AdviseAsync("L1", "BL1", Labels(("Shipping", "S2"))).AsTask();
        await Task.Delay(150);
        Assert.False(readvice.IsCompleted);

        gateway.Release();
        await Task.WhenAll(induct, readvice).WaitAsync(Wait);

        var stored = (await db.Orders.FindActiveByTuIdAsync("BL1"))!;
        Assert.Equal(1, stored.PrintCount);
        Assert.True(stored.HasPrinted);
        Assert.Equal("S2", Assert.Single(stored.Labels.Labels).Lpn);

        // 'Reprint Labels' off: the re-advised, already-printed carton stays locked against a silent reprint.
        Assert.Equal(InductStatus.NoReprint, (await b.Induct.InductAsync("L2", "BL1")).Status);
        Assert.Single(gateway.Jobs);
    }

    // ---- (d) two lane evaluations on the same line serialize across hosts ------------------------------

    [Fact]
    public async Task D_LaneEvals_OnSameLine_FromTwoHosts_NeverOverlap()
    {
        var db = new SimSharedDatabase();
        _ = Hosts(db, new GatedGateway(released: true), new InMemorySettingsProvider()); // seeds printer rows
        var probe = new ConcurrencyProbePrinterStates(db.PrinterStates);
        var evalA = TestServices.LaneEval(probe, locks: db.Locks.ForHost("A"));
        var evalB = TestServices.LaneEval(probe, locks: db.Locks.ForHost("B"));

        var evals = Enumerable.Range(0, 6).SelectMany(_ => new[]
        {
            Task.Run(() => evalA.EvaluateAsync(TwoLanes[0], new ZoneState(true), T0).AsTask()),
            Task.Run(() => evalB.EvaluateAsync(TwoLanes[0], new ZoneState(true), T0).AsTask()),
        });
        var results = await Task.WhenAll(evals).WaitAsync(Wait);

        Assert.All(results, r => Assert.False(r.Skipped));
        Assert.Equal(1, probe.Peak);
    }

    [Fact]
    public async Task D_LaneEval_WhenOtherHostHoldsLineLease_IsSkipped_AndWritesNothing_ThenProceedsAfterRelease()
    {
        var db = new SimSharedDatabase();
        _ = Hosts(db, new GatedGateway(released: true), new InMemorySettingsProvider());
        var line = TwoLanes[0];
        var evalB = TestServices.LaneEval(
            db.PrinterStates,
            locks: new ShortTimeoutLocks(db.Locks.ForHost("B"), TimeSpan.FromMilliseconds(50)));

        var heldByA = await db.Locks.ForHost("A").AcquireAsync(LockNames.PrinterState(line.LineId));
        var skipped = await evalB.ApplyPrinterStatusAsync(line, "Ship1", plcOnline: false, engineOnline: false, new ZoneState(true), T0);

        Assert.True(skipped.Skipped);
        Assert.True(db.PrinterStates.Get("Ship1")!.PlcOnline); // the status write is part of the skipped step

        await heldByA.DisposeAsync();
        var applied = await evalB.ApplyPrinterStatusAsync(line, "Ship1", plcOnline: false, engineOnline: false, new ZoneState(true), T0);
        Assert.False(applied.Skipped);
        Assert.False(db.PrinterStates.Get("Ship1")!.PlcOnline);
    }

    // ---- (e) run ids and exception carton ids unique across hosts --------------------------------------

    [Fact]
    public async Task E_ExceptionCartonIds_AndRunIds_AreUniqueAcrossHosts()
    {
        var db = new SimSharedDatabase();
        var gateway = new GatedGateway(released: true);
        var (a, b) = Hosts(db, gateway, new InMemorySettingsProvider(seedKnownSettings: false)); // exceptions on

        var inducts = Enumerable.Range(1, 20).SelectMany(i => new[]
        {
            Task.Run(() => a.Induct.InductAsync(new InductScan("L1", "???", SeqNum: i)).AsTask()),
            Task.Run(() => b.Induct.InductAsync(new InductScan("L2", "???", SeqNum: i)).AsTask()),
        });
        var results = await Task.WhenAll(inducts).WaitAsync(Wait);

        var exceptionIds = results.Select(r => r.ExceptionCartonId).ToArray();
        Assert.All(exceptionIds, id => Assert.False(string.IsNullOrEmpty(id)));
        Assert.Equal(exceptionIds.Length, exceptionIds.Distinct(StringComparer.Ordinal).Count());
        foreach (var id in exceptionIds)
        {
            var synthetic = await db.Orders.FindActiveByTuIdAsync(id!);
            Assert.NotNull(synthetic);
            Assert.True(synthetic.IsExceptionCarton);
        }

        var runIds = db.Runs.Records.Select(r => r.RunId).ToArray();
        Assert.Equal(runIds.Length, runIds.Distinct().Count());
        Assert.True(runIds.Length >= exceptionIds.Length);
    }

    [Fact]
    public async Task E_RunIds_ReservedConcurrentlyByManyHosts_AreUnique()
    {
        var db = new SimSharedDatabase();
        var reservations = Enumerable.Range(0, 500)
            .Select(_ => Task.Run(() => db.Runs.ReserveRunIdAsync().AsTask()));

        var ids = await Task.WhenAll(reservations);

        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(id > 0));
    }

    // ---- (f) slot counter shared across hosts and cycles 1..300 ----------------------------------------

    [Fact]
    public async Task F_SlotSequence_IsSharedAcrossHosts_AndContinuesAcrossThem()
    {
        var db = new SimSharedDatabase();
        var (a, b) = Hosts(db, new GatedGateway(released: true), TestSettings.ReprintOff());
        var slots = new List<int?>();
        for (var i = 1; i <= 6; i++)
        {
            var host = i % 2 == 1 ? a : b;
            // Same line (= same controller) on both hosts: per-controller counters (R2) are still host-shared.
            const string lane = "L1";
            await host.Advice.AdviseAsync(lane, $"BL{i}", Labels(("Shipping", $"S{i}")));
            slots.Add((await host.Induct.InductAsync(lane, $"BL{i}")).Slot);
        }

        Assert.Equal(new int?[] { 1, 2, 3, 4, 5, 6 }, slots);
    }

    [Fact]
    public async Task F_SlotSequence_DrawnConcurrently_CoversOneTo300ExactlyOncePerCycle_ThenWraps()
    {
        var db = new SimSharedDatabase();
        var draws = Enumerable.Range(0, 600).Select(_ => Task.Run(() => db.Slots.NextSlotAsync("PLC:1").AsTask()));

        var values = await Task.WhenAll(draws);

        Assert.All(values, v => Assert.InRange(v, 1, 300));
        Assert.All(values.GroupBy(v => v), g => Assert.Equal(2, g.Count()));
        Assert.Equal(1, await db.Slots.NextSlotAsync("PLC:1"));
    }

    // ---- (g) lease expiry lets a second host proceed after the holder crashes --------------------------

    [Fact]
    public async Task G_CrashedHolder_LeaseExpires_AndHostBPrints_StaleHolderIsFenced()
    {
        var time = new ManualTimeProvider(T0);
        var db = new SimSharedDatabase(time, leaseDuration: TimeSpan.FromSeconds(30));
        var gateway = new GatedGateway(released: true);
        var (_, b) = Hosts(db, gateway, TestSettings.ReprintOff());
        await b.Advice.AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));

        // Host A takes the carton lease and "crashes" — never releases.
        var crashed = await db.Locks.ForHost("A").AcquireAsync(LockNames.Carton("BL1"));

        var induct = b.Induct.InductAsync("L2", "BL1").AsTask();
        await Task.Delay(150);
        Assert.False(induct.IsCompleted);
        Assert.Empty(gateway.Jobs);

        time.Advance(TimeSpan.FromSeconds(31));
        var result = await induct.WaitAsync(Wait);

        Assert.Equal(InductStatus.Printed, result.Status);
        Assert.Single(gateway.Jobs);
        Assert.False(await crashed.RenewAsync());       // the revived holder learns it lost the lease…
        await crashed.DisposeAsync();                    // …and its release cannot free another owner's lease
        Assert.Null(db.Locks.CurrentOwner(LockNames.Carton("BL1")));
    }

    [Fact]
    public async Task G_InductOnHostB_TimesOutWhileLiveHolderKeepsLease_FailsSafeWithoutPrinting()
    {
        var db = new SimSharedDatabase();
        var gateway = new GatedGateway(released: true);
        var a = new Host("A", db, gateway, TestSettings.ReprintOff());
        var b = new Host("B", db, gateway, TestSettings.ReprintOff(),
            new ShortTimeoutLocks(db.Locks.ForHost("B"), TimeSpan.FromMilliseconds(50)));
        await a.Advice.AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));

        await using var held = await db.Locks.ForHost("A").AcquireAsync(LockNames.Carton("BL1"));
        var result = await b.Induct.InductAsync("L2", "BL1");

        Assert.Equal(InductStatus.LockTimeout, result.Status);
        Assert.NotNull(result.Slot); // the PLC still gets its slot assignment
        Assert.Empty(gateway.Jobs);
        Assert.Equal(0, (await db.Orders.FindActiveByTuIdAsync("BL1"))!.PrintCount);
    }

    // ---- (h) version conflicts surface and are retried / handled ---------------------------------------

    [Fact]
    public async Task H_StaleUpdate_ThrowsConcurrencyConflict_AndSaveWithRetryReappliesOnLatestRow()
    {
        var db = new SimSharedDatabase();
        var (a, _) = Hosts(db, new GatedGateway(released: true), TestSettings.ReprintOff());
        await a.Advice.AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));

        var copyA = (await db.Orders.FindActiveByTuIdAsync("BL1"))!;
        var copyB = (await db.Orders.FindActiveByTuIdAsync("BL1"))!;
        Assert.NotSame(copyA, copyB);

        copyA.SetAdviceMetadata(waveId: "W-A");
        await db.Orders.UpdateAsync(copyA);
        Assert.Equal(2, copyA.Version);

        copyB.MarkVerifyFailed(T0);
        var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => db.Orders.UpdateAsync(copyB).AsTask());
        Assert.Equal(1, conflict.ExpectedVersion);
        Assert.Equal(2, conflict.ActualVersion);

        var saved = await db.Orders.SaveWithRetryAsync(copyB, o => o.MarkVerifyFailed(T0));
        var stored = (await db.Orders.FindActiveByTuIdAsync("BL1"))!;
        Assert.Equal(3, stored.Version);
        Assert.Equal("W-A", stored.WaveId);                                 // host A's write survived
        Assert.Equal(TransportOrderStatus.HeldForIntervention, stored.Status); // host B's intent re-applied
        Assert.Equal(3, saved!.Version);
    }

    [Fact]
    public async Task H_DuplicateInsert_OfActiveTuId_IsRejected()
    {
        var db = new SimSharedDatabase();
        await db.Orders.InsertAsync(new TransportOrder("BL1", "L1", Labels(("Shipping", "S1")), T0));

        await Assert.ThrowsAsync<DuplicateTransportOrderException>(
            () => db.Orders.InsertAsync(new TransportOrder("BL1", "L2", Labels(("Shipping", "S2")), T0)).AsTask());
    }

    [Fact]
    public async Task H_InductPersistConflict_FromLeaseBypassingWriter_IsReplayedOnLatestRow_PrintsOnce()
    {
        var db = new SimSharedDatabase();
        var rogue = new RogueWriterStore(db.Orders) { RogueMutation = o => o.SetAdviceMetadata(waveId: "ROGUE") };
        var gateway = new GatedGateway(released: true);
        var lines = new InMemoryLineProvider(db.PrinterStates).AddCommissioned(TwoLanes[0]);
        var induct = TestServices.Induct(rogue, lines, gateway, new TestClock(T0), TestSettings.ReprintOff(),
            runs: db.Runs, slots: db.Slots, locks: db.Locks.ForHost("A"));
        await TestServices.Advice(db.Orders, new TestClock(T0), locks: db.Locks.ForHost("A"))
            .AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));

        rogue.Arm();
        var result = await induct.InductAsync("L1", "BL1");

        Assert.Equal(InductStatus.Printed, result.Status);
        Assert.Equal(1, rogue.Conflicts);
        Assert.Single(gateway.Jobs); // the conflict never causes a second dispatch
        var stored = (await db.Orders.FindActiveByTuIdAsync("BL1"))!;
        Assert.Equal(1, stored.PrintCount);
        Assert.True(stored.HasPrinted);
        Assert.Equal("ROGUE", stored.WaveId); // the competing write is not lost either
    }
}
