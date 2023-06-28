namespace Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;

using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Writes x86 ASM instructions to Memory bus
/// </summary>
public class MemoryAsmWriter : MemoryBytesWriter {
    private readonly CallbackHandler _callbackHandler;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="memory">Memory BUS to where instructions are going to be written</example></param>
    /// <param name="beginningAddress">Where to start writing</param>
    /// <param name="callbackHandler">CallbackHandler instance to use to register the callbacks we create</param>
    public MemoryAsmWriter(Memory memory, SegmentedAddress beginningAddress, CallbackHandler callbackHandler) : base(memory, beginningAddress) {
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
        uint physicalAddress = CurrentAddress.ToPhysical();
        ICallback callback = new Callback(callbackNumber, runnable, physicalAddress);
        _callbackHandler.AddCallback(callback);
        WriteCallback(callback.Index);
    }

    private void WriteCallback(byte callbackNumber) {
        WriteByte(0xFE);
        WriteByte(0x38);
        WriteByte(callbackNumber);
    }

    public void WriteIret() {
        WriteByte(0xCF);
    }

    public void WriteNop() {
        WriteByte(0x90);
    }

    /// <summary>
    /// Writes a far CALL instruction. 
    /// </summary>
    /// <param name="destination">Segmented address of the function to call</param>
    /// <returns>Returns the address of the call destination segmented address</returns>
    public SegmentedAddress WriteFarCall(SegmentedAddress destination) {
        WriteByte(0x9A);
        return WriteSegmentedAddress(destination);
    }

    public SegmentedAddress WriteSegmentedAddress(SegmentedAddress address) {
        // Make a copy of the address since it is going to be modified by our writes.
        SegmentedAddress ret = new SegmentedAddress(CurrentAddress);
        WriteWord(address.Offset);
        WriteWord(address.Segment);
        return ret;
    }

    public void WriteFarRet() {
        WriteByte(0xCB);
    }
}