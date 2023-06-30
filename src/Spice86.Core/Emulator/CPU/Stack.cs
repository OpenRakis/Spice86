namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Memory;

public class Stack {
    private readonly IMemory _memory;

    private readonly State _state;
    public uint PhysicalAddress => _state.StackPhysicalAddress;

    public Stack(IMemory memory, State state) {
        this._memory = memory;
        this._state = state;
    }

    public ushort Peek16(int index) {
        return _memory.GetUint16((uint)(PhysicalAddress + index));
    }

    public void Poke16(int index, ushort value) {
        _memory.SetUint16((uint)(PhysicalAddress + index), value);
    }

    public ushort Pop16() {
        ushort res = _memory.GetUint16(_state.StackPhysicalAddress);
        _state.SP = (ushort)(_state.SP + 2);
        return res;
    }

    public void Push16(ushort value) {
        _state.SP = (ushort)(_state.SP - 2);
        _memory.SetUint16(_state.StackPhysicalAddress, value);
    }
    
    public uint Peek32(int index) {
        return _memory.GetUint32((uint)(PhysicalAddress + index));
    }

    public void Poke32(int index, uint value) {
        _memory.SetUint32((uint)(PhysicalAddress + index), value);
    }

    public uint Pop32() {
        uint res = _memory.GetUint32(_state.StackPhysicalAddress);
        _state.SP = (ushort)(_state.SP + 4);
        return res;
    }

    public void Push32(uint value) {
        _state.SP = (ushort)(_state.SP - 4);
        _memory.SetUint32(_state.StackPhysicalAddress, value);
    }
}