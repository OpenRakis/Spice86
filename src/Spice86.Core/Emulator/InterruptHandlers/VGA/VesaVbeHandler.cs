namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Implementation of VESA VBE (VESA BIOS Extensions) 1.0 functionality.
/// VESA VBE provides standardized access to extended video modes beyond standard VGA.
/// </summary>
public class VesaVbeHandler : IVesaVbeHandler {
    private const uint VbeSignature = 0x41534556; // "VESA" in little-endian
    private const ushort VbeVersion10 = 0x0100; // VBE 1.0 in BCD format
    private const string OemString = "Spice86 VBE 1.0";
    
    // VBE constants for memory layout
    private const uint OemStringAddress = 0xC0000; // Store OEM string in video BIOS area
    private const uint ModeListAddress = 0xC0020; // Store mode list after OEM string
    
    private readonly State _state;
    private readonly IIndexable _memory;
    private readonly ILoggerService _loggerService;
    private readonly IVgaFunctionality _vgaFunctionality;
    
    private ushort _currentVbeMode;
    
    /// <summary>
    /// Supported VBE modes for VBE 1.0.
    /// These are standard VGA modes that we report as VBE-compatible.
    /// </summary>
    private static readonly ushort[] SupportedVbeModes = new ushort[] {
        0x100, // 640x400 256-color
        0x101, // 640x480 256-color
        0x102, // 800x600 16-color
        0x103, // 800x600 256-color
        0x104, // 1024x768 16-color
        0x105, // 1024x768 256-color
        0x106, // 1280x1024 16-color
        0x107  // 1280x1024 256-color
    };

    /// <summary>
    /// Initializes a new instance of the VesaVbeHandler class.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="loggerService">The logger service.</param>
    /// <param name="vgaFunctionality">The VGA functionality provider.</param>
    public VesaVbeHandler(State state, IIndexable memory, ILoggerService loggerService, IVgaFunctionality vgaFunctionality) {
        _state = state;
        _memory = memory;
        _loggerService = loggerService;
        _vgaFunctionality = vgaFunctionality;
        _currentVbeMode = 0xFFFF; // No VBE mode set initially
    }

    /// <inheritdoc />
    public void ReturnControllerInfo() {
        // Arrange
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI);
        
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("VBE Function 00h: ReturnControllerInfo at {Segment:X4}:{Offset:X4}",
                _state.ES, _state.DI);
        }

        // Act
        VbeInfoBlock infoBlock = new VbeInfoBlock(_memory, bufferAddress);
        infoBlock.VbeSignature = VbeSignature;
        infoBlock.VbeVersion = VbeVersion10;
        infoBlock.Capabilities = VbeCapabilities.Dac8BitCapable;
        infoBlock.TotalMemory = 4; // 4 * 64KB = 256KB
        
        // Set OEM string
        infoBlock.SetOemString(OemString, OemStringAddress);
        
        // Set supported mode list
        infoBlock.SetVideoModeList(SupportedVbeModes, ModeListAddress);

        // Assert - Return success
        SetVbeReturnValue(VbeReturnStatus.Success);
    }

    /// <inheritdoc />
    public void ReturnModeInfo() {
        // Arrange
        ushort modeNumber = _state.CX;
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DI);

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("VBE Function 01h: ReturnModeInfo for mode 0x{Mode:X4} at {Segment:X4}:{Offset:X4}",
                modeNumber, _state.ES, _state.DI);
        }

        // Check if mode is supported
        if (!IsModeSupported(modeNumber)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("VBE mode 0x{Mode:X4} is not supported", modeNumber);
            }
            SetVbeReturnValue(VbeReturnStatus.Failed);
            return;
        }

        // Act
        ModeInfoBlock modeInfo = new ModeInfoBlock(_memory, bufferAddress);
        FillModeInfo(modeInfo, modeNumber);

        // Assert - Return success
        SetVbeReturnValue(VbeReturnStatus.Success);
    }

    /// <inheritdoc />
    public void SetVbeMode() {
        // Arrange
        ushort modeNumber = (ushort)(_state.BX & 0x7FFF); // Mask off bit 15
        bool dontClearMemory = (_state.BX & 0x8000) != 0;

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("VBE Function 02h: SetVbeMode to 0x{Mode:X4}, clear memory: {ClearMemory}",
                modeNumber, !dontClearMemory);
        }

        // Check if mode is supported
        if (!IsModeSupported(modeNumber)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("VBE mode 0x{Mode:X4} is not supported", modeNumber);
            }
            SetVbeReturnValue(VbeReturnStatus.Failed);
            return;
        }

        // Act - Set the mode
        _currentVbeMode = modeNumber;
        
        // Map VBE mode to VGA mode if possible and call VGA mode switch
        int vgaModeId = MapVbeModeToVgaMode(modeNumber);
        if (vgaModeId >= 0) {
            ModeFlags flags = dontClearMemory ? ModeFlags.NoClearMem : ModeFlags.Legacy;
            _vgaFunctionality.VgaSetMode(vgaModeId, flags);
        } else {
            // For modes that don't map to VGA, we just record the mode number
            // but don't actually switch the video mode (this is a simplified implementation)
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("VBE mode 0x{Mode:X4} has no VGA mapping, mode set in name only", modeNumber);
            }
        }

        // Assert - Return success
        SetVbeReturnValue(VbeReturnStatus.Success);
    }

    /// <inheritdoc />
    public void ReturnCurrentVbeMode() {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("VBE Function 03h: ReturnCurrentVbeMode = 0x{Mode:X4}", _currentVbeMode);
        }

        // Arrange/Act
        _state.BX = _currentVbeMode;

        // Assert - Return success
        SetVbeReturnValue(VbeReturnStatus.Success);
    }

    /// <inheritdoc />
    public void SaveRestoreState() {
        // Arrange
        byte subfunction = _state.DL;
        ushort requestedStates = _state.CX;

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("VBE Function 04h: SaveRestoreState, subfunction {Subfunction:X2}, states {States:X4}",
                subfunction, requestedStates);
        }

        // Act
        switch (subfunction) {
            case 0x00:
                // Return buffer size needed - estimate 1 block (64 bytes) per state bit
                int stateCount = CountSetBits(requestedStates);
                _state.BX = (ushort)stateCount;
                SetVbeReturnValue(VbeReturnStatus.Success);
                break;

            case 0x01:
                // Save state - not fully implemented yet
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("VBE save state not fully implemented");
                }
                SetVbeReturnValue(VbeReturnStatus.Success);
                break;

            case 0x02:
                // Restore state - not fully implemented yet
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("VBE restore state not fully implemented");
                }
                SetVbeReturnValue(VbeReturnStatus.Success);
                break;

            default:
                // Assert - Invalid subfunction
                SetVbeReturnValue(VbeReturnStatus.Failed);
                break;
        }
    }

    /// <summary>
    /// Sets the VBE return value in AX register.
    /// AL = 4Fh indicates VBE support, AH contains the status code.
    /// </summary>
    /// <param name="status">The VBE return status.</param>
    private void SetVbeReturnValue(VbeReturnStatus status) {
        _state.AL = 0x4F; // VBE supported
        _state.AH = (byte)status;
    }

    /// <summary>
    /// Checks if a VBE mode is supported.
    /// </summary>
    /// <param name="modeNumber">The VBE mode number to check.</param>
    /// <returns>True if the mode is supported, false otherwise.</returns>
    private bool IsModeSupported(ushort modeNumber) {
        foreach (ushort mode in SupportedVbeModes) {
            if (mode == modeNumber) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Fills the ModeInfoBlock structure with information about a specific mode.
    /// </summary>
    /// <param name="modeInfo">The ModeInfoBlock to fill.</param>
    /// <param name="modeNumber">The VBE mode number.</param>
    private void FillModeInfo(ModeInfoBlock modeInfo, ushort modeNumber) {
        // Set common attributes
        modeInfo.ModeAttributes = VbeModeAttribute.ModeSupported |
                                   VbeModeAttribute.TtyOutputSupported |
                                   VbeModeAttribute.ColorMode |
                                   VbeModeAttribute.GraphicsMode;

        modeInfo.WindowAAttributes = VbeWindowAttribute.WindowExists |
                                      VbeWindowAttribute.WindowReadable |
                                      VbeWindowAttribute.WindowWritable;
        modeInfo.WindowBAttributes = 0; // No window B
        modeInfo.WindowGranularity = 64; // 64KB granularity
        modeInfo.WindowSize = 64; // 64KB window size
        modeInfo.WindowASegment = 0xA000; // Standard VGA segment
        modeInfo.WindowBSegment = 0;
        modeInfo.WindowFunctionPtr = 0; // No window function in our implementation
        modeInfo.NumberOfPlanes = 1;
        modeInfo.NumberOfBanks = 1;
        modeInfo.BankSize = 0;
        modeInfo.Reserved1 = 1; // Always 1 per spec

        // Set mode-specific parameters
        switch (modeNumber) {
            case 0x100: // 640x400x256
                modeInfo.Width = 640;
                modeInfo.Height = 400;
                modeInfo.BitsPerPixel = 8;
                modeInfo.MemoryModel = VbeMemoryModel.PackedPixel;
                modeInfo.BytesPerScanLine = 640;
                modeInfo.NumberOfImagePages = 1;
                break;

            case 0x101: // 640x480x256
                modeInfo.Width = 640;
                modeInfo.Height = 480;
                modeInfo.BitsPerPixel = 8;
                modeInfo.MemoryModel = VbeMemoryModel.PackedPixel;
                modeInfo.BytesPerScanLine = 640;
                modeInfo.NumberOfImagePages = 1;
                break;

            case 0x102: // 800x600x16
                modeInfo.Width = 800;
                modeInfo.Height = 600;
                modeInfo.BitsPerPixel = 4;
                modeInfo.MemoryModel = VbeMemoryModel.Planar;
                modeInfo.BytesPerScanLine = 100;
                modeInfo.NumberOfPlanes = 4;
                modeInfo.NumberOfImagePages = 1;
                break;

            case 0x103: // 800x600x256
                modeInfo.Width = 800;
                modeInfo.Height = 600;
                modeInfo.BitsPerPixel = 8;
                modeInfo.MemoryModel = VbeMemoryModel.PackedPixel;
                modeInfo.BytesPerScanLine = 800;
                modeInfo.NumberOfImagePages = 1;
                break;

            case 0x104: // 1024x768x16
                modeInfo.Width = 1024;
                modeInfo.Height = 768;
                modeInfo.BitsPerPixel = 4;
                modeInfo.MemoryModel = VbeMemoryModel.Planar;
                modeInfo.BytesPerScanLine = 128;
                modeInfo.NumberOfPlanes = 4;
                modeInfo.NumberOfImagePages = 1;
                break;

            case 0x105: // 1024x768x256
                modeInfo.Width = 1024;
                modeInfo.Height = 768;
                modeInfo.BitsPerPixel = 8;
                modeInfo.MemoryModel = VbeMemoryModel.PackedPixel;
                modeInfo.BytesPerScanLine = 1024;
                modeInfo.NumberOfImagePages = 1;
                break;

            case 0x106: // 1280x1024x16
                modeInfo.Width = 1280;
                modeInfo.Height = 1024;
                modeInfo.BitsPerPixel = 4;
                modeInfo.MemoryModel = VbeMemoryModel.Planar;
                modeInfo.BytesPerScanLine = 160;
                modeInfo.NumberOfPlanes = 4;
                modeInfo.NumberOfImagePages = 1;
                break;

            case 0x107: // 1280x1024x256
                modeInfo.Width = 1280;
                modeInfo.Height = 1024;
                modeInfo.BitsPerPixel = 8;
                modeInfo.MemoryModel = VbeMemoryModel.PackedPixel;
                modeInfo.BytesPerScanLine = 1280;
                modeInfo.NumberOfImagePages = 1;
                break;
        }

        // Set character cell dimensions (8x16 is standard)
        modeInfo.CharacterWidth = 8;
        modeInfo.CharacterHeight = 16;

        // Direct color mode info (for 256-color modes)
        if (modeInfo.BitsPerPixel == 8) {
            modeInfo.DirectColorModeInfo = VbeDirectColorModeInfo.ColorRampProgrammable;
        }
    }

    /// <summary>
    /// Maps a VBE mode number to a standard VGA mode if possible.
    /// </summary>
    /// <param name="vbeMode">The VBE mode number.</param>
    /// <returns>The VGA mode number, or -1 if no mapping exists.</returns>
    private int MapVbeModeToVgaMode(ushort vbeMode) {
        return vbeMode switch {
            0x100 => 0x13, // 640x400x256 -> VGA mode 13h (close enough)
            0x101 => 0x13, // 640x480x256 -> VGA mode 13h
            _ => -1 // No direct mapping for other modes
        };
    }

    /// <summary>
    /// Counts the number of set bits in a value.
    /// </summary>
    /// <param name="value">The value to count bits in.</param>
    /// <returns>The number of set bits.</returns>
    private int CountSetBits(ushort value) {
        int count = 0;
        while (value != 0) {
            count += value & 1;
            value >>= 1;
        }
        return count;
    }
}
