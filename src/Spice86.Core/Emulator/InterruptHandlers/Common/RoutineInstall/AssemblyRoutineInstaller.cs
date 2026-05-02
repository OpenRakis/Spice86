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

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memoryAsmWriter">The low level class that writes x86 instructions to the memory bus.</param>
    /// <param name="functionCatalogue">List of all functions.</param>
    public AssemblyRoutineInstaller(MemoryAsmWriter memoryAsmWriter, FunctionCatalogue functionCatalogue) {
        _memoryAsmWriter = memoryAsmWriter;
        _functionCatalogue = functionCatalogue;
    }

    /// <summary>
    /// Writes ASM code of the given routine in RAM.
    /// Registers its name in the function handler as well.
    /// </summary>
    /// <param name="assemblyRoutineWriter">The class that writes machine code in memory as glue between the interrupt calls and the C# interrupt handlers.</param>
    /// <param name="name">Name of the routine. Will be registered in function catalogue.</param>
    /// <returns>Address of the handler ASM code</returns>
    public SegmentedAddress InstallAssemblyRoutine(IAssemblyRoutineWriter assemblyRoutineWriter, string name) {
        SegmentedAddress routineAddress = assemblyRoutineWriter.WriteAssemblyInRam(_memoryAsmWriter);
        _functionCatalogue.GetOrCreateFunctionInformation(routineAddress, name);
        return routineAddress;
    }
}