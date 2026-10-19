using System.Collections.Concurrent;
using DroneControl.Core;
using DroneControl.Core.Transport;

namespace DroneControl.Tests;

public class DroneClientTests
{
    [Fact]
    public async Task PulseFlag_ExpiresAfterDuration()
    {
        var time = new ManualTimeProvider();
        await using var client = new DroneClient(new DryRunTransport(), time);

        client.PulseFlag(DroneFlags.TakeOff, TimeSpan.FromSeconds(1));
        Assert.True(client.Snapshot().Flags.HasFlag(DroneFlags.TakeOff));

        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.True(client.Snapshot().Flags.HasFlag(DroneFlags.TakeOff));

        time.Advance(TimeSpan.FromMilliseconds(2));
        Assert.Equal(DroneFlags.None, client.Snapshot().Flags);
    }

    [Fact]
    public async Task PersistentAndPulseFlags_AreCombined()
    {
        var time = new ManualTimeProvider();
        await using var client = new DroneClient(new DryRunTransport(), time);

        client.SetFlag(DroneFlags.Headless, true);
        client.PulseFlag(DroneFlags.Land, TimeSpan.FromSeconds(1));
        Assert.Equal(DroneFlags.Headless | DroneFlags.Land, client.Snapshot().Flags);

        client.ClearPulses();
        Assert.Equal(DroneFlags.Headless, client.Snapshot().Flags);

        client.SetFlag(DroneFlags.Headless, false);
        Assert.Equal(DroneFlags.None, client.Snapshot().Flags);
    }

    [Fact]
    public async Task Hover_CentersSticks_KeepsThrottle()
    {
        await using var client = new DroneClient(new DryRunTransport());
        client.SetSticks(10, 20, 30);
        client.SetThrottle(200);

        client.Hover();

        var state = client.Snapshot();
        Assert.Equal(ControlState.Center, state.Roll);
        Assert.Equal(ControlState.Center, state.Pitch);
        Assert.Equal(ControlState.Center, state.Yaw);
        Assert.Equal(200, state.Throttle);
    }

    [Fact]
    public async Task MoveAsync_ReturnsToHover_WhenCancelled()
    {
        await using var client = new DroneClient(new DryRunTransport());
        using var cts = new CancellationTokenSource();

        var move = FlightPresets.MoveAsync(client, 0, 100, 0, TimeSpan.FromSeconds(30), cts.Token);
        Assert.Equal(255, client.Snapshot().Pitch);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => move);
        Assert.Equal(ControlState.Center, client.Snapshot().Pitch);
    }

    [Fact]
    public async Task ControlLoop_SendsPackets_AndExitOnDisable()
    {
        var transport = new RecordingTransport();
        await using var client = new DroneClient(transport);
        client.SetThrottle(77);

        client.EnableControl();
        await transport.WaitForAsync(p => p.Length == DroneProtocol.ControlPacketLength, count: 3);
        await client.DisableControlAsync();

        var control = transport.Sent.First(p => p.Length == DroneProtocol.ControlPacketLength);
        Assert.Equal(77, control[4]);
        Assert.Equal(DroneProtocol.ExitControl(), transport.Sent.Last());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan delta) => _ticks += delta.Ticks;
    }

    private sealed class RecordingTransport : IDroneTransport
    {
        private readonly ConcurrentQueue<byte[]> _sent = new();

        public event Action<byte[]>? DataReceived
        {
            add { }
            remove { }
        }

        public string Description => "test";

        public IReadOnlyList<byte[]> Sent => [.. _sent];

        public ValueTask SendAsync(byte[] data, CancellationToken cancellationToken)
        {
            _sent.Enqueue(data);
            return ValueTask.CompletedTask;
        }

        public async Task WaitForAsync(Func<byte[], bool> predicate, int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (_sent.Count(predicate) < count)
                await Task.Delay(10, timeout.Token);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
