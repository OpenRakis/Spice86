namespace Spice86.Core.Emulator.InterruptHandlers;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Interface for C# interrupt handlers.
/// Interrupt handlers write their ASM code in emulated memory.
/// </summary>
public interface IInterruptHandler {
    /// <summary>
    /// Vector number of the interrupt beeing represented
    /// </summary>
    public byte VectorNumber { get; }
    /// <summary>
    /// Writes the ASM implementation of the Interrupt handler in emulated RAM.
    /// </summary>
    /// <param name="memoryAsmWriter"></param>
    public SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter);
}