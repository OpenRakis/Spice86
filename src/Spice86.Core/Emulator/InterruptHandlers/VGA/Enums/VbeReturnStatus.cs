namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

/// <summary>
/// VESA VBE function return status codes.
/// These codes are returned in the AH register after a VBE function call.
/// AL should contain 0x4F to indicate VBE support.
/// </summary>
public enum VbeReturnStatus : byte {
    /// <summary>
    /// Function call successful.
    /// </summary>
    Success = 0x00,

    /// <summary>
    /// Function call failed.
    /// </summary>
    Failed = 0x01,

    /// <summary>
    /// Function is not supported in current hardware configuration.
    /// </summary>
    NotSupportedInCurrentHardware = 0x02,

    /// <summary>
    /// Function is invalid in current video mode.
    /// </summary>
    InvalidInCurrentVideoMode = 0x03
}
