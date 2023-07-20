namespace Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;

using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Interface for objects that need assembly routine to be in ram
/// </summary>
public interface IAssemblyRoutineWriter {
    /// <summary>
    /// Writes the ASM implementation of the Interrupt handler in emulated RAM.
    /// </summary>
    /// <param name="memoryAsmWriter"></param>
    public SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter);
}