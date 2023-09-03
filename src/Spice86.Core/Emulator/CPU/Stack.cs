namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

public class Stack {
    private readonly IMemory _memory;

    private readonly ICpuState _state;
    public uint PhysicalAddress => _state.StackPhysicalAddress;

    public Stack(IMemory memory, ICpuState state) {
        this._memory = memory;
        this._state = state;
    }

    public ushort Peek16(int index) {
        return _memory.UInt16[(uint)(PhysicalAddress + index)];
    }

    public void Poke16(int index, ushort value) {
        _memory.UInt16[(uint)(PhysicalAddress + index)] = value;
    }

    public ushort Pop16() {
        ushort res = _memory.UInt16[_state.StackPhysicalAddress];
        _state.SP = (ushort)(_state.SP + 2);
        return res;
    }

    public void Push16(ushort value) {
        _state.SP = (ushort)(_state.SP - 2);
        _memory.UInt16[_state.StackPhysicalAddress] = value;
    }
    
    public uint Peek32(int index) {
        return _memory.UInt32[(uint)(PhysicalAddress + index)];
    }

    public void Poke32(int index, uint value) {
        _memory.UInt32[(uint)(PhysicalAddress + index)] = value;
    }

    public uint Pop32() {
        uint res = _memory.UInt32[_state.StackPhysicalAddress];
        _state.SP = (ushort)(_state.SP + 4);
        return res;
    }

    public void Push32(uint value) {
        _state.SP = (ushort)(_state.SP - 4);
        _memory.UInt32[_state.StackPhysicalAddress] = value;
    }
    
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
}