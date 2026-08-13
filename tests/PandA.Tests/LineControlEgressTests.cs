using PandA.Core;
using PandA.Core.Control;
using PandA.Sim;
using Xunit;

namespace PandA.Tests;

public sealed class LineControlEgressTests
{
    [Fact]
    public void ShutLine_UsesPlcZoneRemoteStopTag()
    {
        var command = LineControlCommand.ShutLine("L1", plcZone: 3, plcDbName: "PLC_A");

        Assert.Equal(LineControl.ShutLine, command.Control);
        Assert.Equal("L1", command.LineId);
        Assert.Equal(3, command.PlcArrayIndex);
        Assert.Equal("vConv.ZoneAr", command.PlcTagNamespace);
        Assert.Equal("vConv.ZoneAr[3].RemoteStop", command.TagName);
        Assert.Equal(1, command.TagValue);
        Assert.Equal("PLC_A", command.PlcDbName);
    }

    [Fact]
    public void SlowLine_UsesSorterPlcRecIdSlowFlagTag()
    {
        var command = LineControlCommand.SlowLine("L1", sorterPlcRecId: 2);

        Assert.Equal(LineControl.SlowLine, command.Control);
        Assert.Equal(2, command.PlcArrayIndex);
        Assert.Equal("vPanda.SlowPanda", command.PlcTagNamespace);
        Assert.Equal("vPanda.SlowPanda[2].SlowFlag", command.TagName);
    }

    [Fact]
    public void Balanced_HasNoTagName()
    {
        var command = LineControlCommand.Balanced("L1");

        Assert.Equal(LineControl.Balanced, command.Control);
        Assert.Equal(string.Empty, command.TagName);
        Assert.Equal(0, command.TagValue);
    }

    [Fact]
    public async Task CapturingGateway_RecordsCommandsInOrder()
    {
        var gateway = new CapturingLinePlcGateway();
        var slow = LineControlCommand.SlowLine("L1", 2);
        var shut = LineControlCommand.ShutLine("L1", 3);

        await gateway.SendLineControlAsync(slow);
        await gateway.SendLineControlAsync(shut);

        Assert.Equal(new[] { slow, shut }, gateway.Commands);
    }
}
