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

    public ushort Peek(int index) {
        return memory.GetUint16((uint)(PhysicalAddress + index));
    }

    public void Poke(int index, ushort value) {
        memory.SetUint16((uint)(PhysicalAddress + index), value);
    }

    public ushort Pop() {
        ushort res = memory.GetUint16(state.StackPhysicalAddress);
        state.SP = (ushort)(state.SP + 2);
        return res;
    }

    public void Push(ushort value) {
        state.SP = (ushort)(state.SP - 2);
        memory.SetUint16(state.StackPhysicalAddress, value);
    }
}