namespace Spice86.Core.Emulator.Devices.Cmos;

/// <summary>
/// CMOS/RTC register address constants for the MC146818 chip.
/// These registers are accessed via I/O ports 0x70 (index) and 0x71 (data).
/// </summary>
public static class CmosRegisterAddresses {
    /// <summary>
    /// Seconds register (0x00). Stores current seconds in BCD or binary format (0-59).
    /// </summary>
    public const byte Seconds = 0x00;

    /// <summary>
    /// Minutes register (0x02). Stores current minutes in BCD or binary format (0-59).
    /// </summary>
    public const byte Minutes = 0x02;

    /// <summary>
    /// Hours register (0x04). Stores current hours in BCD or binary format (0-23 or 1-12 with AM/PM).
    /// </summary>
    public const byte Hours = 0x04;

    /// <summary>
    /// Day of week register (0x06). Stores current day of week (1-7, where 1=Sunday).
    /// </summary>
    public const byte DayOfWeek = 0x06;

    /// <summary>
    /// Day of month register (0x07). Stores current day of month in BCD or binary format (1-31).
    /// </summary>
    public const byte DayOfMonth = 0x07;

    /// <summary>
    /// Month register (0x08). Stores current month in BCD or binary format (1-12).
    /// </summary>
    public const byte Month = 0x08;

    /// <summary>
    /// Year register (0x09). Stores the two-digit year (00-99) within the century specified by the Century register (0x32), in BCD or binary format.
    /// </summary>
    public const byte Year = 0x09;

    /// <summary>
    /// Status Register A (0x0A). Controls time base, rate selection, and update-in-progress flag.
    /// </summary>
    public const byte StatusRegisterA = 0x0A;

    /// <summary>
    /// Status Register B (0x0B). Controls data format (BCD/binary), 12/24 hour mode, and interrupt enables.
    /// </summary>
    public const byte StatusRegisterB = 0x0B;

    /// <summary>
    /// Status Register C (0x0C). Interrupt request flags (read-only). Reading this register acknowledges interrupts.
    /// </summary>
    public const byte StatusRegisterC = 0x0C;

    /// <summary>
    /// Status Register D (0x0D). Valid RAM and battery status (read-only).
    /// </summary>
    public const byte StatusRegisterD = 0x0D;

    /// <summary>
    /// Shutdown status register (0x0F). Used by BIOS for shutdown/restart operations.
    /// </summary>
    public const byte ShutdownStatus = 0x0F;

    /// <summary>
    /// Century register (0x32). Stores current century in BCD or binary format (19 or 20).
    /// </summary>
    public const byte Century = 0x32;
}

/// <summary>
/// CMOS I/O port addresses for accessing the MC146818 RTC chip.
/// </summary>
public static class CmosPorts {
    /// <summary>
    /// CMOS address/index port (0x70). Write-only port for selecting which CMOS register to access.
    /// Bit 7 controls NMI: 0=enable NMI, 1=disable NMI.
    /// </summary>
    public const ushort Address = 0x70;

    /// <summary>
    /// CMOS data port (0x71). Read/write port for accessing the data in the selected CMOS register.
    /// </summary>
    public const ushort Data = 0x71;
}