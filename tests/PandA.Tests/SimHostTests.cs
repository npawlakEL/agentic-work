using PandA.Sim;
using PandA.Sim.Messaging;
using Xunit;

namespace PandA.Tests;

/// <summary>
/// Tests for the bare-bones message layer + <see cref="SimHost"/>: 281/286 frame round-trips and the
/// end-to-end 281 induct → print → 286 verify happy path driven through the message boundary.
/// </summary>
public sealed class SimHostTests
{
    [Fact]
    public void Frame_ParsesAndRoundTrips_281Example()
    {
        // The canonical example from sdisp_VLC2DB_Msgs_Process_PA.
        const string wire = "<281,1,1,1,1,8,0,0154006001,0,0,0,0,0,0,0>";
        var frame = PaFrame.Parse(wire);

        Assert.Equal(281, frame.Code);
        Assert.Equal(wire, frame.ToWire());

        var scan = InductScanMessage.FromFrame(frame);
        Assert.True(scan.IsInduct);
        Assert.Equal(1, scan.DeviceId);
        Assert.Equal(8, scan.SeqNum);
        Assert.Equal("0154006001", scan.BlindLabel);
    }

    [Fact]
    public void InductScanMessage_ToFrame_FromFrame_RoundTrips()
    {
        var msg = new InductScanMessage(1, 1, 1, 1, 42, 0, "BLIND9");
        var back = InductScanMessage.FromFrame(PaFrame.Parse(msg.ToFrame().ToWire()));
        Assert.Equal(msg, back);
    }

    [Fact]
    public void VerifyScanMessage_PipeDelimitedBuffer_RoundTrips()
    {
        var msg = new VerifyScanMessage(1, 1, 1, 5, 8, ["123456798", "0154006001"]);
        var wire = msg.ToFrame().ToWire();

        Assert.Contains("123456798|0154006001", wire);
        var back = VerifyScanMessage.FromFrame(PaFrame.Parse(wire));
        Assert.Equal(msg.LabelBuffer, back.LabelBuffer);
        Assert.Equal(5, back.DeviceId);
    }

    [Fact]
    public void Frame_MissingBrackets_Throws()
    {
        Assert.Throws<FormatException>(() => PaFrame.Parse("281,1,2,3"));
    }

    [Fact]
    public void SimHost_SeedsCartons()
    {
        var host = new SimHost();
        Assert.Equal(3, host.BlindLabels.Count);
        Assert.Contains("0154006001", host.BlindLabels);
    }

    [Fact]
    public void SimHost_Describe_ShowsAdvisedLabels_AndInitialState()
    {
        var host = new SimHost();
        var text = host.Describe("0154006001");

        Assert.Contains("status     : Advised", text);
        Assert.Contains("printCount : 0", text);
        Assert.Contains("Shipping", text);
        Assert.Contains("Content", text);
    }

    [Fact]
    public async Task SimHost_Run_DrivesInductPrintVerify_ToVerified()
    {
        var host = new SimHost();
        var log = await host.RunAsync("0154006003"); // 3-label carton
        var text = string.Join('\n', log);

        Assert.Contains("IN  281 PANDA_SCAN_INBOUND", text);
        Assert.Contains("induct → Printed", text);
        Assert.Contains("OUT PRINT", text);
        Assert.Contains("IN  286 PANDA_SCAN_VERIFY", text);
        Assert.Contains("station Verified", text);
        Assert.Contains("printCount=1", text);

        // Three labels → three print jobs emitted in the transcript.
        Assert.Equal(3, log.Count(l => l.Contains("OUT PRINT")));

        // After the run the carton is actually Verified in the store.
        Assert.Contains("0154006003: Verified", text);
    }
}
