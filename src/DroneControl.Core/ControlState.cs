namespace DroneControl.Core;

/// <summary>
/// Положение «стиков» и флагов, которое уходит в дрон каждые 50 мс.
/// Оси крена, тангажа и рыскания: 1…255, центр 128. Газ: 0…255.
/// </summary>
public readonly record struct ControlState(byte Roll, byte Pitch, byte Throttle, byte Yaw, DroneFlags Flags)
{
    public const byte Center = 128;

    public static ControlState Neutral => new(Center, Center, 0, Center, DroneFlags.None);
}
