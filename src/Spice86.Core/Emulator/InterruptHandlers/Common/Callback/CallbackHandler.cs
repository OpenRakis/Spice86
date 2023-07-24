namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers.Common.IndexBasedDispatcher;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Stores callback instructions definitions.
/// Acts as a glue between code read by the CPU (callback number) and the C# code behind that is called.
/// </summary>
public class CallbackHandler : IndexBasedDispatcher<ICallback> {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public CallbackHandler(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
    }

    /// <inheritdoc/>
    protected override UnhandledOperationException GenerateUnhandledOperationException(int index) {
        return new UnhandledCallbackException(_machine, index);
    }

    /// <summary>
    /// Adds the callback to the dispatch table.
    /// </summary>
    /// <param name="callback">The callback to add.</param>
    /// <exception cref="ArgumentException">If an item with the same callback index has already been added</exception>
    public void AddCallback(ICallback callback) {
        AddRunnable(callback.Index, callback);
    }

    /// <summary>
    /// Runs the callback from C# code that overrides the target program's machine code.
    /// </summary>
    /// <param name="index">The index at which the callback is supposed to be available.</param>
    public void RunFromOverriden(int index) {
        GetRunnable(index).RunFromOverriden();
    }

    public byte[] ReplaceAllCallbacksInRamImage(IMemory memory) {
        ByteArrayBasedIndexable indexable = new ByteArrayBasedIndexable(memory.RamCopy);
        MemoryAsmWriter memoryAsmWriter = new MemoryAsmWriter(indexable, new SegmentedAddress(0, 0), this);
        foreach (ICallback callback in this.AllRunnables) {
            memoryAsmWriter.CurrentAddress = callback.InstructionAddress;
            memoryAsmWriter.EraseCallbackWithInt(callback.Index);
        }

        return indexable.Array;
    }
}