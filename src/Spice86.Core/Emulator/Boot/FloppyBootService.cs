namespace Spice86.Core.Emulator.Boot;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Bootstraps the CPU from a floppy disk image, mirroring the IBM PC BIOS
/// floppy-boot convention used by DOSBox Staging's <c>boot.cpp</c>.
/// <para>
/// This service deliberately lives outside the DOS namespace: PC booters
/// produced by the BOOT command do not run on top of the emulator's DOS
/// kernel. The DOS layer only orchestrates user-facing argument parsing
/// (drive letter, image path) before delegating to this service.
/// </para>
/// </summary>
public sealed class FloppyBootService {
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
    /// Creates a new <see cref="FloppyBootService"/>.
    /// </summary>
    public FloppyBootService(IMemory memory, State state, ILoggerService loggerService) {
        _memory = memory;
        _state = state;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Validates a floppy image, copies its first 512 bytes to physical
    /// 0x7C00, and configures the CPU registers per the BIOS bootstrap
    /// convention so the emulator resumes execution at <c>0000:7C00</c>.
    /// <para>
    /// Registers set: <c>CS:IP=0000:7C00</c>, <c>SS:SP=0000:7C00</c>,
    /// <c>DS=ES=0</c>, <c>AX=0</c>, <c>BX=0x7C00</c>, <c>CX=1</c>,
    /// <c>DX=0</c>, <c>DL</c>=<paramref name="driveNumber"/>,
    /// <c>SI=DI=BP=0</c>, IF set.
    /// </para>
    /// </summary>
    /// <param name="imageData">The full floppy image bytes.</param>
    /// <param name="driveNumber">BIOS drive number (0 = A:, 1 = B:).</param>
    /// <param name="imagePathForLogging">Optional path used in info logs.</param>
    /// <returns><c>true</c> if the boot sector was loaded and CPU prepared; <c>false</c> for a missing/invalid image.</returns>
    public bool TryBootFromFloppyImage(byte[]? imageData, byte driveNumber, string? imagePathForLogging) {
        if (imageData is null || imageData.Length < BootSectorSize) {
            return false;
        }
        if (imageData[BootSectorSize - 2] != 0x55 || imageData[BootSectorSize - 1] != 0xAA) {
            return false;
        }

        _memory.LoadData(BootSectorLoadAddress, imageData, BootSectorSize);
        SetupCpuRegistersForBoot(driveNumber);
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("BOOT: loaded {Bytes} bytes from floppy '{Path}' at 0000:7C00, DL={DL:X2}",
                BootSectorSize, imagePathForLogging ?? string.Empty, _state.DL);
        }
        return true;
    }

    private void SetupCpuRegistersForBoot(byte driveNumber) {
        _state.CS = 0;
        _state.IP = BootSectorLoadOffset;
        _state.DS = 0;
        _state.ES = 0;
        _state.SS = 0;
        _state.SP = BootSectorLoadOffset;
        _state.AX = 0;
        _state.BX = BootSectorLoadOffset;
        _state.CX = 1;
        _state.DX = 0;
        _state.DL = driveNumber;
        _state.SI = 0;
        _state.DI = 0;
        _state.BP = 0;
        _state.InterruptFlag = true;
    }
}
