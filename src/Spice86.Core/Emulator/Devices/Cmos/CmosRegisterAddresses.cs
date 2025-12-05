namespace Spice86.Core.Emulator.Devices.Cmos;

/// <summary>
/// CMOS/RTC register address constants for the MC146818 chip.
/// These registers are accessed via I/O ports 0x70 (index) and 0x71 (data).
/// </summary>
public static class CmosRegisterAddresses {
    /// <summary>
    /// Status Register B (0x0B). Controls data format (BCD/binary), 12/24 hour mode, and interrupt enables.
    /// Bit 6: Periodic Interrupt Enable (PIE) - When set, enables periodic interrupts on IRQ 8 (INT 70h).
    /// </summary>
    public const byte StatusRegisterB = 0x0B;
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
