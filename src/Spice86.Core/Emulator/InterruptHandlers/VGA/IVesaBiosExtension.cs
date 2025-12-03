namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

/// <summary>
/// "The standard provides a set of functions which an application program can use
/// to A) obtain information about the capabilities and characteristics of a
/// specific Super VGA implementation and B) to control the operation of such
/// hardware in terms of video mode initialization and video memory access.
/// The functions are provided as an extension to the VGA BIOS video services, accessed
/// through interrupt 10h."
/// VESA Super VGA BIOS Extension Standard #VS911022, October 22, 1991, VBE Version 1.2
/// </summary>
public interface IVesaBiosExtension {
    /// <summary>
    /// "Function 00h - Return Super VGA Information.
    /// The purpose of this function is to provide information to the calling program
    /// about the general capabilities of the Super VGA environment. The function fills
    /// an information block structure at the address specified by the caller.
    /// The information block size is 256 bytes."
    /// Input: AH = 4Fh Super VGA support, AL = 00h Return Super VGA information, ES:DI = Pointer to buffer.
    /// Output: AX = Status (All other registers are preserved).
    /// </summary>
    void VbeGetControllerInfo();

    /// <summary>
    /// "Function 01h - Return Super VGA mode information.
    /// This function returns information about a specific Super VGA video mode that was
    /// returned by Function 0. The function fills a mode information block structure
    /// at the address specified by the caller. The mode information block size is
    /// maximum 256 bytes."
    /// Input: AH = 4Fh Super VGA support, AL = 01h Return Super VGA mode information,
    /// CX = Super VGA video mode (mode number must be one of those returned by Function 0),
    /// ES:DI = Pointer to 256 byte buffer.
    /// Output: AX = Status (All other registers are preserved).
    /// </summary>
    void VbeGetModeInfo();

    /// <summary>
    /// "Function 02h - Set Super VGA video mode.
    /// This function initializes a video mode. The BX register contains the mode to
    /// set. The format of VESA mode numbers is described in chapter 2. If the mode
    /// cannot be set, the BIOS should leave the video environment unchanged and return
    /// a failure error code."
    /// Input: AH = 4Fh Super VGA support, AL = 02h Set Super VGA video mode,
    /// BX = Video mode (D0-D14 = Video mode, D15 = Clear memory flag:
    /// 0 = Clear video memory, 1 = Don't clear video memory).
    /// Output: AX = Status (All other registers are preserved).
    /// </summary>
    void VbeSetMode();
}
