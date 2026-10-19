using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DroneControl.Core;
using DroneControl.Core.Transport;

namespace DroneControl.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxLogLines = 300;
    private const double ThrottleHoldValue = ControlState.Center;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _uiTimer;

    private DroneClient? _client;
    private CancellationTokenSource? _presetCts;
    private volatile byte[]? _lastPacket;
    private string? _lastLoggedPacket;
    private string? _lastSendError;

    public MainViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, OnUiTimerTick, _dispatcher);
        _uiTimer.Start();
        AddLog("Готово. Подключитесь к Wi-Fi дрона и нажмите «Подключить».");
    }

    public ObservableCollection<string> LogLines { get; } = [];

    // ── Подключение ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditConnection))]
    private string _host = DroneProtocol.DefaultHost;

    [ObservableProperty]
    private int _port = DroneProtocol.DefaultPort;

    [ObservableProperty]
    private bool _isDryRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditConnection), nameof(ConnectButtonText), nameof(StatusText))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isControlEnabled;

    public bool CanEditConnection => !IsConnected;

    public string ConnectButtonText => IsConnected ? "Отключить" : "Подключить";

    public string StatusText => (IsConnected, IsControlEnabled) switch
    {
        (false, _) => "Не подключено",
        (true, false) => $"Подключено · {_client?.TransportDescription} · только heartbeat",
        (true, true) => $"Подключено · {_client?.TransportDescription} · управление активно",
    };

    // ── Газ ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThrottlePercent))]
    private double _throttle;

    [ObservableProperty]
    private bool _isAltitudeHold;

    public double ThrottlePercent => Throttle / 255.0 * 100.0;

    // ── Параметры пресетов ─────────────────────────────────────────────────

    [ObservableProperty]
    private double _power = 50;

    [ObservableProperty]
    private double _durationSeconds = 1.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPresetRunning))]
    private string? _activePreset;

    public bool IsPresetRunning => ActivePreset is not null;

    // ── Экспериментальные флаги ────────────────────────────────────────────

    [ObservableProperty]
    private bool _isHeadless;

    [ObservableProperty]
    private bool _isReturnOrUnlock;

    [ObservableProperty]
    private bool _isLight;

    // ── Индикаторы ─────────────────────────────────────────────────────────

    [ObservableProperty]
    private int _rollValue = ControlState.Center;

    [ObservableProperty]
    private int _pitchValue = ControlState.Center;

    [ObservableProperty]
    private int _yawValue = ControlState.Center;

    [ObservableProperty]
    private int _throttleValue;

    [ObservableProperty]
    private string _flagsText = "—";

    [ObservableProperty]
    private string _lastPacketHex = "—";

    // ── Подключение / управление ───────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnected)
        {
            await DisconnectAsync();
            return;
        }

        try
        {
            IDroneTransport transport = IsDryRun ? new DryRunTransport() : new UdpDroneTransport(Host, Port);
            var client = new DroneClient(transport);
            client.ControlPacketSent += packet => _lastPacket = packet;
            client.DataReceived += data => Post(() => AddLog($"← {ToHex(data)}"));
            client.SendFailed += ex => Post(() => OnSendFailed(ex));

            client.SetThrottle(ToByte(Throttle));
            client.SetFlag(DroneFlags.Headless, IsHeadless);
            client.SetFlag(DroneFlags.ReturnOrUnlock, IsReturnOrUnlock);
            client.SetFlag(DroneFlags.Light, IsLight);
            client.Start();

            _client = client;
            IsConnected = true;
            AddLog($"Подключено: {client.TransportDescription}. Heartbeat 01 01 раз в секунду.");
        }
        catch (Exception ex)
        {
            AddLog($"Ошибка подключения: {ex.Message}");
        }
    }

    private async Task DisconnectAsync()
    {
        CancelPreset();
        var client = _client;
        _client = null;
        IsControlEnabled = false;
        IsConnected = false;
        _lastPacket = null;

        if (client is not null)
            await client.DisposeAsync();

        AddLog("Отключено.");
    }

    partial void OnIsControlEnabledChanged(bool value)
    {
        if (_client is null)
        {
            if (value)
            {
                AddLog("Сначала подключитесь к дрону.");
                _dispatcher.InvokeAsync(() => IsControlEnabled = false);
            }
            return;
        }

        if (value)
        {
            if (IsAltitudeHold)
                Throttle = ThrottleHoldValue;
            _client.EnableControl();
            AddLog("Управление включено: пакеты 03 66 … 99 каждые 50 мс.");
        }
        else
        {
            CancelPreset();
            _lastPacket = null;
            _ = DisableControlAsync(_client);
        }
    }

    private async Task DisableControlAsync(DroneClient client)
    {
        await client.DisableControlAsync();
        AddLog("Управление выключено (отправлено 08 01).");
    }

    // ── Газ ────────────────────────────────────────────────────────────────

    partial void OnThrottleChanged(double value) => _client?.SetThrottle(ToByte(value));

    partial void OnIsAltitudeHoldChanged(bool value)
    {
        if (value)
            Throttle = ThrottleHoldValue;
    }

    [RelayCommand]
    private void ThrottleZero() => Throttle = 0;

    [RelayCommand]
    private void ThrottleHold() => Throttle = ThrottleHoldValue;

    // ── Экспериментальные флаги ────────────────────────────────────────────

    partial void OnIsHeadlessChanged(bool value) => _client?.SetFlag(DroneFlags.Headless, value);

    partial void OnIsReturnOrUnlockChanged(bool value) => _client?.SetFlag(DroneFlags.ReturnOrUnlock, value);

    partial void OnIsLightChanged(bool value) => _client?.SetFlag(DroneFlags.Light, value);

    // ── Пресеты ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void TakeOff()
    {
        if (IsAltitudeHold)
            Throttle = ThrottleHoldValue;
        _ = RunPresetAsync("Взлёт", FlightPresets.TakeOffAsync);
    }

    [RelayCommand]
    private void Land() => _ = RunPresetAsync("Посадка", FlightPresets.LandAsync);

    [RelayCommand]
    private void Hover()
    {
        if (IsAltitudeHold)
            Throttle = ThrottleHoldValue;
        _ = RunPresetAsync("Зависание", FlightPresets.HoverAsync);
    }

    [RelayCommand]
    private void Forward() => Move("Вперёд", roll: 0, pitch: Power, yaw: 0);

    [RelayCommand]
    private void Back() => Move("Назад", roll: 0, pitch: -Power, yaw: 0);

    [RelayCommand]
    private void Left() => Move("Влево", roll: -Power, pitch: 0, yaw: 0);

    [RelayCommand]
    private void Right() => Move("Вправо", roll: Power, pitch: 0, yaw: 0);

    [RelayCommand]
    private void YawLeft() => Move("Поворот влево", roll: 0, pitch: 0, yaw: -Power);

    [RelayCommand]
    private void YawRight() => Move("Поворот вправо", roll: 0, pitch: 0, yaw: Power);

    [RelayCommand]
    private void Flip() => _ = RunPresetAsync("Флип вперёд", (c, ct) => FlightPresets.FlipAsync(c, FlipDirection.Forward, ct));

    [RelayCommand]
    private void CalibrateGyro() => _ = RunPresetAsync("Калибровка гироскопа", FlightPresets.CalibrateGyroAsync);

    [RelayCommand]
    private void Demo()
    {
        double power = Power;
        _ = RunPresetAsync("Демо-сценарий", (c, ct) => FlightPresets.DemoAsync(c, power, ct));
    }

    private void Move(string name, double roll, double pitch, double yaw)
    {
        var duration = TimeSpan.FromSeconds(DurationSeconds);
        _ = RunPresetAsync(
            $"{name} {Power:0}% · {DurationSeconds:0.0} с",
            (c, ct) => FlightPresets.MoveAsync(c, roll, pitch, yaw, duration, ct));
    }

    private async Task RunPresetAsync(string name, Func<DroneClient, CancellationToken, Task> preset)
    {
        var client = _client;
        if (client is null || !IsControlEnabled)
        {
            AddLog("Сначала подключитесь и включите управление.");
            return;
        }

        CancelPreset();
        var cts = new CancellationTokenSource();
        _presetCts = cts;
        ActivePreset = name;
        AddLog($"▶ {name}");

        try
        {
            await preset(client, cts.Token);
            AddLog($"✓ {name}");
        }
        catch (OperationCanceledException)
        {
            AddLog($"■ {name}: прервано");
        }
        catch (Exception ex)
        {
            AddLog($"✗ {name}: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_presetCts, cts))
            {
                _presetCts = null;
                ActivePreset = null;
            }
            cts.Dispose();
        }
    }

    private void CancelPreset() => _presetCts?.Cancel();

    // ── Стоп ───────────────────────────────────────────────────────────────

    /// <summary>Прерывает выполнение команд: отмена пресета, снятие флагов, стики в центр.</summary>
    [RelayCommand]
    private void Stop()
    {
        CancelPreset();
        if (_client is null)
            return;

        _client.ClearPulses();
        _client.Hover();
        if (IsAltitudeHold)
            Throttle = ThrottleHoldValue;
        AddLog("STOP: команды прерваны, стики в центре (зависание).");
    }

    /// <summary>Аварийная остановка моторов (флаг 0x04). Дрон упадёт!</summary>
    [RelayCommand]
    private void EmergencyStop()
    {
        CancelPreset();
        if (_client is null || !IsControlEnabled)
        {
            AddLog("Аварийный стоп недоступен: управление не включено.");
            return;
        }

        _client.ClearPulses();
        _client.Hover();
        Throttle = 0;
        _client.PulseFlag(DroneFlags.EmergencyStop, DroneProtocol.FlagPulseDuration);
        AddLog("АВАРИЙНЫЙ СТОП МОТОРОВ (флаг 0x04).");
    }

    [RelayCommand]
    private void ClearLog() => LogLines.Clear();

    // ── Служебное ──────────────────────────────────────────────────────────

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        var packet = _lastPacket;
        if (packet is null || packet.Length != DroneProtocol.ControlPacketLength)
        {
            LastPacketHex = "—";
            FlagsText = "—";
            return;
        }

        // 03 66 RR PP TT YY FF CS 99
        RollValue = packet[2];
        PitchValue = packet[3];
        ThrottleValue = packet[4];
        YawValue = packet[5];
        var flags = (DroneFlags)packet[6];
        FlagsText = flags == DroneFlags.None ? "0x00" : $"0x{packet[6]:X2} · {flags}";

        string hex = ToHex(packet);
        LastPacketHex = hex;
        if (hex != _lastLoggedPacket)
        {
            _lastLoggedPacket = hex;
            AddLog($"→ {hex}");
        }
    }

    private void OnSendFailed(Exception ex)
    {
        // Ошибка повторяется на каждом пакете (20 раз в секунду), пишем её один раз.
        if (ex.Message == _lastSendError)
            return;
        _lastSendError = ex.Message;
        AddLog($"Ошибка отправки: {ex.Message}");
    }

    private void AddLog(string message)
    {
        LogLines.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {message}");
        while (LogLines.Count > MaxLogLines)
            LogLines.RemoveAt(LogLines.Count - 1);
    }

    private void Post(Action action) => _dispatcher.InvokeAsync(action);

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static string ToHex(byte[] data) => BitConverter.ToString(data).Replace('-', ' ');

    public async ValueTask DisposeAsync()
    {
        _uiTimer.Stop();
        CancelPreset();
        var client = _client;
        _client = null;
        if (client is not null)
            await client.DisposeAsync();
    }
}
