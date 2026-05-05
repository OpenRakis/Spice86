namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Storage;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// INT 26h — Absolute Disk Write.
/// For floppy drives (AL=0/1) writes sectors directly into the floppy image via
/// <see cref="IFloppyDriveAccess"/>. Hard disk drives (AL>=2) return success without
/// transferring data.
/// </summary>
/// <remarks>
/// Per the DOS specification, INT 26h returns via RETF (not IRET), leaving the
/// FLAGS word pushed by the INT instruction on the stack. Callers are required to
/// POPF after INT 26h to discard those flags.
/// </remarks>
public class DosDiskInt26Handler : InterruptHandler {
    private const ushort ExtendedTransferMagic = 0xFFFF;
    private const ushort ErrorInvalidDrive = 0x8002;

    private readonly DosDriveManager _dosDriveManager;

    /// <summary>
    /// Initializes a new instance of <see cref="DosDiskInt26Handler"/>.
    /// </summary>
    /// <param name="memory">The emulated memory bus.</param>
    /// <param name="dosDriveManager">Provides access to mounted floppy and hard disk drives.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="loggerService">The logging service implementation.</param>
    public DosDiskInt26Handler(IMemory memory, DosDriveManager dosDriveManager,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _dosDriveManager = dosDriveManager;
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x26;

    /// <inheritdoc />
    /// <remarks>
    /// INT 25h/26h use RETF instead of IRET, leaving FLAGS on the stack for the caller to POPF.
    /// This matches real DOS and DOSBox Staging behaviour.
    /// </remarks>
    public override SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        SegmentedAddress handlerAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.RegisterAndWriteCallback(VectorNumber, Run);
        memoryAsmWriter.WriteFarRet();
        return handlerAddress;
    }

    /// <inheritdoc />
    public override void Run() {
        byte driveIndex = State.AL;
        if (driveIndex >= DosDriveManager.MaxDriveCount || !_dosDriveManager.HasDriveAtIndex(driveIndex)) {
            State.AX = ErrorInvalidDrive;
            SetCarryFlag(true, true);
            return;
        }

        uint startSector;
        ushort sectorCount;
        uint bufferAddress;

        if (State.CX == ExtendedTransferMagic) {
            uint structAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.BX);
            startSector = Memory.UInt32[structAddress];
            sectorCount = Memory.UInt16[structAddress + 4u];
            ushort bufOff = Memory.UInt16[structAddress + 6u];
            ushort bufSeg = Memory.UInt16[structAddress + 8u];
            bufferAddress = MemoryUtils.ToPhysicalAddress(bufSeg, bufOff);
        } else {
            startSector = State.DX;
            sectorCount = State.CX;
            bufferAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.BX);
        }

        if (driveIndex >= 2) {
            SetCarryFlag(false, true);
            State.AX = 0;
            return;
        }

        if (!_dosDriveManager.TryGetGeometry(driveIndex, out int _, out int _, out int _, out int bytesPerSector)) {
            State.AX = ErrorInvalidDrive;
            SetCarryFlag(true, true);
            return;
        }

        int byteOffset = (int)((long)startSector * bytesPerSector);
        int byteCount = sectorCount * bytesPerSector;
        byte[] buffer = new byte[byteCount];
        for (int i = 0; i < byteCount; i++) {
            buffer[i] = Memory.UInt8[bufferAddress + (uint)i];
        }

        if (!_dosDriveManager.TryWrite(driveIndex, byteOffset, buffer, 0, byteCount)) {
            State.AX = 0x0408;
            SetCarryFlag(true, true);
            return;
        }

        SetCarryFlag(false, true);
        State.AX = 0;
    }
}
