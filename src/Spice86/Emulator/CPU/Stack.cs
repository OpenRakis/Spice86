namespace Spice86.Emulator.CPU;

using Spice86.Emulator.Memory;

public class Stack {
    private readonly Memory memory;

    private readonly State state;

    public Stack(Memory memory, State state) {
        this.memory = memory;
        this.state = state;
    }

    public ushort Peek(int index) {
        return memory.GetUint16((uint)(state.GetStackPhysicalAddress() + index));
    }

    public void Poke(int index, ushort value) {
        memory.SetUint16((uint)(state.GetStackPhysicalAddress() + index), value);
    }

    public ushort Pop() {
        ushort res = memory.GetUint16(state.GetStackPhysicalAddress());
        state.SetSP((ushort)(state.GetSP() + 2));
        return res;
    }

    public void Push(ushort value) {
        state.SetSP((ushort)(state.GetSP() - 2));
        memory.SetUint16(state.GetStackPhysicalAddress(), value);
    }
}