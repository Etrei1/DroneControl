using System.Net.Sockets;

namespace DroneControl.Core.Transport;

public sealed class UdpDroneTransport : IDroneTransport
{
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _receiveCts = new();
    private readonly Task _receiveTask;

    public event Action<byte[]>? DataReceived;

    public string Description { get; }

    public UdpDroneTransport(string host, int port)
    {
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Connect(host, port);
        Description = $"UDP {host}:{port}";
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));
    }

    public async ValueTask SendAsync(byte[] data, CancellationToken cancellationToken)
    {
        await _udp.SendAsync(data, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                DataReceived?.Invoke(result.Buffer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                // ICMP «port unreachable»: дрон ещё не поднял порт. Windows сообщает об этом
                // на следующем Receive. Продолжаем слушать.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _receiveCts.Cancel();
        _udp.Dispose();
        try
        {
            await _receiveTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Цикл приёма уже завершается, его ошибки после закрытия сокета неинтересны.
        }
        _receiveCts.Dispose();
    }
}
