namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
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
/// <b>SP vs ESP:</b> the address computation uses SS:ESP (32-bit) when the SS descriptor's D/B bit is
/// set, and SS:SP (16-bit, matching real mode) otherwise - <see cref="StackPointer"/> is the single
/// point that decides this for every method below. Note that the operand size (16-bit vs 32-bit values
/// pushed or popped) is entirely independent of the address size: <see cref="Push32"/> and
/// <see cref="Pop32"/> correctly store 32-bit values regardless of which stack address width is active.
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

    /// <summary>Whether SS's descriptor has the D/B bit set, making ESP (not SP) the authoritative stack address.</summary>
    private bool StackAddressIs32Bit => _state.SegmentDescriptorCaches[SegmentRegisterIndex.SsIndex].DefaultBig;

    /// <summary>
    /// The authoritative stack pointer: <see cref="State.ESP"/> when SS is a 32-bit-default segment,
    /// <see cref="State.SP"/> (zero-extended) otherwise. Every push/pop/peek/poke method below reads and
    /// writes through this single property so the SP/ESP choice is made in exactly one place.
    /// </summary>
    private uint StackPointer {
        get => StackAddressIs32Bit ? _state.ESP : _state.SP;
        set {
            if (StackAddressIs32Bit) {
                _state.ESP = value;
            } else {
                _state.SP = (ushort)value;
            }
        }
    }

    /// <summary>
    /// Wraps an address computation to the current stack address width: full 32-bit range when SS is
    /// 32-bit-default, or 16-bit (matching real hardware SP register wraparound) otherwise.
    /// </summary>
    private uint MaskAddress(uint value) => StackAddressIs32Bit ? value : (ushort)value;

    /// <summary>
    /// Computes <see cref="StackPointer"/> + <paramref name="delta"/>, wrapped to the current stack
    /// address width. <paramref name="delta"/> may be negative.
    /// </summary>
    private uint OffsetStackPointer(int delta) => MaskAddress(unchecked((uint)((int)StackPointer + delta)));

    /// <summary>
    /// Peeks a 8 bit value from the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    /// <returns>The value in memory.</returns>
    public byte Peek8(int index) {
        uint offset = OffsetStackPointer(index);
        return _memory.UInt8[_state.SS, offset, SegmentAccessKind.Stack];
    }

    /// <summary>
    /// Peeks a 16 bit value from the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    /// <returns>The value in memory.</returns>
    public ushort Peek16(int index) {
        uint offset = OffsetStackPointer(index);
        return _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack];
    }

    /// <summary>
    /// Pokes a 16 bit value on the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    /// <param name="value">The value to store in memory.</param>
    public void Poke16(int index, ushort value) {
        uint offset = OffsetStackPointer(index);
        _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack] = value;
    }

    /// <summary>
    /// Pops a 16 bit value from the stack
    /// </summary>
    /// <returns>The value retrieved from the stack, therefore read from memory</returns>
    public ushort Pop16() {
        ushort res = _memory.UInt16[_state.SS, StackPointer, SegmentAccessKind.Stack];
        StackPointer = OffsetStackPointer(2);
        return res;
    }

    /// <summary>
    /// Pushes a 16 bit value on the stack
    /// </summary>
    /// <param name="value">The value pushed onto the stack, therefore stored in memory.</param>
    public void Push16(ushort value) {
        uint newSp = OffsetStackPointer(-2);
        _memory.UInt16[_state.SS, newSp, SegmentAccessKind.Stack] = value;
        StackPointer = newSp;
    }

    /// <summary>
    /// Peeks a 32 bit value from the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    public uint Peek32(int index) {
        uint offset = OffsetStackPointer(index);
        return _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack];
    }

    /// <summary>
    /// Pokes a 32 bit value on the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    /// <param name="value">The value to store in memory.</param>
    public void Poke32(int index, uint value) {
        uint offset = OffsetStackPointer(index);
        _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack] = value;
    }

    /// <summary>
    /// Pops a 32 bit value from the stack
    /// </summary>
    /// <returns>The value popped from the stack.</returns>
    public uint Pop32() {
        uint res = _memory.UInt32[_state.SS, StackPointer, SegmentAccessKind.Stack];
        StackPointer = OffsetStackPointer(4);
        return res;
    }

    /// <summary>
    /// Pops a 16-bit segment selector from a stack slot of the given width: reads only the low 16 bits
    /// of the slot (matching real hardware's POP Sreg, which discards any upper bits in a 32-bit slot)
    /// but advances the stack pointer by the full slot width.
    /// </summary>
    /// <param name="slotSizeBytes">The slot width in bytes: 2 for a 16-bit operand-size POP, 4 for 32-bit.</param>
    public ushort PopSegmentSelector(int slotSizeBytes) {
        ushort value = _memory.UInt16[_state.SS, StackPointer, SegmentAccessKind.Stack];
        StackPointer = OffsetStackPointer(slotSizeBytes);
        return value;
    }

    /// <summary>
    /// Performs LEAVE: releases the current stack frame by setting the stack pointer to the frame
    /// pointer's value, then popping the caller's saved frame pointer back off the stack.
    /// Two independent axes control this instruction:
    /// - The ADDRESS used to locate the saved frame pointer (and the resulting new stack pointer)
    ///   follows the stack's own address width (SS's D/B bit via <see cref="StackAddressIs32Bit"/>):
    ///   EBP/ESP when the stack is 32-bit-default, BP/SP otherwise - resolved fresh every call since SS
    ///   can differ between calls to the same code address.
    /// - The VALUE popped back into the frame-pointer register, and the WIDTH of that register write,
    ///   follow the instruction's operand size (<paramref name="operandSize32"/>, safe to fix at parse
    ///   time since it comes from CS): a 16-bit-operand LEAVE only ever writes BP (leaving EBP's upper
    ///   half untouched), even when the stack itself is 32-bit - matching a plain POP's semantics.
    /// The saved frame pointer is read before the stack pointer is committed, so a fault reading it
    /// leaves the stack pointer unchanged (matching real-80386 fault atomicity).
    /// </summary>
    public void Leave(bool operandSize32) {
        uint frameAddress = StackAddressIs32Bit ? _state.EBP : _state.BP;
        uint poppedValue = ReadFrameValue(frameAddress, operandSize32);
        int pointerSize = operandSize32 ? 4 : 2;
        StackPointer = MaskAddress(frameAddress + (uint)pointerSize);
        if (operandSize32) {
            _state.EBP = poppedValue;
        } else {
            _state.BP = (ushort)poppedValue;
        }
    }

    /// <summary>
    /// Pushes a 32 bit value on the stack
    /// </summary>
    /// <param name="value">The value to store onto the stack.</param>
    public void Push32(uint value) {
        uint newSp = OffsetStackPointer(-4);
        _memory.UInt32[_state.SS, newSp, SegmentAccessKind.Stack] = value;
        StackPointer = newSp;
    }

    /// <summary>
    /// Pre-validates that all slots for a multi-register push (PUSHA/PUSHAD) are accessible.
    /// Checks each slot going downward from the current stack pointer. Raises #SS if any slot crosses
    /// the segment limit. No state is modified if the check fails.
    /// </summary>
    /// <param name="valueSizeBytes">Size of each value in bytes (2 for 16-bit, 4 for 32-bit).</param>
    /// <param name="valueCount">Number of values to push.</param>
    public void ValidateStackPushRange(ushort valueSizeBytes, ushort valueCount) {
        uint offset = StackPointer;
        for (ushort i = 0; i < valueCount; i++) {
            offset = MaskAddress(offset - valueSizeBytes);
            _memory.Mmu.CheckAccess(_state.SS, offset, valueSizeBytes, SegmentAccessKind.Stack, isWrite: true);
        }
    }

    /// <summary>
    /// Pre-validates that all slots for a multi-register pop (POPA/POPAD) are accessible.
    /// Checks each slot going upward from the current stack pointer. Raises #SS if any slot crosses the
    /// segment limit. No state is modified if the check fails.
    /// </summary>
    /// <param name="valueSizeBytes">Size of each value in bytes (2 for 16-bit, 4 for 32-bit).</param>
    /// <param name="valueCount">Number of values to pop.</param>
    public void ValidateStackPopRange(ushort valueSizeBytes, ushort valueCount) {
        uint offset = StackPointer;
        for (ushort i = 0; i < valueCount; i++) {
            _memory.Mmu.CheckAccess(_state.SS, offset, valueSizeBytes, SegmentAccessKind.Stack, isWrite: false);
            offset = MaskAddress(offset + valueSizeBytes);
        }
    }

    /// <summary>
    /// Pushes all 8 general-purpose 16-bit registers (PUSHA order: AX, CX, DX, BX, SP, BP, SI, DI).
    /// The range is validated up front, all eight slots are written, and the #SS (if any slot crossed
    /// the segment limit) is raised only afterwards - matching real-80386 PUSHAD, which stores every
    /// register before reporting the fault.
    /// </summary>
    public void PushAll16(ushort ax, ushort cx, ushort dx, ushort bx, ushort sp, ushort bp, ushort si, ushort di) {
        CpuStackSegmentFaultException? pendingFault = GetStackPushRangeFault(2, 8);
        ushort offset = (ushort)StackPointer;
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, ax);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, cx);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, dx);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, bx);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, sp);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, bp);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, si);
        offset = (ushort)(offset - 2); _memory.WriteUInt16Segmented(_state.SS, offset, di);
        if (pendingFault is not null) {
            throw pendingFault;
        }
        StackPointer = offset;
    }

    /// <summary>
    /// Pushes all 8 general-purpose 32-bit registers (PUSHAD order: EAX, ECX, EDX, EBX, ESP, EBP, ESI, EDI).
    /// The range is validated up front, all eight slots are written, and the #SS (if any slot crossed
    /// the segment limit) is raised only afterwards - matching real-80386 PUSHAD, which stores every
    /// register before reporting the fault.
    /// </summary>
    public void PushAll32(uint eax, uint ecx, uint edx, uint ebx, uint esp, uint ebp, uint esi, uint edi) {
        CpuStackSegmentFaultException? pendingFault = GetStackPushRangeFault(4, 8);
        ushort offset = (ushort)StackPointer;
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, eax);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, ecx);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, edx);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, ebx);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, esp);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, ebp);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, esi);
        offset = (ushort)(offset - 4); _memory.WriteUInt32Segmented(_state.SS, offset, edi);
        if (pendingFault is not null) {
            throw pendingFault;
        }
        StackPointer = offset;
    }

    /// <summary>
    /// Walks the push slots going downward from the current stack pointer and captures the first #SS
    /// any slot raises, without throwing. Returns null when the whole range is accessible so the caller
    /// can complete its writes before re-raising the captured fault (deferred-fault PUSHAD semantics).
    /// </summary>
    /// <param name="valueSizeBytes">Size of each value in bytes (2 for 16-bit, 4 for 32-bit).</param>
    /// <param name="valueCount">Number of values to push.</param>
    /// <returns>The first stack-segment fault encountered, or null if all slots are valid.</returns>
    private CpuStackSegmentFaultException? GetStackPushRangeFault(ushort valueSizeBytes, ushort valueCount) {
        uint offset = StackPointer;
        for (ushort i = 0; i < valueCount; i++) {
            offset = MaskAddress(offset - valueSizeBytes);
            try {
                _memory.Mmu.CheckAccess(_state.SS, offset, valueSizeBytes, SegmentAccessKind.Stack, isWrite: true);
            } catch (CpuStackSegmentFaultException exception) {
                return exception;
            }
        }
        return null;
    }

    /// <summary>
    /// Pops all 8 general-purpose 16-bit registers (POPA order: DI, SI, BP, skip SP, BX, DX, CX, AX).
    /// Each slot is read individually; if a slot raises #SS, earlier register assignments persist
    /// while the stack pointer is left at its original value (matches 80386 partial-pop fault semantics).
    /// </summary>
    public void PopAll16() {
        uint offset = StackPointer;
        _state.DI = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 2);
        _state.SI = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 2);
        _state.BP = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 2);
        _memory.Mmu.CheckAccess(_state.SS, offset, 2, SegmentAccessKind.Stack, isWrite: false); offset = MaskAddress(offset + 2); // skip SP slot
        _state.BX = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 2);
        _state.DX = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 2);
        _state.CX = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 2);
        _state.AX = _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 2);
        StackPointer = offset;
    }

    /// <summary>
    /// Pops all 8 general-purpose 32-bit registers (POPAD order: EDI, ESI, EBP, skip ESP, EBX, EDX, ECX, EAX).
    /// Each slot is read individually; if a slot raises #SS, earlier register assignments persist
    /// while the stack pointer is left at its original value (matches 80386 partial-pop fault semantics).
    /// The ESP slot is advanced past without being popped into the register, but its upper 16 bits are
    /// folded back into ESP (matching real hardware: POPAD never changes the high word of ESP, only the
    /// low word advances past the 8 slots).
    /// </summary>
    public void PopAll32() {
        uint offset = StackPointer;
        _state.EDI = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 4);
        _state.ESI = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 4);
        _state.EBP = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 4);
        uint espSlot = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 4);
        _state.EBX = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 4);
        _state.EDX = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 4);
        _state.ECX = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 4);
        _state.EAX = _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack]; offset = MaskAddress(offset + 4);
        _state.ESP = (espSlot & 0xFFFF0000u) | offset;
    }

    /// <summary>
    /// Peeks a SegmentedAddress value from the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    /// <returns>The value in memory.</returns>
    public SegmentedAddress PeekSegmentedAddress(int index) {
        uint offset = OffsetStackPointer(index);
        return _memory.SegmentedAddress16[_state.SS, offset, SegmentAccessKind.Stack];
    }

    /// <summary>
    /// Pokes a SegmentedAddress value on the stack
    /// </summary>
    /// <param name="index">The offset from the stack top</param>
    /// <param name="value">The value to store in memory.</param>
    public void PokeSegmentedAddress(int index, SegmentedAddress value) {
        uint offset = OffsetStackPointer(index);
        ValidateStackAccess(offset, 4);
        _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack] = value.Offset;
        _memory.UInt16[_state.SS, MaskAddress(offset + 2), SegmentAccessKind.Stack] = value.Segment;
    }

    /// <summary>
    /// Pops a SegmentedAddress value from the stack
    /// </summary>
    /// <returns>The value retrieved from the stack, therefore read from memory</returns>
    public SegmentedAddress PopSegmentedAddress() {
        SegmentedAddress res = _memory.SegmentedAddress16[_state.SS, StackPointer, SegmentAccessKind.Stack];
        StackPointer = OffsetStackPointer(4);
        return res;
    }

    /// <summary>
    /// Pops a SegmentedAddress32 value from the stack.
    /// The indexer performs two separate 4-byte MMU checks matching hardware's per-pop semantics.
    /// </summary>
    /// <returns>The value retrieved from the stack, therefore read from memory</returns>
    public SegmentedAddress32 PopSegmentedAddress32() {
        SegmentedAddress32 res = _memory.SegmentedAddress32[_state.SS, StackPointer, SegmentAccessKind.Stack];
        StackPointer = OffsetStackPointer(8);
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
        uint newSp = OffsetStackPointer(-4);
        ValidateStackAccess(newSp, 4);
        _memory.UInt16[_state.SS, newSp, SegmentAccessKind.Stack] = value.Offset;
        _memory.UInt16[_state.SS, MaskAddress(newSp + 2), SegmentAccessKind.Stack] = value.Segment;
        StackPointer = newSp;
    }

    /// <summary>
    /// Pushes a 32-bit far pointer (4-byte offset and 2-byte segment) on the stack.
    /// </summary>
    /// <param name="value">The 32-bit segmented address to push.</param>
    public void PushFarPointer32(SegmentedAddress32 value) {
        uint newSp = OffsetStackPointer(-8);
        ValidateStackAccess(newSp, 8);
        _memory.UInt32[_state.SS, newSp, SegmentAccessKind.Stack] = value.Offset;
        _memory.UInt16[_state.SS, MaskAddress(newSp + 4), SegmentAccessKind.Stack] = value.Segment;
        _memory.UInt16[_state.SS, MaskAddress(newSp + 6), SegmentAccessKind.Stack] = 0;
        StackPointer = newSp;
    }

    private void ValidateStackAccess(uint offset, uint accessSizeBytes) {
        _memory.Mmu.CheckAccess(_state.SS, offset, accessSizeBytes, SegmentAccessKind.Stack, isWrite: true);
    }

    /// <summary>
    /// Pops a number of bytes from the stack (that is, increment the stack pointer), without returning any value
    /// </summary>
    /// <param name="numberOfBytesToPop">The number of bytes to pop. The Stack Pointer Register will be incremented by this value</param>
    public void Discard(int numberOfBytesToPop) {
        StackPointer = OffsetStackPointer(numberOfBytesToPop);
    }

    /// <summary>
    /// ENTER: creates a nested stack frame. Pushes the current frame pointer, then - for a nesting
    /// level above 0 - copies <paramref name="level"/>-1 additional frame pointers from the enclosing
    /// frames followed by the new frame pointer itself, before allocating <paramref name="storageSize"/>
    /// bytes of dynamic storage.
    /// Two independent axes control this instruction, and must not be conflated:
    /// - The stack's own address width (SS's D/B bit, via <see cref="StackAddressIs32Bit"/>) governs
    ///   how the frame-pointer CHAIN-WALK addresses are computed/wrapped (BP-based 16-bit addressing
    ///   vs EBP-based 32-bit addressing), and how the new frame pointer value itself is formed: when
    ///   the stack is 16-bit, the eventual BP writeback only ever touches BP's 16 bits on real hardware,
    ///   so the untouched upper half of EBP must be folded back into the pushed/stored frame-pointer
    ///   value everywhere it is used (chain copies and the register writeback alike) - resolved fresh
    ///   every call since SS can differ between calls to the same code address.
    /// - The instruction's operand size (<paramref name="operandSize32"/>, safe to fix at parse time
    ///   since it comes from CS) governs only the WIDTH of the data pushed/copied on the stack (2 vs 4
    ///   bytes) - independent of the stack's address width.
    /// The stack pointer and frame-pointer register are committed only after the storage-allocation
    /// validation succeeds, so a fault leaves both unchanged (matching real hardware's atomic fault semantics).
    /// </summary>
    public void Enter(ushort storageSize, byte level, bool operandSize32) {
        level = (byte)(level & 0x1F);
        int pointerSize = operandSize32 ? 4 : 2;

        uint oldBaseValue = operandSize32 ? _state.EBP : _state.BP;
        uint newSp = OffsetStackPointer(-pointerSize);
        WriteFrameValue(newSp, oldBaseValue, operandSize32);

        // The frame-pointer register writeback width follows the stack's own address width: in real
        // (16-bit-default) mode ENTER writes the narrow 16-bit BP, zeroing EBP's upper half (matching
        // real hardware, where only BP's 16 bits are affected); in 32-bit mode it writes the full EBP.
        // The value stored on the stack (and used for chain copies) is always the new stack address.
        uint newFrameAddress = newSp;

        uint sp = newSp;
        if (level > 0) {
            uint chainAddress = StackAddressIs32Bit ? _state.EBP : _state.BP;
            for (int i = 1; i < level; i++) {
                chainAddress = MaskAddress(chainAddress - (uint)pointerSize);
                sp = OffsetStackPointerFrom(sp, -pointerSize);
                WriteFrameValue(sp, ReadFrameValue(chainAddress, operandSize32), operandSize32);
            }
            sp = OffsetStackPointerFrom(sp, -pointerSize);
            WriteFrameValue(sp, newFrameAddress, operandSize32);
        }

        // ENTER reserves storageSize bytes of dynamic storage without writing to it, but real hardware
        // still validates that a write at the FINAL stack pointer (after this reservation) would
        // succeed - raising the same #PF/#GP/#SS a later access there would, even though no data is
        // actually stored at that address by ENTER itself.
        uint finalSp = OffsetStackPointerFrom(sp, -(int)storageSize);
        _memory.Mmu.CheckAccess(_state.SS, finalSp, 1, SegmentAccessKind.Stack, isWrite: true);
        _memory.Mmu.TranslateAddress(_state.SS, finalSp, isWrite: true);

        StackPointer = finalSp;
        if (StackAddressIs32Bit) {
            _state.EBP = newFrameAddress;
        } else {
            _state.BP = (ushort)newFrameAddress;
        }
    }

    /// <summary>
    /// Computes a stack pointer offset from an explicit base value (rather than the live
    /// <see cref="StackPointer"/>), wrapped to the current stack address width.
    /// </summary>
    private uint OffsetStackPointerFrom(uint baseValue, int delta) => MaskAddress(unchecked((uint)((int)baseValue + delta)));

    private uint ReadFrameValue(uint offset, bool operandSize32) {
        return operandSize32 ? _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack] : _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack];
    }

    private void WriteFrameValue(uint offset, uint value, bool operandSize32) {
        if (operandSize32) {
            _memory.UInt32[_state.SS, offset, SegmentAccessKind.Stack] = value;
        } else {
            _memory.UInt16[_state.SS, offset, SegmentAccessKind.Stack] = (ushort)value;
        }
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