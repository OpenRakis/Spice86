namespace Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Handles installing interrupts in the machine: <br/>
///  - Writes their emulated ASM handlers in Emulated Memory BUS <br/>
///  - Registers the handlers in the Vector table at the appropriate place <br/>
///  - Creates a function with a "nice" name to describe the handler so that it shows what it is in code generation / exports
/// </summary>
public class InterruptInstaller : AssemblyRoutineInstaller {
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly InstalledInterruptHandlers _installed;
    private readonly IEmulatorLoggerState _loggerState;

    /// <summary>
    /// Initializes a new instance of the <see cref="InterruptInstaller"/> class.
    /// </summary>
    /// <param name="interruptVectorTable">The interrupt vector table</param>
    /// <param name="memoryAsmWriter">The class that writes machine code for interrupt handlers</param>
    /// <param name="functionCatalogue">List of all functions.</param>
    /// <param name="installed">The registry of installed interrupt handlers to record into.</param>
    public InterruptInstaller(InterruptVectorTable interruptVectorTable, MemoryAsmWriter memoryAsmWriter,
        FunctionCatalogue functionCatalogue, IEmulatorLoggerState loggerState,
        InstalledInterruptHandlers installed) : base(memoryAsmWriter, functionCatalogue) {
        _interruptVectorTable = interruptVectorTable;
        _loggerState = loggerState;
        _installed = installed;
    }

    /// <summary>
    /// Writes ASM code of the given handler in RAM.
    /// Registers it in the vector table and register its name in the function handler as well.
    /// </summary>
    /// <param name="interruptHandler">The class that implements the interrupt handling with C# functions.</param>
    /// <returns>Address of the handler ASM code</returns>
    public SegmentedAddress InstallInterruptHandler(IInterruptHandler interruptHandler) {
        if (interruptHandler is IInterruptLoggerStateConsumer interruptLoggerStateConsumer) {
            interruptLoggerStateConsumer.SetLoggerState(_loggerState);
        }

        SegmentedAddress handlerAddress = InstallAssemblyRoutine(interruptHandler,
            $"provided_interrupt_handler_{interruptHandler.VectorNumber:X}");

        // Define ASM in vector table
        _interruptVectorTable[interruptHandler.VectorNumber] = new(handlerAddress.Segment, handlerAddress.Offset);
        _installed.Register(interruptHandler, handlerAddress);
        return handlerAddress;
    }
}
