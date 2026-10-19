namespace DroneControl.Core;

/// <summary>
/// Биты байта флагов в пакете управления (байт 5 после заголовка 0x66).
/// Восстановлено из FlyController.FlyControlTask оригинального приложения.
/// </summary>
[Flags]
public enum DroneFlags : byte
{
    None = 0,

    /// <summary>Взлёт одной кнопкой. Приложение держит бит 1 секунду.</summary>
    TakeOff = 0x01,

    /// <summary>Посадка одной кнопкой. Приложение держит бит 1 секунду.</summary>
    Land = 0x02,

    /// <summary>Аварийная остановка моторов. Приложение держит бит 1 секунду.</summary>
    EmergencyStop = 0x04,

    /// <summary>Флип (переворот) в сторону, куда отклонены крен/тангаж. Держится 600 мс.</summary>
    Flip = 0x08,

    /// <summary>Экспериментально: предположительно headless-режим.</summary>
    Headless = 0x10,

    /// <summary>Экспериментально: возврат / разблокировка моторов.</summary>
    ReturnOrUnlock = 0x20,

    /// <summary>Экспериментально: предположительно подсветка.</summary>
    Light = 0x40,

    /// <summary>Калибровка гироскопа. Приложение держит бит 2 секунды.</summary>
    GyroCalibration = 0x80,
}
