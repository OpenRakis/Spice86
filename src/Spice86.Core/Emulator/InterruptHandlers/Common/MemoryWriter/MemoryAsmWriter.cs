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
    /// Registers a new callback that will call the given runnable with the manually defined callback number.<br/>
    /// </summary>
    /// <param name="callbackNumber">Callback index. For manually defined callbacks, cannot exceed 0xFF</param>
    /// <param name="runnable">Action to run when this callback is executed by the CPU</param>
    public void RegisterAndWriteCallback(byte callbackNumber, Action runnable) {
        RegisterAndWriteCallback((ushort)callbackNumber, runnable);
    }

    /// <summary>
    /// Registers a new callback that will call the given runnable.<br/>
    /// Callback number is automatically allocated.
    /// </summary>
    /// <param name="runnable"></param>
    public void RegisterAndWriteCallback(Action runnable) {
        ushort callbackNumber = _callbackHandler.AllocateNextCallback();
        RegisterAndWriteCallback(callbackNumber, runnable);
    }

    private void RegisterAndWriteCallback(ushort callbackNumber, Action runnable) {
        Callback callback = new Callback(callbackNumber, runnable, CurrentAddress);
        _callbackHandler.AddCallback(callback);
        WriteCallback(callback.Index);
    }

    private void WriteCallback(ushort callbackNumber) {
        WriteUInt8(0xFE);
        WriteUInt8(0x38);
        WriteUInt16(callbackNumber);
    }


    /// <summary>
    /// Erases a callback instruction at current address and replaces it with an INT + a NOP. Useful to dump memory and avoid unsupported opcodes in ghidra.
    /// </summary>
    /// <param name="callbackNumber">Used to write an INT instruction to memory, followed by the callback number.</param>
    public void EraseCallbackWithInt(ushort callbackNumber) {
        if (callbackNumber < 0x100) {
            this.WriteInt((byte)callbackNumber);
        } else {
            // signal this was a callback that has no conventional int representation
            this.WriteInt(0xFF);
        }
        this.WriteNop();
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

    public void WriteJumpNear(short offset) {
        WriteUInt8(0xE9);
        WriteInt16(offset);
    }

    public void WriteJumpShort(sbyte offset) {
        WriteUInt8(0xEB);
        WriteInt8(offset);
    }

    public void WriteJz(sbyte delta) {
        WriteUInt8(0x74);
        WriteInt8(delta);
    }

    public void WriteJnz(sbyte delta) {
        WriteUInt8(0x75);
        WriteInt8(delta);
    }

    /// <summary>
    /// Writes a NOP to memory. This instruction does nothing.
    /// </summary>
    public void WriteNop() {
        WriteUInt8(0x90);
    }

    /// <summary>
    /// Writes a STI instruction to memory. Re-enables hardware interrupts (IF=1).
    /// </summary>
    public void WriteSti() {
        WriteUInt8(0xFB);
    }

    /// <summary>
    /// Writes a far CALL instruction to the given inMemoryAddressSwitcher default address. <br/>
    /// Throws UnrecoverableException if DefaultAddressValue is not initialized. <br/>
    /// If successful, sets the switcher PhysicalLocation to the location of the far call address, making it possible to change it dynamically.
    /// </summary>
    /// <returns>Returns the address of the call destination segmented address</returns>
    public void WriteFarCallToSwitcherDefaultAddress(InMemoryAddressSwitcher inMemoryAddressSwitcher) {
        if (inMemoryAddressSwitcher.DefaultAddress is null) {
            throw new UnrecoverableException("Cannot write a FAR call to a null address.");
        }
        inMemoryAddressSwitcher.PhysicalLocation = WriteFarCall(inMemoryAddressSwitcher.DefaultAddress.Value).Linear;
    }

    /// <summary>
    /// Writes a far CALL instruction.
    /// </summary>
    /// <param name="destination">Segmented address of the function to call</param>
    /// <returns>Returns the address of the call destination segmented address</returns>
    public SegmentedAddress WriteFarCall(SegmentedAddress destination) {
        WriteUInt8(0x9A);
        // Make a copy of the address since it is going to be modified by our writes.
        SegmentedAddress ret = CurrentAddress;
        WriteSegmentedAddress(destination);
        return ret;
    }

    /// <summary>
    /// Writes a FAR RET instruction to memory.
    /// </summary>
    public void WriteFarRet() {
        WriteUInt8(0xCB);
    }

    /// <summary>
    /// Writes MOV AH, imm8 instruction to memory.
    /// </summary>
    /// <param name="value">Value to move into AH.</param>
    public void WriteMovAh(byte value) {
        WriteUInt8(0xB4);
        WriteUInt8(value);
    }

    /// <summary>
    /// Writes MOV AX, imm16 instruction to memory.
    /// </summary>
    /// <param name="value">Value to move into AX.</param>
    public void WriteMovAx(ushort value) {
        WriteUInt8(0xB8);
        WriteUInt16(value);
    }

    /// <summary>
    /// Writes MOV DX, imm16 instruction to memory.
    /// </summary>
    /// <param name="value">Value to move into DX.</param>
    public void WriteMovDx(ushort value) {
        WriteUInt8(0xBA);
        WriteUInt16(value);
    }

    /// <summary>
    /// Writes CMP AL, imm8 instruction to memory.
    /// </summary>
    /// <param name="value">Value to compare with AL.</param>
    public void WriteCmpAl(byte value) {
        WriteUInt8(0x3C);
        WriteUInt8(value);
    }

    /// <summary>
    /// Writes CMP AH, imm8 instruction to memory.
    /// </summary>
    /// <param name="value">Value to compare with AH.</param>
    public void WriteCmpAh(byte value) {
        WriteUInt8(0x80);
        WriteUInt8(0xFC);
        WriteUInt8(value);
    }

    /// <summary>
    /// Writes JA (Jump if Above) instruction to memory.
    /// </summary>
    /// <param name="offset">Signed offset for the jump.</param>
    public void WriteJa(sbyte offset) {
        WriteUInt8(0x77);
        WriteInt8(offset);
    }

    /// <summary>
    /// Writes JB (Jump if Below) instruction to memory.
    /// </summary>
    /// <param name="offset">Signed offset for the jump.</param>
    public void WriteJb(sbyte offset) {
        WriteUInt8(0x72);
        WriteInt8(offset);
    }

    /// <summary>
    /// Writes SUB AL, imm8 instruction to memory.
    /// </summary>
    /// <param name="value">Value to subtract from AL.</param>
    public void WriteSubAl(byte value) {
        WriteUInt8(0x2C);
        WriteUInt8(value);
    }

    /// <summary>
    /// Writes MOV AX, imm16 instruction with AH and AL bytes in sequence.
    /// Used for setting both AL (exit code) and AH (0x4C for INT 21h terminate).
    /// </summary>
    /// <param name="alValue">Value for AL (exit code).</param>
    /// <param name="ahValue">Value for AH (typically 0x4C for terminate function).</param>
    public void WriteMovAxSplit(byte alValue, byte ahValue) {
        WriteUInt8(0xB8);
        WriteUInt8(alValue);
        WriteUInt8(ahValue);
    }
}