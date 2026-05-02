namespace Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Handles installing ASM routines in the machine memory: <br/>
///  - Writes their emulated ASM handlers in Emulated Memory BUS <br/>
///  - Creates a function with a "nice" name to describe the handler so that it shows what it is in code generation / exports
/// </summary>
public class AssemblyRoutineInstaller {
    private readonly MemoryAsmWriter _memoryAsmWriter;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly EmulatorProvidedCodeRegistry _emulatorProvidedCodeRegistry;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memoryAsmWriter">The low level class that writes x86 instructions to the memory bus.</param>
    /// <param name="functionCatalogue">List of all functions.</param>
    /// <param name="emulatorProvidedCodeRegistry">Registry that records every routine installed by the emulator so the debugger UI can mark it visually.</param>
    public AssemblyRoutineInstaller(MemoryAsmWriter memoryAsmWriter, FunctionCatalogue functionCatalogue, EmulatorProvidedCodeRegistry emulatorProvidedCodeRegistry) {
        _memoryAsmWriter = memoryAsmWriter;
        _functionCatalogue = functionCatalogue;
        _emulatorProvidedCodeRegistry = emulatorProvidedCodeRegistry;
    }

    /// <summary>
    /// Writes ASM code of the given routine in RAM.
    /// Registers its name in the function handler as well.
    /// Also records the routine's address range in the <see cref="EmulatorProvidedCodeRegistry"/>.
    /// </summary>
    /// <param name="assemblyRoutineWriter">The class that writes machine code in memory as glue between the interrupt calls and the C# interrupt handlers.</param>
    /// <param name="name">Name of the routine. Will be registered in function catalogue.</param>
    /// <param name="subsystem">Subsystem label used by the debugger UI (e.g. "Interrupt 21h", "Mouse driver").</param>
    /// <returns>Address of the handler ASM code</returns>
    public SegmentedAddress InstallAssemblyRoutine(IAssemblyRoutineWriter assemblyRoutineWriter, string name, string subsystem) {
        SegmentedAddress routineAddress = assemblyRoutineWriter.WriteAssemblyInRam(_memoryAsmWriter);
        SegmentedAddress endAddress = _memoryAsmWriter.CurrentAddress;
        _functionCatalogue.GetOrCreateFunctionInformation(routineAddress, name);
        int byteLength = ComputeByteLength(routineAddress, endAddress);
        _emulatorProvidedCodeRegistry.Register(new ProvidedRoutineInfo(routineAddress, byteLength, name, subsystem));
        return routineAddress;
    }

    private static int ComputeByteLength(SegmentedAddress start, SegmentedAddress end) {
        long startPhysical = (long)start.Segment * 16 + start.Offset;
        long endPhysical = (long)end.Segment * 16 + end.Offset;
        long delta = endPhysical - startPhysical;
        if (delta < 0) {
            return 0;
        }
        return (int)delta;
    }
}