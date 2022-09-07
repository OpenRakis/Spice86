namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Memory;

public class Stack {
    private readonly Memory memory;

    private readonly State state;
    public uint PhysicalAddress => state.StackPhysicalAddress;

    public Stack(Memory memory, State state) {
        this.memory = memory;
        this.state = state;
    }

    public ushort Peek16(int index) {
        return memory.GetUint16((uint)(PhysicalAddress + index));
    }

    public void Poke16(int index, ushort value) {
        memory.SetUint16((uint)(PhysicalAddress + index), value);
    }

    public ushort Pop16() {
        ushort res = memory.GetUint16(state.StackPhysicalAddress);
        state.SP = (ushort)(state.SP + 2);
        return res;
    }

    public void Push16(ushort value) {
        state.SP = (ushort)(state.SP - 2);
        memory.SetUint16(state.StackPhysicalAddress, value);
    }
    
    public uint Peek32(int index) {
        return memory.GetUint32((uint)(PhysicalAddress + index));
    }

    public void Poke32(int index, uint value) {
        memory.SetUint32((uint)(PhysicalAddress + index), value);
    }

    public uint Pop32() {
        uint res = memory.GetUint32(state.StackPhysicalAddress);
        state.SP = (ushort)(state.SP + 4);
        return res;
    }

    public void Push32(uint value) {
        state.SP = (ushort)(state.SP - 4);
        memory.SetUint32(state.StackPhysicalAddress, value);
    }
}