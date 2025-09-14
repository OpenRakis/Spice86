﻿namespace Spice86.Core.Emulator.InterruptHandlers.Timer;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Implementation of int8 that just updates a value in the bios data area.
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
        memoryAsmWriter.WriteInt(0x1C);
        // EOI to PIC after handler execution
        memoryAsmWriter.RegisterAndWriteCallback(AfterInt8Execution);
        memoryAsmWriter.WriteIret();

        return interruptHandlerAddress;
    }

    /// <summary>
    /// Gets or set the value of the real time clock, in ticks.
    /// </summary>
    public uint TickCounterValue {
        get => _biosDataArea.TimerCounter;
        set => _biosDataArea.TimerCounter = value;
    }

    private void IncTickCounterValue() {
        TickCounterValue++;
    }

    private void AfterInt8Execution() {
        _dualPic.AcknowledgeInterrupt(0);
    }

    public byte VectorNumber => 0x8;
}