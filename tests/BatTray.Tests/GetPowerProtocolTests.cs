using BatTray.Core.Hid.Protocols;

namespace BatTray.Tests;

public class GetPowerProtocolTests
{
    [Fact]
    public void BuildRequest_MatchesKnownFrame()
    {
        // 08 04 00 … 00 49 — the 0x49 checksum makes the frame sum to 0x55.
        var frame = GetPowerProtocol.BuildRequest();

        Assert.Equal(17, frame.Length);
        Assert.Equal(0x08, frame[0]);
        Assert.Equal(0x04, frame[1]);
        Assert.All(frame[2..16], b => Assert.Equal(0, b));
        Assert.Equal(0x49, frame[16]);
        Assert.True(GetPowerProtocol.HasValidChecksum(frame));
    }

    [Fact]
    public void BuildRequest_PadsToOutputLength_KeepingChecksumOnFirst17Bytes()
    {
        var frame = GetPowerProtocol.BuildRequest(65);

        Assert.Equal(65, frame.Length);
        Assert.Equal(0x49, frame[16]);
        Assert.All(frame[17..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void ParseNordicReply_DocumentedExample_Is35Percent()
    {
        // 08 04 00 00 00 02 23 00 0E 94 00 … 82 → 35 %, 3732 mV, not wired.
        var frame = Frame(0x08, 0x04, 0x00, 0x00, 0x00, 0x02, 0x23, 0x00, 0x0E, 0x94);
        frame[16] = 0x82;

        var reading = GetPowerProtocol.ParseNordicReply(frame);

        Assert.NotNull(reading);
        Assert.Equal(35, reading.Percent);
        Assert.False(reading.Charging);
    }

    [Fact]
    public void ParseNordicReply_Wired_ReportsCharging()
    {
        var frame = WithChecksum(Frame(0x08, 0x04, 0, 0, 0, 0, 80, 1));

        var reading = GetPowerProtocol.ParseNordicReply(frame);

        Assert.Equal(80, reading?.Percent);
        Assert.True(reading?.Charging);
    }

    [Theory]
    [InlineData(0)]   // asleep
    [InlineData(101)] // invalid
    public void ParseNordicReply_RejectsOutOfRangeLevel(byte level) =>
        Assert.Null(GetPowerProtocol.ParseNordicReply(WithChecksum(Frame(0x08, 0x04, 0, 0, 0, 0, level))));

    [Fact]
    public void ParseNordicReply_RejectsBadChecksum()
    {
        var frame = WithChecksum(Frame(0x08, 0x04, 0, 0, 0, 0, 50));
        frame[16] ^= 0xFF;

        Assert.Null(GetPowerProtocol.ParseNordicReply(frame));
    }

    [Fact]
    public void ParseAresonReply_LevelIsTenPercentSteps()
    {
        var reading = GetPowerProtocol.ParseAresonReply(WithChecksum(Frame(0x09, 0x04, 0x00, 0, 0, 0, 8, 1)));

        Assert.Equal(80, reading?.Percent);
        Assert.True(reading?.Charging);
    }

    [Fact]
    public void ParseAresonReply_RejectsWrongHeaderOrLevel()
    {
        Assert.Null(GetPowerProtocol.ParseAresonReply(WithChecksum(Frame(0x08, 0x04, 0, 0, 0, 0, 8))));
        Assert.Null(GetPowerProtocol.ParseAresonReply(WithChecksum(Frame(0x09, 0x04, 0, 0, 0, 0, 11))));
    }

    [Fact]
    public void ParseReplies_RejectShortFrames() =>
        Assert.Null(GetPowerProtocol.ParseNordicReply(new byte[] { 0x08, 0x04, 0, 0, 0, 0, 50, 0 }));

    [Fact]
    public void Models_DoNotIncludeRk100Receiver()
    {
        // 25A7:FA70 (the RK100's Compx receiver) answers differently: it must never be sent GetPower.
        Assert.DoesNotContain(GetPowerProtocol.Models, m => m.VendorId == 0x25A7 && m.ProductId == 0xFA70);
        Assert.DoesNotContain(GetPowerProtocol.Models, m => m.VendorId == 0x25A7 && m.ProductId is null);
    }

    private static byte[] Frame(params byte[] head)
    {
        var frame = new byte[17];
        head.CopyTo(frame, 0);
        return frame;
    }

    private static byte[] WithChecksum(byte[] frame)
    {
        frame[16] = GetPowerProtocol.ComputeChecksum(frame.AsSpan(0, 16));
        return frame;
    }
}
