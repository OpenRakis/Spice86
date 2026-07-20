namespace Spice86.Core.Emulator.Boot;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Boots an IBM PC-compatible machine from a floppy image without involving the DOS kernel.
/// </summary>
public sealed class PCBootLoader {
    /// <summary>
    /// Size of a floppy boot sector in bytes.
    /// </summary>
    public const int BootSectorSize = 512;

    /// <summary>
    /// Offset within segment 0 where the BIOS loads the boot sector.
    /// </summary>
    public const ushort BootSectorLoadOffset = 0x7C00;

    private const uint BootSectorLoadAddress = BootSectorLoadOffset;

    private readonly IMemory _memory;
    private readonly State _state;
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Creates a new boot loader for floppy-based PC boot images.
    /// </summary>
    public PCBootLoader(IMemory memory, State state, ILoggerService loggerService) {
        _memory = memory;
        _state = state;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Validates a floppy image, copies its first 512 bytes to physical 0x7C00, and configures the CPU registers
    /// per the BIOS bootstrap convention so execution resumes at <c>0000:7C00</c>.
    /// </summary>
    /// <param name="imageData">The full floppy image bytes.</param>
    /// <param name="driveNumber">BIOS drive number (0 = A:, 1 = B:).</param>
    /// <param name="imagePathForLogging">Image path used in info logs.</param>
    /// <returns><c>true</c> if the boot sector was loaded and CPU prepared; <c>false</c> for a missing or invalid image.</returns>
    public bool TryBootFromFloppyImage(byte[] imageData, byte driveNumber, string imagePathForLogging) {
        if (imageData.Length < BootSectorSize) {
            return false;
        }
        if (imageData[BootSectorSize - 2] != 0x55 || imageData[BootSectorSize - 1] != 0xAA) {
            return false;
        }

        _memory.LoadData(BootSectorLoadAddress, imageData, BootSectorSize);
        PrepareCpuRegistersForBoot(_state, driveNumber);
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation("PCBOOT: loaded {Bytes} bytes from floppy '{Path}' at 0000:7C00, DL={DL:X2}",
                BootSectorSize, imagePathForLogging, _state.DL);
        }
        return true;
    }

    public static void PrepareCpuRegistersForBoot(State state, byte driveNumber) {
        state.CS = 0;
        state.IP = BootSectorLoadOffset;
        state.DS = 0;
        state.ES = 0;
        state.SS = 0;
        state.SP = BootSectorLoadOffset;
        state.AX = 0;
        state.BX = BootSectorLoadOffset;
        state.CX = 1;
        state.DX = 0;
        state.DL = driveNumber;
        state.SI = 0;
        state.DI = 0;
        state.BP = 0;
        state.InterruptFlag = true;
    }
}
