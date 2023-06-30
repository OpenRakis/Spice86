namespace Spice86.Core.Emulator.InterruptHandlers;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Handles installing interrupts in the machine:
///  - Writes their emulated ASM handlers in Emulated Memory BUS
///  - Registers the handlers in the Vector table at the appropriate place
///  - Creates a function with a "nice" name to describe the handler so that it shows what it is in code generation / exports
/// </summary>
public class InterruptInstaller {
    private readonly MemoryAsmWriter _memoryAsmWriter;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly FunctionHandler _functionHandler;

    public InterruptInstaller(Indexable memory, CallbackHandler callbackHandler, FunctionHandler functionHandler) {
        _functionHandler = functionHandler;
        SegmentedAddress beginningAddress = new SegmentedAddress(MemoryMap.InterruptHandlersSegment, 0);
        _memoryAsmWriter = new MemoryAsmWriter(memory, beginningAddress, callbackHandler);
        _interruptVectorTable = new InterruptVectorTable(memory);
    }

    /// <summary>
    /// Writes ASM code of the given handler in RAM.
    /// Registers it in the vector table and register its name in the function handler as well.
    /// </summary>
    /// <param name="interruptHandler"></param>
    public void InstallInterruptHandler(IInterruptHandler interruptHandler) {
        SegmentedAddress handlerAddress = interruptHandler.WriteAssemblyInRam(_memoryAsmWriter);
                
        // Define ASM in vector table
        _interruptVectorTable[interruptHandler.VectorNumber] = (handlerAddress.Segment, handlerAddress.Offset);

        // Define interrupt in function handler so that users have an idea what this is among all the other horrible things DOS programs do
        string name = interruptHandler.GetType().Name;
        _functionHandler.GetOrCreateFunctionInformation(handlerAddress, $"provided_interrupt_handler_{interruptHandler.VectorNumber}_{name}");
    }
}