namespace DroneControl.Core.Transport;

public interface IDroneTransport : IAsyncDisposable
{
    /// <summary>Данные, пришедшие от дрона (wifiId, счётчики фото/видео и т.п.).</summary>
    event Action<byte[]>? DataReceived;

    string Description { get; }

    ValueTask SendAsync(byte[] data, CancellationToken cancellationToken);
}
