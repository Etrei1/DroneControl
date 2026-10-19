using DroneControl.Core.Transport;

namespace DroneControl.Core;

/// <summary>
/// Держит текущее положение стиков и шлёт его дрону: heartbeat раз в секунду,
/// пакет управления каждые 50 мс (пока управление включено).
/// Сеттеры потокобезопасны и могут вызываться из UI и из пресетов одновременно.
/// </summary>
public sealed class DroneClient : IAsyncDisposable
{
    private const int FlagBits = 8;

    private readonly IDroneTransport _transport;
    private readonly TimeProvider _time;
    private readonly Lock _stateLock = new();
    private readonly long[] _pulseExpiry = new long[FlagBits];
    private readonly CancellationTokenSource _lifetime = new();

    private byte _roll = ControlState.Center;
    private byte _pitch = ControlState.Center;
    private byte _yaw = ControlState.Center;
    private byte _throttle;
    private DroneFlags _persistentFlags;

    private Task? _heartbeatTask;
    private Task? _controlTask;
    private CancellationTokenSource? _controlCts;
    private bool _disposed;

    /// <summary>Каждый отправленный пакет управления (вызывается из фонового потока).</summary>
    public event Action<byte[]>? ControlPacketSent;

    /// <summary>Ответы дрона (вызывается из фонового потока).</summary>
    public event Action<byte[]>? DataReceived;

    /// <summary>Ошибка отправки (вызывается из фонового потока).</summary>
    public event Action<Exception>? SendFailed;

    public DroneClient(IDroneTransport transport, TimeProvider? timeProvider = null)
    {
        _transport = transport;
        _time = timeProvider ?? TimeProvider.System;
        _transport.DataReceived += OnDataReceived;
    }

    public string TransportDescription => _transport.Description;

    public bool IsControlEnabled => _controlCts is not null;

    /// <summary>Запускает heartbeat. Дрон без него не считает пульт подключённым.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _heartbeatTask ??= Task.Run(() => RunLoopAsync(DroneProtocol.HeartbeatInterval, DroneProtocol.Heartbeat, _lifetime.Token));
    }

    /// <summary>Начинает слать пакеты управления каждые 50 мс.</summary>
    public void EnableControl()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_controlCts is not null)
            return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _controlCts = cts;
        _controlTask = Task.Run(() => RunLoopAsync(DroneProtocol.ControlInterval, NextControlPacket, cts.Token));
    }

    /// <summary>Останавливает пакеты управления и шлёт дрону «выход из режима управления».</summary>
    public async Task DisableControlAsync()
    {
        var cts = _controlCts;
        if (cts is null)
            return;

        _controlCts = null;
        cts.Cancel();
        if (_controlTask is not null)
            await _controlTask.ConfigureAwait(false);
        _controlTask = null;
        cts.Dispose();

        await TrySendAsync(DroneProtocol.ExitControl(), CancellationToken.None).ConfigureAwait(false);
    }

    public void SetSticks(byte roll, byte pitch, byte yaw)
    {
        lock (_stateLock)
        {
            _roll = roll;
            _pitch = pitch;
            _yaw = yaw;
        }
    }

    public void SetThrottle(byte throttle)
    {
        lock (_stateLock)
            _throttle = throttle;
    }

    /// <summary>Центрирует крен, тангаж и рыскание. Газ не трогает.</summary>
    public void Hover() => SetSticks(ControlState.Center, ControlState.Center, ControlState.Center);

    /// <summary>Постоянный флаг (режимы), держится пока не выключат.</summary>
    public void SetFlag(DroneFlags flag, bool enabled)
    {
        lock (_stateLock)
            _persistentFlags = enabled ? _persistentFlags | flag : _persistentFlags & ~flag;
    }

    /// <summary>Временный флаг: держится заданное время, потом сам снимается (как в оригинале).</summary>
    public void PulseFlag(DroneFlags flag, TimeSpan duration)
    {
        long expiry = _time.GetTimestamp() + (long)(duration.TotalSeconds * _time.TimestampFrequency);
        lock (_stateLock)
        {
            for (int bit = 0; bit < FlagBits; bit++)
            {
                if (((byte)flag & (1 << bit)) != 0)
                    _pulseExpiry[bit] = expiry;
            }
        }
    }

    public void ClearPulses()
    {
        lock (_stateLock)
            Array.Clear(_pulseExpiry);
    }

    public ControlState Snapshot()
    {
        lock (_stateLock)
        {
            long now = _time.GetTimestamp();
            var flags = _persistentFlags;
            for (int bit = 0; bit < FlagBits; bit++)
            {
                if (_pulseExpiry[bit] > now)
                    flags |= (DroneFlags)(1 << bit);
            }
            return new ControlState(_roll, _pitch, _throttle, _yaw, flags);
        }
    }

    private byte[] NextControlPacket()
    {
        var packet = DroneProtocol.BuildControlPacket(Snapshot());
        ControlPacketSent?.Invoke(packet);
        return packet;
    }

    private async Task RunLoopAsync(TimeSpan period, Func<byte[]> packetFactory, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(period);
        try
        {
            do
            {
                await TrySendAsync(packetFactory(), cancellationToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Нормальная остановка цикла.
        }
    }

    private async Task TrySendAsync(byte[] packet, CancellationToken cancellationToken)
    {
        try
        {
            await _transport.SendAsync(packet, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SendFailed?.Invoke(ex);
        }
    }

    private void OnDataReceived(byte[] data) => DataReceived?.Invoke(data);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        await DisableControlAsync().ConfigureAwait(false);
        _lifetime.Cancel();
        if (_heartbeatTask is not null)
            await _heartbeatTask.ConfigureAwait(false);

        _transport.DataReceived -= OnDataReceived;
        await _transport.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
    }
}
