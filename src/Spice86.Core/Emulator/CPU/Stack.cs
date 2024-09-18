namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

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
/// </summary>
public class Stack {
    private readonly IMemory _memory;

    private readonly State _state;

    /// <summary>
    /// The physical address of the stack in memory
    /// </summary>
    public uint PhysicalAddress => _state.StackPhysicalAddress;

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
    /// <param name="index">The offset from the <see cref="PhysicalAddress"/></param>
    /// <returns>The value in memory.</returns>
    public byte Peek8(int index) {
        return _memory.UInt8[(uint)(PhysicalAddress + index)];
    }

    /// <summary>
    /// Peeks a 16 bit value from the stack
    /// </summary>
    /// <param name="index">The offset from the <see cref="PhysicalAddress"/></param>
    /// <returns>The value in memory.</returns>
    public ushort Peek16(int index) {
        return _memory.UInt16[(uint)(PhysicalAddress + index)];
    }

    /// <summary>
    /// Pokes a 16 bit value on the stack
    /// </summary>
    /// <param name="index">The offset from the <see cref="PhysicalAddress"/></param>
    /// <param name="value">The value to store in memory.</param>
    public void Poke16(int index, ushort value) {
        _memory.UInt16[(uint)(PhysicalAddress + index)] = value;
    }

    /// <summary>
    /// Pops a 16 bit value from the stack
    /// </summary>
    /// <returns>The value retrieved from the stack, therefore read from memory</returns>
    public ushort Pop16() {
        ushort res = _memory.UInt16[PhysicalAddress];
        _state.SP = (ushort)(_state.SP + 2);
        return res;
    }

    /// <summary>
    /// Pushes a 16 bit value on the stack
    /// </summary>
    /// <param name="value">The value pushed onto the stack, therefore stored in memory.</param>
    public void Push16(ushort value) {
        _state.SP = (ushort)(_state.SP - 2);
        _memory.UInt16[PhysicalAddress] = value;
    }

    /// <summary>
    /// Peeks a 32 bit value from the stack
    /// </summary>
    /// <param name="index">The offset from the <see cref="PhysicalAddress"/></param>
    public uint Peek32(int index) {
        return _memory.UInt32[(uint)(PhysicalAddress + index)];
    }

    /// <summary>
    /// Pokes a 32 bit value on the stack
    /// </summary>
    /// <param name="index">The offset from the <see cref="PhysicalAddress"/></param>
    /// <param name="value">The value to store in memory.</param>
    public void Poke32(int index, uint value) {
        _memory.UInt32[(uint)(PhysicalAddress + index)] = value;
    }

    /// <summary>
    /// Pops a 32 bit value from the stack
    /// </summary>
    /// <returns>The value popped from the stack.</returns>
    public uint Pop32() {
        uint res = _memory.UInt32[PhysicalAddress];
        _state.SP = (ushort)(_state.SP + 4);
        return res;
    }

    /// <summary>
    /// Pushes a 32 bit value on the stack
    /// </summary>
    /// <param name="value">The value to store onto the stack.</param>
    public void Push32(uint value) {
        _state.SP = (ushort)(_state.SP - 4);
        _memory.UInt32[PhysicalAddress] = value;
    }
    
    /// <summary>
    /// Peeks a SegmentedAddress value from the stack
    /// </summary>
    /// <param name="index">The offset from the <see cref="PhysicalAddress"/></param>
    /// <returns>The value in memory.</returns>
    public SegmentedAddress PeekSegmentedAddress(int index) {
        return _memory.SegmentedAddress[(uint)(PhysicalAddress + index)];
    }

    /// <summary>
    /// Pokes a SegmentedAddress value on the stack
    /// </summary>
    /// <param name="index">The offset from the <see cref="PhysicalAddress"/></param>
    /// <param name="value">The value to store in memory.</param>
    public void PokeSegmentedAddress(int index, SegmentedAddress value) {
        _memory.SegmentedAddress[(uint)(PhysicalAddress + index)] = value;
    }

    /// <summary>
    /// Pops a SegmentedAddress value from the stack
    /// </summary>
    /// <returns>The value retrieved from the stack, therefore read from memory</returns>
    public SegmentedAddress PopSegmentedAddress() {
        SegmentedAddress res = _memory.SegmentedAddress[PhysicalAddress];
        _state.SP = (ushort)(_state.SP + 4);
        return res;
    }

    /// <summary>
    /// Pushes a SegmentedAddress value on the stack
    /// </summary>
    /// <param name="value">The value pushed onto the stack, therefore stored in memory.</param>
    public void PushSegmentedAddress(SegmentedAddress value) {
        _state.SP = (ushort)(_state.SP - 4);
        _memory.SegmentedAddress[PhysicalAddress] = value;
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
        uint flagsAddress = MemoryUtils.ToPhysicalAddress(_state.SS, (ushort)(_state.SP + 4));
        int value = _memory.UInt16[flagsAddress];
        if (flagValue) {
            value |= flagMask;
        } else {
            value &= ~flagMask;
        }

        _memory.UInt16[flagsAddress] = (ushort)value;
    }

    /// <summary>
    ///    Returns a string representation of a window around the current stack address.
    /// </summary>
    /// <param name="range">How many entries to show</param>
    /// <returns>A string detailing the addresses and values on the stack around the current stack pointer</returns>
    public string PeekWindow(int range = 8) {
        var sb = new StringBuilder();
        ushort range16 = (ushort)(range << 1);
        for (uint i = PhysicalAddress - range16; i < PhysicalAddress + range16; i += 2) {
            if (i == PhysicalAddress) {
                sb.Append('*');
            }
            sb.AppendLine($"[0x{i:X6}] 0x{_memory.UInt16[i]:X4}");
        }
        return sb.ToString();
    }
}