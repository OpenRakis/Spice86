namespace Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Handles installing interrupts in the machine: <br/>
///  - Writes their emulated ASM handlers in Emulated Memory BUS <br/>
///  - Registers the handlers in the Vector table at the appropriate place <br/>
///  - Creates a function with a "nice" name to describe the handler so that it shows what it is in code generation / exports
/// </summary>
public class InterruptInstaller : AssemblyRoutineInstaller {
    private readonly InterruptVectorTable _interruptVectorTable;

    /// <summary>
    /// Initializes a new instance of the <see cref="InterruptInstaller"/> class.
    /// </summary>
    /// <param name="interruptVectorTable">The interrupt vector table</param>
    /// <param name="memoryAsmWriter">The class that writes machine code for interrupt handlers</param>
    /// <param name="functionCatalogue">List of all functions.</param>
    /// <param name="emulatorProvidedCodeRegistry">Registry that records every routine installed by the emulator so the debugger UI can mark it visually.</param>
    public InterruptInstaller(InterruptVectorTable interruptVectorTable, MemoryAsmWriter memoryAsmWriter, FunctionCatalogue functionCatalogue, EmulatorProvidedCodeRegistry emulatorProvidedCodeRegistry) : base(memoryAsmWriter, functionCatalogue, emulatorProvidedCodeRegistry) {
        _interruptVectorTable = interruptVectorTable;
    }

    /// <summary>
    /// Writes ASM code of the given handler in RAM.
    /// Registers it in the vector table and register its name in the function handler as well.
    /// </summary>
    /// <param name="interruptHandler">The class that implements the interrupt handling with C# functions.</param>
    /// <returns>Address of the handler ASM code</returns>
    public SegmentedAddress InstallInterruptHandler(IInterruptHandler interruptHandler) {
        string name = $"provided_interrupt_handler_{interruptHandler.VectorNumber:X}";
        string subsystem = $"Interrupt {interruptHandler.VectorNumber:X2}h";
        SegmentedAddress handlerAddress = InstallAssemblyRoutine(interruptHandler, name, subsystem);

        // Define ASM in vector table
        _interruptVectorTable[interruptHandler.VectorNumber] = new(handlerAddress.Segment, handlerAddress.Offset);
        return handlerAddress;
    }
}