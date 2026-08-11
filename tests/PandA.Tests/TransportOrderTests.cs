using PandA.Core;
using Xunit;

namespace PandA.Tests;

public sealed class TransportOrderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TransportOrder NewOrder() =>
        new("BLIND1", "L1", new PandaLabelSet([new Label("Shipping", "SHIP1", "^XA^XZ")]), T0);

    [Fact]
    public void FreshlyAdvised_CanPrint()
    {
        var order = NewOrder();
        Assert.True(order.CanPrint);
        Assert.Equal(0, order.PrintCount);
    }

    [Fact]
    public void CompletePrintRun_IncrementsCount_AndBlocksFurtherPrinting()
    {
        var order = NewOrder();
        order.CompletePrintRun(T0);

        Assert.Equal(1, order.PrintCount);
        Assert.Equal(TransportOrderStatus.Printed, order.Status);
        Assert.False(order.CanPrint); // no reprint without authorization
    }

    [Fact]
    public void VerifyFailed_HoldsForIntervention_AndStaysUnprintable()
    {
        var order = NewOrder();
        order.CompletePrintRun(T0);
        order.MarkVerifyFailed(T0);

        Assert.Equal(TransportOrderStatus.HeldForIntervention, order.Status);
        Assert.False(order.CanPrint);
        Assert.Equal(1, order.PrintCount); // history preserved, not reset
    }

    [Fact]
    public void AuthorizeReprint_MakesPrintableWithoutResettingCount()
    {
        var order = NewOrder();
        order.CompletePrintRun(T0);
        order.MarkVerifyFailed(T0);

        order.AuthorizeReprint("operator relabel");

        Assert.Equal(TransportOrderStatus.ReprintAuthorized, order.Status);
        Assert.True(order.CanPrint);
        Assert.Equal(1, order.PrintCount);
        Assert.Equal("operator relabel", order.ReprintAuthorizationReason);
    }

    [Fact]
    public void SecondCompletePrintRun_AfterAuthorization_CountsTwo_AndConsumesAuthorization()
    {
        var order = NewOrder();
        order.CompletePrintRun(T0);
        order.MarkVerifyFailed(T0);
        order.AuthorizeReprint("relabel");

        order.CompletePrintRun(T0.AddMinutes(1));

        Assert.Equal(2, order.PrintCount);
        Assert.Equal(TransportOrderStatus.Printed, order.Status);
        Assert.False(order.CanPrint); // authorization consumed
        Assert.Null(order.ReprintAuthorizationReason);
    }

    [Fact]
    public void MarkVerified_CompletesCarton()
    {
        var order = NewOrder();
        order.CompletePrintRun(T0);
        order.MarkVerified(T0.AddMinutes(1));

        Assert.Equal(TransportOrderStatus.Verified, order.Status);
        Assert.Equal(T0.AddMinutes(1), order.VerifiedAt);
        Assert.False(order.CanPrint);
    }

    [Fact]
    public void FirstPrintedAt_IsStableAcrossRuns_LastPrintedAt_Advances()
    {
        var order = NewOrder();
        order.CompletePrintRun(T0);
        order.MarkVerifyFailed(T0);
        order.AuthorizeReprint("relabel");
        order.CompletePrintRun(T0.AddMinutes(5));

        Assert.Equal(T0, order.FirstPrintedAt);
        Assert.Equal(T0.AddMinutes(5), order.LastPrintedAt);
    }

    [Fact]
    public void OverwriteAdvice_ResetsLifecycleAndCounter()
    {
        var order = NewOrder();
        order.CompletePrintRun(T0);

        order.OverwriteAdvice("L1", new PandaLabelSet([new Label("Content", "CON1", "^XA^XZ")]), T0.AddMinutes(1));

        Assert.Equal(TransportOrderStatus.Advised, order.Status);
        Assert.Equal(0, order.PrintCount);
        Assert.True(order.CanPrint);
        Assert.False(order.PrintStateFor("Content").Printed);
    }
}
