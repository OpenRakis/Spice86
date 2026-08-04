namespace Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
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
    private readonly HashSet<byte> _hardwareInterruptVectorNumbers;
    private readonly List<SegmentedAddress> _installedHardwareInterruptHandlerAddresses = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InterruptInstaller"/> class.
    /// </summary>
    /// <param name="interruptVectorTable">The interrupt vector table</param>
    /// <param name="memoryAsmWriter">The class that writes machine code for interrupt handlers</param>
    /// <param name="functionCatalogue">List of all functions.</param>
    /// <param name="hardwareInterruptVectorNumbers">Interrupts that are hardware-triggered (external event).</param>
    public InterruptInstaller(InterruptVectorTable interruptVectorTable, MemoryAsmWriter memoryAsmWriter,
        FunctionCatalogue functionCatalogue, IEnumerable<byte> hardwareInterruptVectorNumbers) : base(memoryAsmWriter, functionCatalogue) {
        _interruptVectorTable = interruptVectorTable;
        _hardwareInterruptVectorNumbers = new HashSet<byte>(hardwareInterruptVectorNumbers);
    }

    /// <summary>
    /// Entry addresses of the emulator-installed handlers.
    /// These fire on external events with nondeterministic timing and may never be reached from the program's
    /// observed entry points, so they are seeded as known-safe CFG roots for speculative exploration.
    /// Captured at install time, so every address here is by construction emulator-installed.
    /// </summary>
    public IReadOnlyList<SegmentedAddress> InstalledHardwareInterruptHandlerAddresses =>
        _installedHardwareInterruptHandlerAddresses;

    /// <summary>
    /// Writes ASM code of the given handler in RAM.
    /// Registers it in the vector table and register its name in the function handler as well.
    /// </summary>
    /// <param name="interruptHandler">The class that implements the interrupt handling with C# functions.</param>
    /// <returns>Address of the handler ASM code</returns>
    public SegmentedAddress InstallInterruptHandler(IInterruptHandler interruptHandler) {
        SegmentedAddress handlerAddress = InstallAssemblyRoutine(interruptHandler,
            $"provided_interrupt_handler_{interruptHandler.VectorNumber:X}");

        // Define ASM in vector table
        _interruptVectorTable[interruptHandler.VectorNumber] = new(handlerAddress.Segment, handlerAddress.Offset);
        if (_hardwareInterruptVectorNumbers.Contains(interruptHandler.VectorNumber)) {
            _installedHardwareInterruptHandlerAddresses.Add(handlerAddress);
        }
        return handlerAddress;
    }
}