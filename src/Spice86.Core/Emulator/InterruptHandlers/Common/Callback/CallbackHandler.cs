namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers.Common.IndexBasedDispatcher;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Stores callback instructions definitions.
/// Acts as a glue between code read by the CPU (callback number) and the C# code behind that is called.
/// </summary>
public class CallbackHandler : IndexBasedDispatcher<ICallback> {
    private const ushort CallbackAllocationStart = 0x100;

    private ushort _nextFreeCallback = CallbackAllocationStart;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public CallbackHandler(State state, ILoggerService loggerService) : base(state, loggerService) {
    }

    /// <inheritdoc/>
    protected override UnhandledOperationException GenerateUnhandledOperationException(int index) {
        return new UnhandledCallbackException(State, index);
    }

    /// <summary>
    /// Allocates the number for the next callback
    /// </summary>
    /// <returns>A free callback number</returns>
    /// <exception cref="InvalidOperationException">If no more callbacks are available</exception>
    public ushort AllocateNextCallback() {
        if (_nextFreeCallback == ushort.MaxValue) {
            throw new InvalidOperationException("Callback allocation was requested but there are no free callbacks anymore");
        }
        return _nextFreeCallback++;
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

    /// <summary>
    /// Remove Spice86 machine code for callbacks from the memory dump, so it has less "noise".
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <returns>A byte array representing the memory content with the Spice86 machine code for callbacks removed.</returns>
    public byte[] ReplaceAllCallbacksInRamImage(IMemory memory) {
        ByteArrayBasedIndexable indexable = new ByteArrayBasedIndexable(memory.ReadRam());
        MemoryAsmWriter memoryAsmWriter = new MemoryAsmWriter(indexable, SegmentedAddress.ZERO, this);
        foreach (ICallback callback in this.AllRunnables) {
            memoryAsmWriter.CurrentAddress = callback.InstructionAddress;
            memoryAsmWriter.EraseCallbackWithInt(callback.Index);
        }

        return indexable.Array;
    }
}