namespace Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;

using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Errors;
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
        Callback callback = new Callback(callbackNumber, runnable, GetCurrentAddressCopy());
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
    /// <param name="callbackNumber">Used to write an INT instruction to memory, followed by the callback number.</param>
    public void EraseCallbackWithInt(byte callbackNumber) {
        this.WriteInt(callbackNumber);
        this.WriteNop();
    }

    /// <summary>
    /// Writes an IRET instruction to memory.
    /// </summary>
    public void WriteIret() {
        WriteUInt8(0xCF);
    }

    /// <summary>
    /// Writes an INT instruction to memory, followed by the vector number.
    /// </summary>
    /// <param name="vectorNumber">The interrupt to call, represented by its vector number.</param>
    public void WriteInt(byte vectorNumber) {
        WriteUInt8(0xCD);
        WriteUInt8(vectorNumber);
    }

    /// <summary>
    /// Writes a NOP to memory. This instruction does nothing.
    /// </summary>
    public void WriteNop() {
        WriteUInt8(0x90);
    }

    /// <summary>
    /// Writes a far CALL instruction to the given inMemoryAddressSwitcher default address.
    /// Throws UnrecoverableException if DefaultAddressValue is not initialized.
    /// If successful, sets the switcher PhysicalLocation to the location of the far call address, making it possible to change it dynamically.
    /// </summary>
    /// <returns>Returns the address of the call destination segmented address</returns>
    public void WriteFarCallToSwitcherDefaultAddress(InMemoryAddressSwitcher inMemoryAddressSwitcher) {
        if (inMemoryAddressSwitcher.DefaultAddress is null) {
            throw new UnrecoverableException("Cannot write a FAR call to a null address.");
        }
        inMemoryAddressSwitcher.PhysicalLocation = WriteFarCall(inMemoryAddressSwitcher.DefaultAddress.Value).ToPhysical();
    }

    /// <summary>
    /// Writes a far CALL instruction.
    /// </summary>
    /// <param name="destination">Segmented address of the function to call</param>
    /// <returns>Returns the address of the call destination segmented address</returns>
    public SegmentedAddress WriteFarCall(SegmentedAddress destination) {
        WriteUInt8(0x9A);
        // Make a copy of the address since it is going to be modified by our writes.
        SegmentedAddress ret = GetCurrentAddressCopy();
        WriteSegmentedAddress(destination);
        return ret;
    }

    /// <summary>
    /// Writes a FAR RET instruction to memory.
    /// </summary>
    public void WriteFarRet() {
        WriteUInt8(0xCB);
    }
}