using PandA.Core;
using PandA.Sim;
using Xunit;

namespace PandA.Tests;

/// <summary>
/// Driver tests for the reprint lifecycle (A5 → B2/B4 of architecture-log 008): <see cref="TransportOrder"/>
/// state → <c>CanPrint</c>, the <see cref="InductService"/> reprint gate, full-vs-partial run counting, and
/// persistence behaviour (upsert counts) verified with <see cref="CountingTransportOrderStore"/>.
/// </summary>
public sealed class ReprintLifecycleMatrixTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static PandaLabelSet Labels(params (string Type, string Lpn)[] labels) =>
        new(labels.Select(l => new Label(l.Type, l.Lpn, $"^XA{l.Type}^XZ")));

    private static PrinterConfig P(string id, string[] map, int order) =>
        new(id, "10.0.0." + order, 9100, map, ApplyOrientation.Side, order);

    // ---- CanPrint truth table (A5 → B4) ---------------------------------------------------------

    public static IEnumerable<object[]> CanPrintCases()
    {
        // Named lifecycle setups → expected CanPrint
        yield return ["freshly advised, never printed", "advised", true];
        yield return ["printed once, not authorized", "printed", false];
        yield return ["verified, not authorized", "verified", false];
        yield return ["held for intervention", "held", false];
        yield return ["reprint authorized", "authorized", true];
    }

    [Theory]
    [MemberData(nameof(CanPrintCases))]
    public void CanPrint_ReflectsLifecycleState(string because, string setup, bool expected)
    {
        var order = new TransportOrder("BLIND1", "L1", Labels(("Shipping", "S1")), T0);
        switch (setup)
        {
            case "printed":
                order.CompletePrintRun(T0);
                break;
            case "verified":
                order.CompletePrintRun(T0);
                order.MarkVerified(T0);
                break;
            case "held":
                order.CompletePrintRun(T0);
                order.MarkVerifyFailed(T0);
                break;
            case "authorized":
                order.CompletePrintRun(T0);
                order.MarkVerifyFailed(T0);
                order.AuthorizeReprint("operator");
                break;
        }

        Assert.True(expected == order.CanPrint, $"{because}: expected CanPrint={expected} but was {order.CanPrint}");
    }

    [Fact]
    public void PrintCount_IsMonotonic_AcrossAuthorizedReprints()
    {
        var order = new TransportOrder("BLIND1", "L1", Labels(("Shipping", "S1")), T0);
        order.CompletePrintRun(T0);
        Assert.Equal(1, order.PrintCount);

        order.MarkVerifyFailed(T0);
        order.AuthorizeReprint("relabel");
        order.CompletePrintRun(T0.AddMinutes(1));

        Assert.Equal(2, order.PrintCount);
        Assert.Equal(T0, order.FirstPrintedAt);            // stable
        Assert.Equal(T0.AddMinutes(1), order.LastPrintedAt); // advances
        Assert.Null(order.ReprintAuthorizationReason);     // consumed by the run
    }

    [Fact]
    public void OverwriteAdvice_ResetsCounterAndLifecycle_NewGeneration()
    {
        var order = new TransportOrder("BLIND1", "L1", Labels(("Shipping", "S1")), T0);
        order.CompletePrintRun(T0);
        order.MarkVerifyFailed(T0);
        order.AuthorizeReprint("relabel");

        order.OverwriteAdvice("L1", Labels(("Shipping", "S2")), T0.AddHours(1));

        Assert.Equal(TransportOrderStatus.Advised, order.Status);
        Assert.Equal(0, order.PrintCount);
        Assert.Null(order.FirstPrintedAt);
        Assert.Null(order.LastPrintedAt);
        Assert.Null(order.VerifyFailedAt);
        Assert.Null(order.ReprintAuthorizationReason);
        Assert.True(order.CanPrint);
    }

    // ---- Induct gate + persistence (B2, senior spy-store rows) ----------------------------------

    private static (InductService Svc, CountingTransportOrderStore Store, CapturingPrinterGateway Gw)
        BuildInduct(LineConfig line, IEnumerable<PrinterState>? states = null)
    {
        var store = new CountingTransportOrderStore();
        var lines = new InMemoryLineProvider().Add(line, states);
        var gw = new CapturingPrinterGateway();
        var svc = new InductService(store, lines, new PrinterSelectionService(), gw, new TestClock(T0), new InMemorySettingsProvider());
        return (svc, store, gw);
    }

    [Fact]
    public async Task Induct_NoActiveOrder_ReturnsNoActiveOrder_NoUpsert()
    {
        var (svc, store, _) = BuildInduct(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        var result = await svc.InductAsync("L1", "MISSING");

        Assert.Equal(InductStatus.NoActiveOrder, result.Status);
        Assert.Equal(0, store.UpsertCount);
    }

    [Fact]
    public async Task Induct_NoLabels_ReturnsNoData_NoUpsert()
    {
        var (svc, store, _) = BuildInduct(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        store.Seed(new TransportOrder("BLIND1", "L1", Labels(), T0));

        var result = await svc.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.NoData, result.Status);
        Assert.Equal(0, store.UpsertCount);
    }

    [Fact]
    public async Task Induct_FullRun_Prints_IncrementsCounter_UpsertsOnce()
    {
        var (svc, store, gw) = BuildInduct(new LineConfig("L1",
            [P("Ship1", ["Shipping"], 0), P("Cont1", ["Content"], 1)]));
        store.Seed(new TransportOrder("BLIND1", "L1", Labels(("Shipping", "S1"), ("Content", "C1")), T0));

        var result = await svc.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.Printed, result.Status);
        Assert.Equal(2, gw.Jobs.Count);
        Assert.Equal(1, store.UpsertCount);
        var order = await store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(1, order!.PrintCount);
        Assert.Equal(TransportOrderStatus.Printed, order.Status);
        Assert.Equal(T0, order.FirstPrintedAt);
        Assert.True(order.PrintStateFor("Shipping").Printed);
        Assert.True(order.PrintStateFor("Content").Printed);
    }

    [Fact]
    public async Task Induct_PartialRun_Upserts_ButDoesNotCountOrAdvance()
    {
        // Only Shipping has a printer; Content has none → partial.
        var (svc, store, gw) = BuildInduct(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        store.Seed(new TransportOrder("BLIND1", "L1", Labels(("Shipping", "S1"), ("Content", "C1")), T0));

        var result = await svc.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.PartiallyPrinted, result.Status);
        Assert.Single(gw.Jobs);
        Assert.Equal(1, store.UpsertCount); // persisted per-label state...
        var order = await store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(0, order!.PrintCount);                       // ...but no count
        Assert.Equal(TransportOrderStatus.Advised, order.Status); // and no advance
        Assert.True(order.PrintStateFor("Shipping").Printed);
        Assert.False(order.PrintStateFor("Content").Printed);
    }

    [Fact]
    public async Task Induct_NoPrinterForAnyLabel_DoesNotUpsert()
    {
        var (svc, store, gw) = BuildInduct(new LineConfig("L1", [P("Par1", ["Parcel"], 0)]));
        store.Seed(new TransportOrder("BLIND1", "L1", Labels(("Shipping", "S1")), T0));

        var result = await svc.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.NoPrinter, result.Status);
        Assert.Empty(gw.Jobs);
        Assert.Equal(0, store.UpsertCount);
    }

    [Fact]
    public async Task Induct_AlreadyPrinted_NotAuthorized_NoReprint_NoUpsert()
    {
        var (svc, store, gw) = BuildInduct(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        var order = new TransportOrder("BLIND1", "L1", Labels(("Shipping", "S1")), T0);
        order.CompletePrintRun(T0); // already printed once
        store.Seed(order);

        var result = await svc.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.NoReprint, result.Status);
        Assert.Empty(gw.Jobs);
        Assert.Equal(0, store.UpsertCount);
    }

    [Fact]
    public async Task Induct_Authorized_FullRun_ConsumesAuthorization_CounterTo2()
    {
        var (svc, store, gw) = BuildInduct(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        var order = new TransportOrder("BLIND1", "L1", Labels(("Shipping", "S1")), T0);
        order.CompletePrintRun(T0);
        order.MarkVerifyFailed(T0);
        order.AuthorizeReprint("operator relabel");
        store.Seed(order);

        var result = await svc.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.Printed, result.Status);
        Assert.Single(gw.Jobs);
        var reprinted = await store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(2, reprinted!.PrintCount);
        Assert.Equal(TransportOrderStatus.Printed, reprinted.Status);
        Assert.Null(reprinted.ReprintAuthorizationReason);
    }

    [Fact]
    public async Task Induct_Authorized_PartialRun_KeepsAuthorization()
    {
        // Authorized carton with two types but only one printer → partial; authorization must remain.
        var (svc, store, _) = BuildInduct(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        var order = new TransportOrder("BLIND1", "L1", Labels(("Shipping", "S1"), ("Content", "C1")), T0);
        order.CompletePrintRun(T0);
        order.MarkVerifyFailed(T0);
        order.AuthorizeReprint("operator relabel");
        store.Seed(order);

        var result = await svc.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.PartiallyPrinted, result.Status);
        var after = await store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(TransportOrderStatus.ReprintAuthorized, after!.Status); // not consumed
        Assert.Equal(1, after.PrintCount);                                   // no count on partial
        Assert.True(after.CanPrint);                                         // can still retry
    }

    [Fact]
    public async Task Induct_MissingLineConfig_Throws()
    {
        var store = new CountingTransportOrderStore();
        var lines = new InMemoryLineProvider(); // no line registered
        var svc = new InductService(store, lines, new PrinterSelectionService(), new CapturingPrinterGateway(), new TestClock(T0), new InMemorySettingsProvider());
        store.Seed(new TransportOrder("BLIND1", "L1", Labels(("Shipping", "S1")), T0));

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.InductAsync("L1", "BLIND1").AsTask());
    }

    [Fact]
    public void FromAssignments_EmptyRun_ReturnsPrinted_ContractQuirk()
    {
        // Senior-flagged: an empty assignment list reports Printed. Pinned so a future change is deliberate.
        var result = InductResult.FromAssignments([]);
        Assert.Equal(InductStatus.Printed, result.Status);
    }
}

