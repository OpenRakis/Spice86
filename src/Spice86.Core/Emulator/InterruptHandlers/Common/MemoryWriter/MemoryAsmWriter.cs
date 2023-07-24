namespace Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;

using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Writes x86 ASM instructions to Memory bus
/// </summary>
public class MemoryAsmWriter : MemoryWriter {
    private readonly CallbackHandler _callbackHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryAsmWriter"/> class with the specified memory as a data sink, beginningAddress and callbackHandler to register callbacks.
    /// </summary>
    /// <param name="memory">Memory BUS to where instructions are going to be written</param>
    /// <param name="beginningAddress">Where to start writing</param>
    /// <param name="callbackHandler">CallbackHandler instance to use to register the callbacks we create</param>
    public MemoryAsmWriter(IIndexable memory, SegmentedAddress beginningAddress, CallbackHandler callbackHandler) : base(memory, beginningAddress) {
        _callbackHandler = callbackHandler;
    }

    /// <summary>
    /// Registers a new callback that will call the given runnable:
    ///  - Callback will know its physical address in memory
    ///  - Callback will be registered in the callback handler
    ///  - Callback instruction referring this callback will be written as ASM
    /// </summary>
    /// <param name="callbackNumber">Callback index</param>
    /// <param name="runnable">Action to run when this callback is executed by the CPU</param>
    public void RegisterAndWriteCallback(byte callbackNumber, Action runnable) {
        ICallback callback = new Callback(callbackNumber, runnable, new SegmentedAddress(CurrentAddress));
        _callbackHandler.AddCallback(callback);
        WriteCallback(callback.Index);
    }

    private void WriteCallback(byte callbackNumber) {
        WriteUInt8(0xFE);
        WriteUInt8(0x38);
        WriteUInt8(callbackNumber);
    }

    
    /// <summary>
    /// Erases a callback instruction at current address and replaces it with an INT + a NOP. Useful to dump memory and avoid unsupported opcodes in ghidra.
    /// </summary>
    /// <param name="callbackNumber"></param>
    public void EraseCallbackWithInt(byte callbackNumber) {
        this.WriteInt(callbackNumber);
        this.WriteNop();
    }

    public void WriteIret() {
        WriteUInt8(0xCF);
    }

    public void WriteInt(byte vectorNumber) {
        WriteUInt8(0xCD);
        WriteUInt8(vectorNumber);
    }

    public void WriteNop() {
        WriteUInt8(0x90);
    }

    /// <summary>
    /// Writes a far CALL instruction. 
    /// </summary>
    /// <param name="destination">Segmented address of the function to call</param>
    /// <returns>Returns the address of the call destination segmented address</returns>
    public SegmentedAddress WriteFarCall(SegmentedAddress destination) {
        WriteUInt8(0x9A);
        // Make a copy of the address since it is going to be modified by our writes.
        SegmentedAddress ret = new SegmentedAddress(CurrentAddress);
        WriteSegmentedAddress(destination);
        return ret;

    }

    public void WriteFarRet() {
        WriteUInt8(0xCB);
    }
}