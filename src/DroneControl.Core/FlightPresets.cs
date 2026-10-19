namespace DroneControl.Core;

public enum FlipDirection
{
    Forward,
    Back,
    Left,
    Right,
}

/// <summary>
/// Готовые сценарии команд. Каждый можно прервать через CancellationToken.
/// Команды движения при любом завершении (в том числе отмене) возвращают дрон в зависание.
/// </summary>
public static class FlightPresets
{
    public static async Task TakeOffAsync(DroneClient client, CancellationToken cancellationToken)
    {
        client.Hover();
        client.PulseFlag(DroneFlags.TakeOff, DroneProtocol.FlagPulseDuration);
        await Task.Delay(DroneProtocol.FlagPulseDuration, cancellationToken).ConfigureAwait(false);
    }

    public static async Task LandAsync(DroneClient client, CancellationToken cancellationToken)
    {
        client.Hover();
        client.PulseFlag(DroneFlags.Land, DroneProtocol.FlagPulseDuration);
        await Task.Delay(DroneProtocol.FlagPulseDuration, cancellationToken).ConfigureAwait(false);
    }

    public static Task HoverAsync(DroneClient client, CancellationToken cancellationToken)
    {
        client.Hover();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Отклоняет стики на заданное время. Значения в процентах −100…+100:
    /// крен «+» вправо, тангаж «+» вперёд, рыскание «+» поворот вправо.
    /// </summary>
    public static async Task MoveAsync(
        DroneClient client,
        double rollPercent,
        double pitchPercent,
        double yawPercent,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        client.SetSticks(
            DroneProtocol.AxisFromPercent(rollPercent),
            DroneProtocol.AxisFromPercent(pitchPercent),
            DroneProtocol.AxisFromPercent(yawPercent));
        try
        {
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            client.Hover();
        }
    }

    /// <summary>Флип: стик в сторону переворота + бит Flip на 600 мс, затем центр (как setCircleTurnEnd).</summary>
    public static async Task FlipAsync(DroneClient client, FlipDirection direction, CancellationToken cancellationToken)
    {
        var (roll, pitch) = direction switch
        {
            FlipDirection.Forward => (0.0, 100.0),
            FlipDirection.Back => (0.0, -100.0),
            FlipDirection.Left => (-100.0, 0.0),
            FlipDirection.Right => (100.0, 0.0),
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };

        client.SetSticks(DroneProtocol.AxisFromPercent(roll), DroneProtocol.AxisFromPercent(pitch), ControlState.Center);
        client.PulseFlag(DroneFlags.Flip, DroneProtocol.FlipPulseDuration);
        try
        {
            await Task.Delay(DroneProtocol.FlipPulseDuration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            client.Hover();
        }
    }

    public static async Task CalibrateGyroAsync(DroneClient client, CancellationToken cancellationToken)
    {
        client.PulseFlag(DroneFlags.GyroCalibration, DroneProtocol.GyroCalibrationDuration);
        await Task.Delay(DroneProtocol.GyroCalibrationDuration, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Демо: взлёт → зависание → вперёд → поворот → назад → посадка.</summary>
    public static async Task DemoAsync(DroneClient client, double powerPercent, CancellationToken cancellationToken)
    {
        var step = TimeSpan.FromSeconds(1);

        await TakeOffAsync(client, cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);

        await MoveAsync(client, 0, powerPercent, 0, step, cancellationToken).ConfigureAwait(false);
        await Task.Delay(step, cancellationToken).ConfigureAwait(false);

        await MoveAsync(client, 0, 0, powerPercent, step, cancellationToken).ConfigureAwait(false);
        await Task.Delay(step, cancellationToken).ConfigureAwait(false);

        await MoveAsync(client, 0, -powerPercent, 0, step, cancellationToken).ConfigureAwait(false);
        await Task.Delay(step, cancellationToken).ConfigureAwait(false);

        await LandAsync(client, cancellationToken).ConfigureAwait(false);
    }
}
