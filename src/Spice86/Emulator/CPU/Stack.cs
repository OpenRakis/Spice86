namespace Spice86.Emulator.Cpu;

using Spice86.Emulator.Memory;

public class Stack {
    private readonly Memory memory;

    private readonly State state;

    public Stack(Memory memory, State state) {
        this.memory = memory;
        this.state = state;
    }

    public ushort Peek(int index) {
        return memory.GetUint16(state.GetStackPhysicalAddress() + index);
    }

    public void Poke(int index, ushort value) {
        memory.SetUint16(state.GetStackPhysicalAddress() + index, value);
    }

    public int Pop() {
        int res = memory.GetUint16(state.GetStackPhysicalAddress());
        state.SetSP(state.GetSP() + 2);
        return res;
    }

    public void Push(int value) {
        var sp = state.GetSP() - 2;
        state.SetSP(sp);
        memory.SetUint16(state.GetStackPhysicalAddress(), (ushort)value);
    }
}