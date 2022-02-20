namespace Spice86.Emulator.InterruptHandlers;

using Spice86.Emulator.Callback;
using Spice86.Emulator.CPU;
using Spice86.Emulator.Errors;
using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;

public abstract class InterruptHandler : IndexBasedDispatcher<IRunnable>, ICallback {
    protected State _state;

    protected Cpu _cpu;

    // Protected visibility because they are used by almost all implementations
    protected Machine _machine;

    protected Memory _memory;

    protected InterruptHandler(Machine machine) {
        this._machine = machine;
        _memory = machine.GetMemory();
        _cpu = machine.GetCpu();
        _state = _cpu.GetState();
    }

    public abstract byte Index { get; }

    public abstract void Run();

    protected override UnhandledOperationException GenerateUnhandledOperationException(int index) {
        return new UnhandledInterruptException(_machine, Index, index);
    }

    protected void SetCarryFlag(bool value, bool setOnStack) {
        _state.SetCarryFlag(value);
        if (setOnStack) {
            _cpu.SetFlagOnInterruptStack(Flags.Carry, value);
        }
    }

    protected void SetZeroFlag(bool value, bool setOnStack) {
        _state.SetZeroFlag(value);
        if (setOnStack) {
            _cpu.SetFlagOnInterruptStack(Flags.Zero, value);
        }
    }
}