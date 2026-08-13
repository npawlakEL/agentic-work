using PandA.Core.Domain;
using PandA.Core.Services;
using PandA.Sim.Stores;

namespace PandA.Tests;

public sealed class XRefStoreTests
{
    [Fact]
    public async Task AssociateAsync_ExistingMapping_ReturnsAlreadyAssociated()
    {
        var store = new InMemoryXRefStore();
        var service = new XRefService(store);

        var first = await service.AssociateAsync("TU-001", "BL-001", BarcodeType.BL);
        var second = await service.AssociateAsync("TU-001", "BL-001", BarcodeType.BL);

        Assert.Equal(XRefAssociateResult.Success, first);
        Assert.Equal(XRefAssociateResult.AlreadyAssociated, second);
        Assert.Single(store.All);
    }

    [Fact]
    public async Task DisassociateAsync_RemovesMatchingBarcodeAndTypeAndReturnsCount()
    {
        var store = new InMemoryXRefStore();
        var service = new XRefService(store);
        await service.AssociateAsync("TU-001", "OLPN-001", BarcodeType.oLPN);
        await service.AssociateAsync("TU-002", "OLPN-001", BarcodeType.oLPN);
        await service.AssociateAsync("TU-003", "OLPN-001", BarcodeType.UPC);

        var removed = await service.DisassociateAsync("OLPN-001", BarcodeType.oLPN);
        var remaining = await store.FindTuIdsByBarcodeAsync("OLPN-001");

        Assert.Equal(2, removed);
        Assert.Equal(new[] { "TU-003" }, remaining);
    }

    [Fact]
    public async Task FindTuIdsByBarcodeAsync_AfterAssociate_ReturnsTuId()
    {
        var store = new InMemoryXRefStore();
        var service = new XRefService(store);

        await service.AssociateAsync("TU-001", "UPC-001", BarcodeType.UPC);

        var tuIds = await store.FindTuIdsByBarcodeAsync("UPC-001");
        Assert.Equal(new[] { "TU-001" }, tuIds);
    }

    [Fact]
    public async Task DisassociateAsync_NonExistentBarcode_ReturnsZeroWithoutThrowing()
    {
        var store = new InMemoryXRefStore();
        var service = new XRefService(store);

        var removed = await service.DisassociateAsync("MISSING", BarcodeType.oLPN);

        Assert.Equal(0, removed);
    }

    [Fact]
    public async Task FindTuIdsByBarcodeAsync_SharedUpc_ReturnsAllTuIds()
    {
        var store = new InMemoryXRefStore();
        var service = new XRefService(store);

        await service.AssociateAsync("TU-001", "UPC-SHARED", BarcodeType.UPC);
        await service.AssociateAsync("TU-002", "UPC-SHARED", BarcodeType.UPC);

        var tuIds = await store.FindTuIdsByBarcodeAsync("UPC-SHARED");

        Assert.Equal(new[] { "TU-001", "TU-002" }, tuIds);
    }
}
