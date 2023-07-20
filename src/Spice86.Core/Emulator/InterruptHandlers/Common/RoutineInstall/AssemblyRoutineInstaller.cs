namespace Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Handles installing ASM routines in the machine memory:
///  - Writes their emulated ASM handlers in Emulated Memory BUS
///  - Creates a function with a "nice" name to describe the handler so that it shows what it is in code generation / exports
/// </summary>
public class AssemblyRoutineInstaller {
    private readonly MemoryAsmWriter _memoryAsmWriter;
    private readonly FunctionHandler _functionHandler;

    public AssemblyRoutineInstaller(MemoryAsmWriter memoryAsmWriter, FunctionHandler functionHandler) {
        _memoryAsmWriter = memoryAsmWriter;
        _functionHandler = functionHandler;
    }

    /// <summary>
    /// Writes ASM code of the given routine in RAM.
    /// Registers its name in the function handler as well.
    /// </summary>
    /// <param name="assemblyRoutineWriter"></param>
    /// <param name="namePrefix"></param>
    /// <returns>Address of the handler ASM code</returns>
    public SegmentedAddress InstallAssemblyRoutine(IAssemblyRoutineWriter assemblyRoutineWriter, string? namePrefix = null) {
        SegmentedAddress routineAddress = assemblyRoutineWriter.WriteAssemblyInRam(_memoryAsmWriter);
        // Define interrupt in function handler so that users have an idea what this is among all the other horrible things DOS programs do
        string routineName = routineAddress.GetType().Name;
        string name = routineName;
        if (!string.IsNullOrWhiteSpace(namePrefix)) {
            name = $"{namePrefix}_{routineName}";
        }

        _functionHandler.GetOrCreateFunctionInformation(routineAddress, name);
        return routineAddress;
    }
}