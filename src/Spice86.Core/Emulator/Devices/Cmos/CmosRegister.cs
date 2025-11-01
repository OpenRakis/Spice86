namespace Spice86.Core.Emulator.Devices.Cmos;

/// <summary>
/// CMOS/RTC register addresses for the MC146818 Real Time Clock chip.
/// </summary>
public enum CmosRegister : byte {
    /// <summary>
    /// Seconds (0-59, BCD or binary depending on Register B bit 2)
    /// </summary>
    Seconds = 0x00,

    /// <summary>
    /// Seconds alarm (0-59, BCD or binary)
    /// </summary>
    SecondsAlarm = 0x01,

    /// <summary>
    /// Minutes (0-59, BCD or binary)
    /// </summary>
    Minutes = 0x02,

    /// <summary>
    /// Minutes alarm (0-59, BCD or binary)
    /// </summary>
    MinutesAlarm = 0x03,

    /// <summary>
    /// Hours (0-23 or 1-12 depending on Register B bit 1, BCD or binary)
    /// </summary>
    Hours = 0x04,

    /// <summary>
    /// Hours alarm (0-23 or 1-12, BCD or binary)
    /// </summary>
    HoursAlarm = 0x05,

    /// <summary>
    /// Day of week (1-7 where 1=Sunday)
    /// </summary>
    DayOfWeek = 0x06,

    /// <summary>
    /// Day of month (1-31, BCD or binary)
    /// </summary>
    DayOfMonth = 0x07,

    /// <summary>
    /// Month (1-12, BCD or binary)
    /// </summary>
    Month = 0x08,

    /// <summary>
    /// Year (0-99, BCD or binary)
    /// </summary>
    Year = 0x09,

    /// <summary>
    /// Register A - Rate selection and divider control
    /// </summary>
    RegisterA = 0x0A,

    /// <summary>
    /// Register B - Control register (24h mode, DSE, PIE, etc.)
    /// </summary>
    RegisterB = 0x0B,

    /// <summary>
    /// Register C - Status register (interrupt flags, read-only)
    /// </summary>
    RegisterC = 0x0C,

    /// <summary>
    /// Register D - Status register (VRT bit, read-only)
    /// </summary>
    RegisterD = 0x0D,

    /// <summary>
    /// Status/Shutdown register
    /// </summary>
    StatusShutdown = 0x0F,

    /// <summary>
    /// Extended RAM low byte (640KB base memory)
    /// </summary>
    ExtendedRamLow = 0x15,

    /// <summary>
    /// Extended RAM high byte (640KB base memory)
    /// </summary>
    ExtendedRamHigh = 0x16,

    /// <summary>
    /// Century (19-21, BCD or binary)
    /// </summary>
    Century = 0x32
}

/// <summary>
/// Register B control bits for the MC146818 RTC.
/// </summary>
[Flags]
public enum RegisterBFlags : byte {
    /// <summary>
    /// No flags set
    /// </summary>
    None = 0x00,

    /// <summary>
    /// Daylight Savings Enable (bit 0)
    /// </summary>
    DaylightSavingsEnable = 0x01,

    /// <summary>
    /// 24-hour mode when set, 12-hour mode when clear (bit 1)
    /// </summary>
    Hour24Mode = 0x02,

    /// <summary>
    /// Binary mode when set, BCD mode when clear (bit 2)
    /// </summary>
    DataModeBinary = 0x04,

    /// <summary>
    /// Square wave enable (bit 3)
    /// </summary>
    SquareWaveEnable = 0x08,

    /// <summary>
    /// Update-ended interrupt enable (bit 4)
    /// </summary>
    UpdateEndedInterruptEnable = 0x10,

    /// <summary>
    /// Alarm interrupt enable (bit 5)
    /// </summary>
    AlarmInterruptEnable = 0x20,

    /// <summary>
    /// Periodic interrupt enable (bit 6)
    /// </summary>
    PeriodicInterruptEnable = 0x40,

    /// <summary>
    /// SET bit - when set, inhibits updates (bit 7)
    /// </summary>
    SetInhibitUpdate = 0x80
}

/// <summary>
/// Register C status flags for the MC146818 RTC (read-only).
/// </summary>
[Flags]
public enum RegisterCFlags : byte {
    /// <summary>
    /// No flags set
    /// </summary>
    None = 0x00,

    /// <summary>
    /// Update-ended interrupt flag (bit 4)
    /// </summary>
    UpdateEndedInterrupt = 0x10,

    /// <summary>
    /// Alarm interrupt flag (bit 5)
    /// </summary>
    AlarmInterrupt = 0x20,

    /// <summary>
    /// Periodic interrupt flag (bit 6)
    /// </summary>
    PeriodicInterrupt = 0x40,

    /// <summary>
    /// Interrupt request flag - set when any enabled interrupt occurs (bit 7)
    /// </summary>
    InterruptRequest = 0x80
}

/// <summary>
/// Register D status flags for the MC146818 RTC (read-only).
/// </summary>
[Flags]
public enum RegisterDFlags : byte {
    /// <summary>
    /// No flags set
    /// </summary>
    None = 0x00,

    /// <summary>
    /// Valid RAM and Time (VRT) bit - set when RTC has valid data (bit 7)
    /// </summary>
    ValidRamAndTime = 0x80
}
