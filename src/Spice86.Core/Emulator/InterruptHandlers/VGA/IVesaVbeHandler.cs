namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

/// <summary>
/// Interface for VESA VBE (VESA BIOS Extensions) 1.0 functionality.
/// VESA VBE provides standardized access to extended video modes beyond standard VGA.
/// Functions are called via INT 10h with AH=4Fh.
/// </summary>
public interface IVesaVbeHandler {
    /// <summary>
    /// Returns VBE controller information (Function 00h).
    /// This function is typically called first by programs to detect VBE presence.
    /// Input: ES:DI points to a 256-byte buffer.
    /// Output: Buffer filled with VbeInfoBlock structure containing:
    /// - VBE signature ("VESA")
    /// - Version number  
    /// - OEM string pointer
    /// - Capabilities
    /// - Supported video mode list pointer
    /// - Total memory in 64KB blocks
    /// </summary>
    void ReturnControllerInfo();

    /// <summary>
    /// Returns information about a specific VBE mode (Function 01h).
    /// Input: CX contains the mode number, ES:DI points to a 256-byte buffer.
    /// Output: Buffer filled with ModeInfoBlock structure containing:
    /// - Mode attributes (supported, color, graphics, etc.)
    /// - Window attributes and positioning
    /// - Resolution (width x height)
    /// - Bits per pixel and memory model
    /// - Color mask information for direct color modes
    /// </summary>
    void ReturnModeInfo();

    /// <summary>
    /// Sets the current VBE video mode (Function 02h).
    /// Input: BX contains the mode number.
    /// Bit 15 of BX: 0 = clear display memory, 1 = don't clear display memory.
    /// Switches the display to the specified VBE mode.
    /// </summary>
    void SetVbeMode();

    /// <summary>
    /// Returns the current VBE mode number (Function 03h).
    /// Output: BX contains the current VBE mode number.
    /// </summary>
    void ReturnCurrentVbeMode();

    /// <summary>
    /// Saves or restores VBE state (Function 04h).
    /// Input: DL specifies the subfunction:
    /// - 00h: Return save/restore state buffer size
    /// - 01h: Save state to buffer at ES:BX
    /// - 02h: Restore state from buffer at ES:BX
    /// CX contains requested states bitmask:
    /// - Bit 0: Video hardware state
    /// - Bit 1: Video BIOS data state
    /// - Bit 2: Video DAC state
    /// - Bit 3: Super VGA state
    /// Output (subfunction 00h): BX contains number of 64-byte blocks required.
    /// </summary>
    void SaveRestoreState();

    /// <summary>
    /// Display Window Control (Function 05h).
    /// Controls the CPU window into video memory for banked modes.
    /// Input: BH specifies subfunction:
    /// - 00h: Set memory window position
    /// - 01h: Get memory window position
    /// BL specifies window number (00h=Window A, 01h=Window B)
    /// DX specifies window position in window granularity units (for set)
    /// Output: DX contains window position in granularity units (for get).
    /// </summary>
    void DisplayWindowControl();
}
