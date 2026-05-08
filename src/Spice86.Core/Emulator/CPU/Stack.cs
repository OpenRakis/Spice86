namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Memory;

using System.Text;

/// <summary>
/// Represents the stack of the CPU.
/// In the x86 architecture, the stack grows downwards, meaning it grows from higher memory addresses to lower memory addresses. <br/>
/// <para>
/// Visualization: <br/><br/>
///
/// Higher Memory Addresses<br/>
/// +-------------------+<br/>
/// |                   |<br/>
/// |                   |<br/>
/// |    Stack Data     |<br/>
/// |                   |<br/>
/// |                   |<br/>
/// +-------------------+ &lt; Stack Pointer (SP)<br/>
/// |                   |<br/>
/// |    Free Space     |<br/>
/// |                   |<br/>
/// |                   |<br/>
/// +-------------------+<br/>
/// Lower Memory Addresses<br/>
/// </para>
/// <para>
/// <b>Why SP and not ESP:</b> Spice86 targets real-mode DOS, where the stack address is always computed
/// as SS:SP with SP truncated to 16 bits. The 32-bit ESP register would only govern stack addressing
/// if the SS segment descriptor's B (Big) bit were set, which only occurs in protected-mode 32-bit
/// stack segments. Real-mode BIOS initialization leaves B=0, so SP is the authoritative pointer.
/// Note that the operand size (16-bit vs 32-bit values pushed or popped) is independent of this:
/// <see cref="Push32"/> and <see cref="Pop32"/> correctly store 32-bit values while still advancing
/// the 16-bit SP, matching actual 386+ real-mode behaviour.
/// </para>
/// </summary>
public class Stack {
    private readonly IMemory _memory;

    private readonly State _state;

    /// <summary>
    /// Creates a new instance of the <see cref="Stack"/> class
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="state">The CPU Registers and Flags.</param>
    public Stack(IMemory memory, State state) {
        this._memory = memory;
        this._state = state;
    }

    /// <summary>
    /// Peeks a 8 bit value from the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    /// <returns>The value in memory.</returns>
    public byte Peek8(int index) {
        ushort offset = (ushort)(_state.SP + index);
        return _memory.UInt8[_state.SS, offset, SegmentAccessKind.Stack];
    }

    /// <summary>
    /// Peeks a 16 bit value from the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    /// <returns>The value in memory.</returns>
    public ushort Peek16(int index) {
        ushort offset = (ushort)(_state.SP + index);
        return _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack];
    }

    /// <summary>
    /// Pokes a 16 bit value on the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    /// <param name="value">The value to store in memory.</param>
    public void Poke16(int index, ushort value) {
        ushort offset = (ushort)(_state.SP + index);
        _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack] = value;
    }

    /// <summary>
    /// Pops a 16 bit value from the stack
    /// </summary>
    /// <returns>The value retrieved from the stack, therefore read from memory</returns>
    public ushort Pop16() {
        ushort res = _memory.UInt16[_state.SS, _state.SP, SegmentAccessKind.Stack];
        _state.SP = (ushort)(_state.SP + 2);
        return res;
    }

    /// <summary>
    /// Pushes a 16 bit value on the stack
    /// </summary>
    /// <param name="value">The value pushed onto the stack, therefore stored in memory.</param>
    public void Push16(ushort value) {
        ushort newSp = (ushort)(_state.SP - 2);
        _memory.UInt16[_state.SS, newSp, SegmentAccessKind.Stack] = value;
        _state.SP = newSp;
    }

    /// <summary>
    /// Peeks a 32 bit value from the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    public uint Peek32(int index) {
        ushort offset = (ushort)(_state.SP + index);
        return _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack];
    }

    /// <summary>
    /// Pokes a 32 bit value on the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    /// <param name="value">The value to store in memory.</param>
    public void Poke32(int index, uint value) {
        ushort offset = (ushort)(_state.SP + index);
        _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack] = value;
    }

    /// <summary>
    /// Pops a 32 bit value from the stack
    /// </summary>
    /// <returns>The value popped from the stack.</returns>
    public uint Pop32() {
        uint res = _memory.UInt32[_state.SS, _state.SP, SegmentAccessKind.Stack];
        _state.SP = (ushort)(_state.SP + 4);
        return res;
    }

    /// <summary>
    /// Performs a 32-bit LEAVE using a 16-bit stack address.
    /// </summary>
    public void Leave32() {
        ushort framePointer = _state.BP;
        uint basePointer = _memory.UInt32[_state.SS, framePointer, SegmentAccessKind.Stack];
        _state.SP = (ushort)(framePointer + 4);
        _state.EBP = basePointer;
    }

    /// <summary>
    /// Performs a 16-bit LEAVE using a 16-bit stack address.
    /// Reads the new BP from [SS:BP] before committing SP, so a #SS during the
    /// pop leaves SP unchanged (matching real-80386 fault atomicity).
    /// Only the low 16 bits of EBP are written; upper 16 bits of EBP are preserved.
    /// </summary>
    public void Leave16() {
        ushort framePointer = _state.BP;
        ushort basePointer = _memory.UInt16[_state.SS, framePointer, SegmentAccessKind.Stack];
        _state.SP = (ushort)(framePointer + 2);
        _state.BP = basePointer;
    }

    /// <summary>
    /// Pushes a 32 bit value on the stack
    /// </summary>
    /// <param name="value">The value to store onto the stack.</param>
    public void Push32(uint value) {
        ushort newSp = (ushort)(_state.SP - 4);
        _memory.UInt32[_state.SS, newSp, SegmentAccessKind.Stack] = value;
        _state.SP = newSp;
    }

    /// <summary>
    /// Pre-validates that all slots for a multi-register push (PUSHA/PUSHAD) are accessible.
    /// Checks each slot going downward from the current SP. Raises #SS if any slot crosses the segment limit.
    /// No state is modified if the check fails.
    /// </summary>
    /// <param name="valueSizeBytes">Size of each value in bytes (2 for 16-bit, 4 for 32-bit).</param>
    /// <param name="valueCount">Number of values to push.</param>
    public void ValidateStackPushRange(ushort valueSizeBytes, ushort valueCount) {
        ushort offset = _state.SP;
        for (ushort i = 0; i < valueCount; i++) {
            offset = (ushort)(offset - valueSizeBytes);
            _memory.Mmu.CheckAccess(_state.SS, offset, valueSizeBytes, SegmentAccessKind.Stack);
        }
    }

    /// <summary>
    /// Pre-validates that all slots for a multi-register pop (POPA/POPAD) are accessible.
    /// Checks each slot going upward from the current SP. Raises #SS if any slot crosses the segment limit.
    /// No state is modified if the check fails.
    /// </summary>
    /// <param name="valueSizeBytes">Size of each value in bytes (2 for 16-bit, 4 for 32-bit).</param>
    /// <param name="valueCount">Number of values to pop.</param>
    public void ValidateStackPopRange(ushort valueSizeBytes, ushort valueCount) {
        ushort offset = _state.SP;
        for (ushort i = 0; i < valueCount; i++) {
            _memory.Mmu.CheckAccess(_state.SS, offset, valueSizeBytes, SegmentAccessKind.Stack);
            offset = (ushort)(offset + valueSizeBytes);
        }
    }

    /// <summary>
    /// Pushes all 8 general-purpose 16-bit registers (PUSHA order: AX, CX, DX, BX, SP, BP, SI, DI).
    /// </summary>
    public void PushAll16(ushort ax, ushort cx, ushort dx, ushort bx, ushort sp, ushort bp, ushort si, ushort di) {
        CpuStackSegmentFaultException? pendingFault = GetStackPushRangeFault(2, 8);
        ushort offset = _state.SP;
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, ax);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, cx);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, dx);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, bx);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, sp);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, bp);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, si);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, di);
        if (pendingFault != null) {
            throw pendingFault;
        }
        _state.SP = offset;
    }

    /// <summary>
    /// Pushes all 8 general-purpose 32-bit registers (PUSHAD order: EAX, ECX, EDX, EBX, ESP, EBP, ESI, EDI).
    /// </summary>
    public void PushAll32(uint eax, uint ecx, uint edx, uint ebx, uint esp, uint ebp, uint esi, uint edi) {
        CpuStackSegmentFaultException? pendingFault = GetStackPushRangeFault(4, 8);
        ushort offset = _state.SP;
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, eax);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, ecx);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, edx);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, ebx);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, esp);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, ebp);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, esi);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, edi);
        if (pendingFault != null) {
            throw pendingFault;
        }
        _state.SP = offset;
    }

    private CpuStackSegmentFaultException? GetStackPushRangeFault(ushort valueSizeBytes, ushort valueCount) {
        ushort offset = _state.SP;
        for (ushort index = 0; index < valueCount; index++) {
            offset = (ushort)(offset - valueSizeBytes);
            try {
                _memory.Mmu.CheckAccess(_state.SS, offset, valueSizeBytes, SegmentAccessKind.Stack);
            } catch (CpuStackSegmentFaultException exception) {
                return exception;
            }
        }
        return null;
    }

    /// <summary>
    /// Pops all 8 general-purpose 16-bit registers (POPA order: DI, SI, BP, skip SP, BX, DX, CX, AX).
    /// Each slot is read individually; if a slot raises #SS, earlier register assignments persist
    /// while SP is left at its original value (matches 80386 partial-pop fault semantics).
    /// </summary>
    public void PopAll16() {
        ushort offset = _state.SP;
        _state.DI = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 2);
        _state.SI = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 2);
        _state.BP = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 2);
        _memory.Mmu.CheckAccess(_state.SS, offset, 2, SegmentAccessKind.Stack); offset = (ushort)(offset + 2); // skip SP slot
        _state.BX = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 2);
        _state.DX = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 2);
        _state.CX = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 2);
        _state.AX = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 2);
        _state.SP = offset;
    }

    /// <summary>
    /// Pops all 8 general-purpose 32-bit registers (POPAD order: EDI, ESI, EBP, skip ESP, EBX, EDX, ECX, EAX).
    /// Each slot is read individually; if a slot raises #SS, earlier register assignments persist
    /// while SP is left at its original value (matches 80386 partial-pop fault semantics).
    /// The ESP slot's upper 16 bits are preserved while the lower 16 bits come from the final SP.
    /// </summary>
    public void PopAll32() {
        ushort originalSp = _state.SP;
        ushort offset = originalSp;
        _state.EDI = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 4);
        _state.ESI = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 4);
        _state.EBP = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 4);
        uint espSlot = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 4);
        _state.EBX = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 4);
        _state.EDX = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 4);
        _state.ECX = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 4);
        _state.EAX = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = (ushort)(offset + 4);
        // ESP: preserve high word from the slot, low word is the new SP
        _state.ESP = (espSlot & 0xFFFF0000u) | offset;
    }
    
    /// <summary>
    /// Peeks a SegmentedAddress value from the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    /// <returns>The value in memory.</returns>
    public SegmentedAddress PeekSegmentedAddress(int index) {
        ushort offset = (ushort)(_state.SP + index);
        return _memory.SegmentedAddress16[_state.SS, offset, SegmentAccessKind.Stack];
    }

    /// <summary>
    /// Pokes a SegmentedAddress value on the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    /// <param name="value">The value to store in memory.</param>
    public void PokeSegmentedAddress(int index, SegmentedAddress value) {
        ushort offset = (ushort)(_state.SP + index);
        ValidateStackAccess(offset, 4);
        _memory.WriteUInt16Segmented(_state.SS, offset, value.Offset);
        _memory.WriteUInt16Segmented(_state.SS, (ushort)(offset + 2), value.Segment);
    }

    /// <summary>
    /// Pops a SegmentedAddress value from the stack
    /// </summary>
    /// <returns>The value retrieved from the stack, therefore read from memory</returns>
    public SegmentedAddress PopSegmentedAddress() {
        SegmentedAddress res = _memory.SegmentedAddress16[_state.SS, _state.SP, SegmentAccessKind.Stack];
        _state.SP = (ushort)(_state.SP + 4);
        return res;
    }
    
    /// <summary>
    /// Pops a SegmentedAddress32 value from the stack.
    /// The indexer performs two separate 4-byte MMU checks matching hardware's per-pop semantics.
    /// </summary>
    /// <returns>The value retrieved from the stack, therefore read from memory</returns>
    public SegmentedAddress32 PopSegmentedAddress32() {
        SegmentedAddress32 res = _memory.SegmentedAddress32[_state.SS, _state.SP, SegmentAccessKind.Stack];
        _state.SP = (ushort)(_state.SP + 8);
        return res;
    }

    /// <summary>
    /// Pops the padded 32-bit interrupt return pointer from the stack.
    /// Reads 6 bytes for the return address then discards 2 bytes of padding.
    /// </summary>
    public SegmentedAddress PopInterruptPointer32() {
        return PopSegmentedAddress32().ToSegmentedAddress();
    }

    /// <summary>
    /// Pushes a SegmentedAddress value on the stack
    /// </summary>
    /// <param name="value">The value pushed onto the stack, therefore stored in memory.</param>
    public void PushSegmentedAddress(SegmentedAddress value) {
        ushort newSp = (ushort)(_state.SP - 4);
        ValidateStackAccess(newSp, 4);
        _memory.WriteUInt16Segmented(_state.SS, newSp, value.Offset);
        _memory.WriteUInt16Segmented(_state.SS, (ushort)(newSp + 2), value.Segment);
        _state.SP = newSp;
    }

    /// <summary>
    /// Pushes a 32-bit far pointer (4-byte offset and 2-byte segment) on the stack.
    /// </summary>
    /// <param name="value">The 32-bit segmented address to push.</param>
    public void PushFarPointer32(SegmentedAddress32 value) {
        ushort newSp = (ushort)(_state.SP - 8);
        ValidateStackAccess(newSp, 8);
        _memory.WriteUInt32Segmented(_state.SS, newSp, value.Offset);
        _memory.WriteUInt16Segmented(_state.SS, (ushort)(newSp + 4), value.Segment);
        _memory.WriteUInt16Segmented(_state.SS, (ushort)(newSp + 6), 0);
        _state.SP = newSp;
    }

    private void ValidateStackAccess(ushort offset, uint accessSizeBytes) {
        _memory.Mmu.CheckAccess(_state.SS, offset, accessSizeBytes, SegmentAccessKind.Stack);
    }

    /// <summary>
    /// Pops a number of bytes from the stack (that is, increment the stack pointer), without returning any value
    /// </summary>
    /// <param name="numberOfBytesToPop">The number of bytes to pop. The Stack Pointer Register will be incremented by this value</param>
    public void Discard(int numberOfBytesToPop) {
        _state.SP = (ushort)(numberOfBytesToPop + _state.SP);
    }

    /// <summary>
    /// Sets the flag on the interrupt stack, which is at SS:SP+4 <br/>
    /// The interrupt stack is a special stack used to store the state of the processor when an interrupt occurs.<br/>
    /// In Real Mode, the CPU pushes FLAGS, CS, and IP onto the interrupt stack.<br/>
    /// </summary>
    /// <param name="flagMask">The flag mask used to modify the uint value in memory</param>
    /// <param name="flagValue">A boolean that determines whether the bits specified by the flagMask should be set (if true) or cleared (if false).</param>
    public void SetFlagOnInterruptStack(int flagMask, bool flagValue) {
        int value = Peek16(4);
        
        if (flagValue) {
            value |= flagMask;
        } else {
            value &= ~flagMask;
        }

        Poke16(4, (ushort)value);
    }

    /// <summary>
    ///    Returns a string representation of a window around the current stack address.
    /// </summary>
    /// <param name="range">How many entries to show</param>
    /// <returns>A string detailing the addresses and values on the stack around the current stack pointer</returns>
    public string PeekWindow(int range = 8) {
        var sb = new StringBuilder();
        ushort range16 = (ushort)(range << 1);
        uint physicalAddress = _state.StackPhysicalAddress;
        for (uint i = physicalAddress - range16; i < physicalAddress + range16; i += 2) {
            if (i == physicalAddress) {
                sb.Append('*');
            }
            sb.AppendLine($"[0x{i:X6}] 0x{_memory.UInt16[i]:X4}");
        }
        return sb.ToString();
    }
}