namespace Spice86.ViewModels;

/// <summary>
/// Static catalog of well-known interrupt vectors and I/O port ranges used to populate
/// the autocomplete suggestions in the breakpoint creation dialog. This is purely
/// reference data and does not depend on the running emulator state.
/// </summary>
public static class KnownBreakpointSuggestions {
    /// <summary>
    /// List of well-known interrupt vectors with a short human-readable description,
    /// ordered by vector number.
    /// </summary>
    public static IReadOnlyList<(byte Vector, string Description)> Interrupts { get; } =
    [
        (0x08, "Timer Interrupt (IRQ 0 / PIT tick)"),
        (0x09, "Keyboard Interrupt (IRQ 1)"),
        (0x10, "BIOS Video Services (AH=function)"),
        (0x11, "BIOS Equipment List"),
        (0x12, "BIOS Conventional Memory Size"),
        (0x13, "BIOS Disk Services (AH=function)"),
        (0x15, "BIOS Miscellaneous Services / PS2 Pointing Device (AH=function)"),
        (0x16, "BIOS Keyboard Services (AH=function)"),
        (0x1A, "BIOS Time / Date Services (AH=function)"),
        (0x1C, "BIOS Timer Tick User Handler"),
        (0x20, "DOS Terminate Program"),
        (0x21, "DOS Functions (AH=function)"),
        (0x22, "DOS Terminate Address (control-flow target)"),
        (0x23, "DOS Ctrl-Break Handler"),
        (0x24, "DOS Critical Error Handler"),
        (0x25, "DOS Absolute Disk Read"),
        (0x26, "DOS Absolute Disk Write"),
        (0x28, "DOS Idle (background processing hook)"),
        (0x2A, "DOS Network / Critical Section"),
        (0x2F, "DOS Multiplex (incl. XMS driver address at AX=4310h)"),
        (0x33, "Mouse Driver Functions (AX=function)"),
        (0x67, "EMS Expanded Memory Services (AH=function)"),
        (0x70, "BIOS Real-Time Clock Interrupt (IRQ 8)"),
        (0x74, "BIOS PS/2 Mouse Interrupt (IRQ 12)")
    ];

    /// <summary>
    /// List of well-known I/O port ranges with a short human-readable description,
    /// ordered by first port address.
    /// </summary>
    public static IReadOnlyList<(ushort FirstPort, ushort LastPort, string Description)> IoPorts { get; } =
    [
        (0x0020, 0x0021, "PIC1 - Master Programmable Interrupt Controller"),
        (0x0040, 0x0043, "PIT 8254 - Programmable Interval Timer"),
        (0x0060, 0x0060, "PS/2 Keyboard Data"),
        (0x0064, 0x0064, "PS/2 Keyboard / Mouse Controller Command & Status"),
        (0x00A0, 0x00A1, "PIC2 - Slave Programmable Interrupt Controller"),
        (0x0200, 0x0207, "Joystick Gameport (PC/AT standard)"),
        (0x0210, 0x021F, "Sound Blaster (base 0x210)"),
        (0x0220, 0x022F, "Sound Blaster (base 0x220 - most common)"),
        (0x0230, 0x023F, "Sound Blaster (base 0x230)"),
        (0x0240, 0x024F, "Sound Blaster (base 0x240) / Gravis Ultrasound"),
        (0x0250, 0x025F, "Sound Blaster (base 0x250)"),
        (0x0260, 0x026F, "Sound Blaster (base 0x260)"),
        (0x0280, 0x028F, "Sound Blaster (base 0x280)"),
        (0x0330, 0x0331, "MPU-401 MIDI Interface (General MIDI / MT-32)"),
        (0x0388, 0x038B, "OPL FM Synthesizer (AdLib / OPL2 / OPL3)"),
        (0x03B0, 0x03BF, "VGA Monochrome / MDA-compatible Registers"),
        (0x03C0, 0x03CF, "VGA Color / EGA-compatible Registers"),
        (0x03D0, 0x03DF, "VGA Color Additional / CGA-compatible Registers")
    ];
}
