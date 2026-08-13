using PandA.Core;
using PandA.Core.Induct;
using PandA.Sim;
using Xunit;

namespace PandA.Tests;

public sealed class CartonRunRepositoryTests
{
    [Fact]
    public async Task CreateRunAsync_AssignsAscendingIdsStartingAtOne()
    {
        var repository = new InMemoryCartonRunRepository();

        var first = await repository.CreateRunAsync(CreateRecord(pandaDataId: 101));
        var second = await repository.CreateRunAsync(CreateRecord(pandaDataId: 102));
        var third = await repository.CreateRunAsync(CreateRecord(pandaDataId: 103));

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(3, third);
        Assert.Equal(new[] { 1L, 2L, 3L }, repository.Records.Select(record => record.RunId));
    }

    [Fact]
    public async Task UpdatePrinterAsync_SetsAssignedPrinterOnly()
    {
        var repository = new InMemoryCartonRunRepository();
        var createdAt = DateTimeOffset.Parse("2026-08-13T10:00:00Z");
        var original = CreateRecord(
            pandaDataId: 101,
            statusAtInduct: CartonStatus.PrintReady,
            assignedPrinter: null,
            createdAt: createdAt);
        var runId = await repository.CreateRunAsync(original);
        var beforeUpdate = Assert.Single(repository.Records);

        await repository.UpdatePrinterAsync(runId, "Printer-01");

        var afterUpdate = Assert.Single(repository.Records);
        Assert.Equal(beforeUpdate with { AssignedPrinter = "Printer-01" }, afterUpdate);
    }

    [Fact]
    public async Task UpdatePrinterAsync_UnknownRunIdThrows()
    {
        var repository = new InMemoryCartonRunRepository();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await repository.UpdatePrinterAsync(404, "Printer-01"));
    }

    [Fact]
    public async Task GetRunsForOrderAsync_ReturnsMatchingPandaDataIdOrderedByRunId()
    {
        var repository = new InMemoryCartonRunRepository();
        var firstMatch = await repository.CreateRunAsync(CreateRecord(pandaDataId: 101));
        await repository.CreateRunAsync(CreateRecord(pandaDataId: 202));
        var secondMatch = await repository.CreateRunAsync(CreateRecord(pandaDataId: 101));

        var records = await repository.GetRunsForOrderAsync(101);

        Assert.Equal(new[] { firstMatch, secondMatch }, records.Select(record => record.RunId));
        Assert.All(records, record => Assert.Equal(101, record.PandaDataId));
    }

    [Fact]
    public async Task GetRunsForOrderAsync_TwoRunsForSamePandaDataIdModelRanTwiceHistory()
    {
        var repository = new InMemoryCartonRunRepository();
        var failedRun = await repository.CreateRunAsync(
            CreateRecord(pandaDataId: 101, statusAtInduct: CartonStatus.VerifyFail));
        var rerun = await repository.CreateRunAsync(
            CreateRecord(pandaDataId: 101, statusAtInduct: CartonStatus.PrintReady));

        var records = await repository.GetRunsForOrderAsync(101);

        Assert.Equal(2, records.Count);
        Assert.Equal(new[] { failedRun, rerun }, records.Select(record => record.RunId));
        Assert.Equal(CartonStatus.VerifyFail, records[0].StatusAtInduct);
        Assert.Equal(CartonStatus.PrintReady, records[1].StatusAtInduct);
        Assert.NotEqual(records[0].RunId, records[1].RunId);
    }

    private static CartonRunRecord CreateRecord(
        long pandaDataId,
        CartonStatus statusAtInduct = CartonStatus.PrintReady,
        string? assignedPrinter = null,
        DateTimeOffset? createdAt = null) =>
        CartonRunRecord.Create(
            tuId: $"TU-{pandaDataId}",
            pandaDataId: pandaDataId,
            lineId: "Line-01",
            sorterNumber: 1,
            sorterMode: 2,
            deviceId: 1,
            seqNum: 10,
            labelStatus: 0,
            scannedLabels: ["SHIP1", "CONTENT1"],
            length: 20,
            width: 10,
            height: 8,
            weight: 5,
            frontGap: 30,
            statusAtInduct: statusAtInduct,
            assignedPrinter: assignedPrinter,
            destinationLane: 7,
            createdAt: createdAt);
}
