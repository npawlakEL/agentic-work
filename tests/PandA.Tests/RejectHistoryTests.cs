using PandA.Core.Verification;
using PandA.Sim;
using Xunit;

namespace PandA.Tests;

public sealed class RejectHistoryTests
{
    [Theory]
    [InlineData(VerifyReasonCode.PandaLogicError, 0, "PandA Logic Error")]
    [InlineData(VerifyReasonCode.PassThroughCarton, 3, "Pass through Carton")]
    [InlineData(VerifyReasonCode.GapError, 4, "Gap Error")]
    [InlineData(VerifyReasonCode.InboundScannerNoRead, 7, "Inbound Scanner No Read")]
    [InlineData(VerifyReasonCode.VerifyScanError, 12, "Verify scan error")]
    [InlineData(VerifyReasonCode.IncorrectShippingLabelApplied, 21, "Incorrect shipping label applied")]
    [InlineData(VerifyReasonCode.ShippingLabelConflict, 33, "Shipping Label label conflict")]
    public void VerifyReasonCode_PreservesSourceNumbersAndDescriptions(VerifyReasonCode code, int value, string description)
    {
        Assert.Equal(value, (int)code);
        Assert.Equal(description, code.ToDescription());
    }

    [Fact]
    public async Task InMemoryRejectHistoryRepository_StoresRecordsInOrder()
    {
        var repository = new InMemoryRejectHistoryRepository();
        var first = RejectRecord.Create(101, 501, VerifyReasonCode.IncorrectShippingLabelApplied);
        var second = RejectRecord.Create(101, 502, VerifyReasonCode.VerifyScanError);

        await repository.AddAsync(first);
        await repository.AddAsync(second);

        Assert.Equal(new[] { first, second }, repository.Records);
    }

    [Fact]
    public void RejectRecord_CreateUsesStableReasonDescription()
    {
        var record = RejectRecord.Create(42, 84, VerifyReasonCode.ContentLabelNoRead);

        Assert.Equal(42, record.PandaDataId);
        Assert.Equal(84, record.CartonListId);
        Assert.Equal("Content Label no read", record.Reason);
    }
}
