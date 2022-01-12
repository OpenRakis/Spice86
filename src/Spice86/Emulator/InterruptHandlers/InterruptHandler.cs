namespace Spice86.Emulator.InterruptHandlers;

using Spice86.Emulator.Callback;
using Spice86.Emulator.Cpu;
using Spice86.Emulator.Errors;
using Spice86.Emulator.Machine;
using Spice86.Emulator.Memory;

public abstract class InterruptHandler : IndexBasedDispatcher<IRunnable>, ICallback
{
    // Protected visibility because they are used by almost all implementations
    protected Machine machine;

    protected Memory memory;
    protected Cpu cpu;
    protected State _state;

    protected InterruptHandler(Machine machine)
    {
        this.machine = machine;
        memory = machine.GetMemory();
        cpu = machine.GetCpu();
        _state = cpu.GetState();
    }

    public abstract void Run();

    public abstract int GetIndex();

    protected override UnhandledOperationException GenerateUnhandledOperationException(int index)
    {
        return new UnhandledInterruptException(machine, GetIndex(), index);
    }

    protected void SetCarryFlag(bool value, bool setOnStack)
    {
        _state.SetCarryFlag(value);
        if (setOnStack)
        {
            cpu.SetFlagOnInterruptStack(Flags.Carry, value);
        }
    }

    protected void SetZeroFlag(bool value, bool setOnStack)
    {
        _state.SetZeroFlag(value);
        if (setOnStack)
        {
            cpu.SetFlagOnInterruptStack(Flags.Zero, value);
        }
    }
}