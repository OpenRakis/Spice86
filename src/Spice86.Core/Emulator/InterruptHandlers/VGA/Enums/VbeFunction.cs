namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
/// VESA VBE function codes called via INT 10h with AH=4Fh.
/// </summary>
public enum VbeFunction : byte {
    /// <summary>
    /// Return VBE Controller Information.
    /// Input: AX=4F00h, ES:DI=Pointer to buffer
    /// Output: AX=004Fh if supported, ES:DI buffer filled with VbeInfoBlock
    /// </summary>
    ReturnControllerInfo = 0x00,

    /// <summary>
    /// Return VBE Mode Information.
    /// Input: AX=4F01h, CX=Mode number, ES:DI=Pointer to buffer
    /// Output: AX=004Fh if supported, ES:DI buffer filled with ModeInfoBlock
    /// </summary>
    ReturnModeInfo = 0x01,

    /// <summary>
    /// Set VBE Mode.
    /// Input: AX=4F02h, BX=Mode number (bit 15: 0=clear memory, 1=don't clear)
    /// Output: AX=004Fh if supported
    /// </summary>
    SetVbeMode = 0x02,

    /// <summary>
    /// Return Current VBE Mode.
    /// Input: AX=4F03h
    /// Output: AX=004Fh if supported, BX=Current VBE mode number
    /// </summary>
    ReturnCurrentVbeMode = 0x03,

    /// <summary>
    /// Save/Restore State.
    /// Input: AX=4F04h, DL=Subfunction (00h=return buffer size, 01h=save, 02h=restore)
    ///        CX=Requested states, ES:BX=Buffer pointer (for save/restore)
    /// Output: AX=004Fh if supported, BX=Number of 64-byte blocks (for subfunction 00h)
    /// </summary>
    SaveRestoreState = 0x04
}
