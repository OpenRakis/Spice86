namespace Ix86.Emulator.Cpu;
using Ix86.Emulator.Memory;

public class Stack
{
    private Memory memory;
    private State state;
    public Stack(Memory memory, State state)
    {
        this.memory = memory;
        this.state = state;
    }

    public virtual void Push(int value)
    {
        var sp = state.GetSP() - 2;
        state.SetSP(sp);
        memory.SetUint16(state.GetStackPhysicalAddress(), value);
    }

    public virtual int Pop()
    {
        int res = memory.GetUint16(state.GetStackPhysicalAddress());
        state.SetSP(state.GetSP() + 2);
        return res;
    }

    public virtual int Peek(int index)
    {
        return memory.GetUint16(state.GetStackPhysicalAddress() + index);
    }

    public virtual void Poke(int index, int value)
    {
        memory.SetUint16(state.GetStackPhysicalAddress() + index, value);
    }
}
