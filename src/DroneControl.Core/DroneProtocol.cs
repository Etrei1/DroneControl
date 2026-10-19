namespace DroneControl.Core;

/// <summary>
/// UDP-протокол управления TX-дронами (приложение KY FPV, DeviceTXFragment + FlyController).
///
///   Heartbeat:       01 01                                  раз в 1 с
///   Управление:      03 66 RR PP TT YY FF CS 99             каждые 50 мс
///                    RR — крен, PP — тангаж, TT — газ, YY — рыскание,
///                    FF — флаги, CS = RR ^ PP ^ TT ^ YY ^ FF
///   Выход из режима: 08 01                                  однократно
/// </summary>
public static class DroneProtocol
{
    public const string DefaultHost = "192.168.1.1";
    public const int DefaultPort = 7099;

    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan ControlInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Сколько держит флаг взлёта/посадки/стопа оригинальное приложение.</summary>
    public static readonly TimeSpan FlagPulseDuration = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan FlipPulseDuration = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan GyroCalibrationDuration = TimeSpan.FromSeconds(2);

    public const int ControlPacketLength = 9;

    private const byte ControlPrefix = 0x03;
    private const byte Header = 0x66;
    private const byte Footer = 0x99;

    public static byte[] Heartbeat() => [0x01, 0x01];

    public static byte[] ExitControl() => [0x08, 0x01];

    public static byte[] BuildControlPacket(ControlState state)
    {
        byte roll = ClampAxis(state.Roll);
        byte pitch = ClampAxis(state.Pitch);
        byte yaw = ClampAxis(state.Yaw);
        // Оригинал превращает газ 1 в 0, остальные значения передаёт как есть.
        byte throttle = state.Throttle == 1 ? (byte)0 : state.Throttle;
        byte flags = (byte)state.Flags;

        return
        [
            ControlPrefix,
            Header,
            roll,
            pitch,
            throttle,
            yaw,
            flags,
            Checksum(roll, pitch, throttle, yaw, flags),
            Footer,
        ];
    }

    public static byte Checksum(byte roll, byte pitch, byte throttle, byte yaw, byte flags) =>
        (byte)(roll ^ pitch ^ throttle ^ yaw ^ flags);

    /// <summary>Переводит отклонение оси −100…+100 % в байт 1…255 (0 % → 128).</summary>
    public static byte AxisFromPercent(double percent)
    {
        percent = Math.Clamp(percent, -100, 100);
        double value = Math.Round(ControlState.Center + percent * 127 / 100.0, MidpointRounding.AwayFromZero);
        return (byte)Math.Clamp((int)value, 1, 255);
    }

    private static byte ClampAxis(byte value) => value < 1 ? (byte)1 : value;
}
