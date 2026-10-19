using DroneControl.Core;

namespace DroneControl.Tests;

public class DroneProtocolTests
{
    [Fact]
    public void NeutralPacket_MatchesOriginalFormat()
    {
        var packet = DroneProtocol.BuildControlPacket(ControlState.Neutral);

        // 03 66 RR PP TT YY FF CS 99, CS = 80^80^00^80^00 = 80
        Assert.Equal(new byte[] { 0x03, 0x66, 0x80, 0x80, 0x00, 0x80, 0x00, 0x80, 0x99 }, packet);
    }

    [Fact]
    public void Checksum_IsXorOfChannelsAndFlags()
    {
        var state = new ControlState(Roll: 200, Pitch: 50, Throttle: 128, Yaw: 10, Flags: DroneFlags.TakeOff);

        var packet = DroneProtocol.BuildControlPacket(state);

        Assert.Equal((byte)(200 ^ 50 ^ 128 ^ 10 ^ 0x01), packet[7]);
        Assert.Equal(0x01, packet[6]);
    }

    [Fact]
    public void Axes_AreClampedToOne()
    {
        var packet = DroneProtocol.BuildControlPacket(new ControlState(0, 0, 0, 0, DroneFlags.None));

        Assert.Equal(1, packet[2]);
        Assert.Equal(1, packet[3]);
        Assert.Equal(1, packet[5]);
    }

    [Fact]
    public void ThrottleOne_BecomesZero()
    {
        var packet = DroneProtocol.BuildControlPacket(ControlState.Neutral with { Throttle = 1 });

        Assert.Equal(0, packet[4]);
    }

    [Theory]
    [InlineData(0, 128)]
    [InlineData(100, 255)]
    [InlineData(-100, 1)]
    [InlineData(50, 192)]
    [InlineData(-50, 65)]
    [InlineData(500, 255)]
    [InlineData(-500, 1)]
    public void AxisFromPercent_MapsToByteRange(double percent, int expected)
    {
        Assert.Equal(expected, DroneProtocol.AxisFromPercent(percent));
    }

    [Fact]
    public void ServicePackets_MatchOriginal()
    {
        Assert.Equal(new byte[] { 0x01, 0x01 }, DroneProtocol.Heartbeat());
        Assert.Equal(new byte[] { 0x08, 0x01 }, DroneProtocol.ExitControl());
    }
}
