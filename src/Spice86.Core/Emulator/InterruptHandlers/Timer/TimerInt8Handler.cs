namespace Spice86.Core.Emulator.InterruptHandlers.Timer;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Updates the BIOS timer and calls INT 1Ch while preserving AX, DX and DS.
/// </summary>
public sealed class TimerInt8Handler : IInterruptHandler {
    private readonly DualPic _dualPic;
    private readonly BiosDataArea _biosDataArea;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="dualPic">The two programmable interrupt controllers.</param>
    /// <param name="biosDataArea">The memory mapped BIOS values.</param>
    public TimerInt8Handler(DualPic dualPic, BiosDataArea biosDataArea) {
        _dualPic = dualPic;
        _biosDataArea = biosDataArea;
    }

    public SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        // Write ASM
        SegmentedAddress interruptHandlerAddress = memoryAsmWriter.CurrentAddress;
        // Increment BIOS tick counter
        memoryAsmWriter.RegisterAndWriteCallback(VectorNumber, IncTickCounterValue);
        // Call user timer hook
        memoryAsmWriter.WriteUInt8(0x1E); // PUSH DS
        memoryAsmWriter.WriteUInt8(0x50); // PUSH AX
        memoryAsmWriter.WriteUInt8(0x52); // PUSH DX
        memoryAsmWriter.WriteInt(0x1C);
        // EOI to PIC after handler execution
        memoryAsmWriter.RegisterAndWriteCallback(CallbackNumbers.TimerAfterInt8Execution, AfterInt8Execution);
        memoryAsmWriter.WriteUInt8(0x5A); // POP DX
        memoryAsmWriter.WriteUInt8(0x58); // POP AX
        memoryAsmWriter.WriteUInt8(0x1F); // POP DS
        memoryAsmWriter.WriteIret();

        return interruptHandlerAddress;
    }

    /// <summary>Runs an <c>int 1Ch</c> a program can hook, so its effects are not bounded.</summary>
    [InterruptService(Unknown = true)]
    private void IncTickCounterValue() {
        _biosDataArea.TimerCounter++;
        if (_biosDataArea.FloppyMotorCounter > 0) {
            _biosDataArea.FloppyMotorCounter--;
        }
    }

    private void AfterInt8Execution() {
        _dualPic.AcknowledgeInterrupt(0);
    }

    public byte VectorNumber => 0x8;

    /// <inheritdoc />
    public IReadOnlySet<byte> ChainedVectorNumbers { get; } = new HashSet<byte> { 0x1C };
}