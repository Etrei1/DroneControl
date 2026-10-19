namespace DroneControl.Core.Transport;

/// <summary>Ничего не отправляет. Нужен, чтобы проверить интерфейс и пакеты без дрона.</summary>
public sealed class DryRunTransport : IDroneTransport
{
    public event Action<byte[]>? DataReceived
    {
        add { }
        remove { }
    }

    public string Description => "Dry-run (без отправки)";

    public ValueTask SendAsync(byte[] data, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
