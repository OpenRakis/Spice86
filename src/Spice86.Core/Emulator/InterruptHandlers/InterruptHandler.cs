namespace Spice86.Core.Emulator.InterruptHandlers;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Base class for interrupt handlers.
/// </summary>
public abstract class InterruptHandler : IndexBasedDispatcher, ICallback {
    /// <summary>
    /// The emulator state.
    /// </summary>
    protected readonly State _state;

    /// <summary>
    /// The emulator CPU.
    /// </summary>
    protected readonly Cpu _cpu;

    /// <summary>
    /// The emulator machine.
    /// </summary>
    /// <remarks>
    /// Protected visibility because it is used by almost all implementations.
    /// </remarks>
    protected readonly Machine _machine;

    /// <summary>
    /// The memory bus.
    /// </summary>
    protected Memory _memory;

    /// <summary>
    /// Indicates whether the interrupt stack is present.
    /// </summary>
    private bool _interruptStackPresent = true;

    /// <summary>
    /// Constructs a new instance of the InterruptHandler class.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    protected InterruptHandler(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        _machine = machine;
        _memory = machine.Memory;
        _cpu = machine.Cpu;
        _state = _cpu.State;
    }

    /// <inheritdoc />
    public abstract byte Index { get; }

    /// <inheritdoc />
    public abstract void Run();

    /// <inheritdoc />
    public virtual ushort? InterruptHandlerSegment => null;

    /// <inheritdoc />
    protected override UnhandledOperationException GenerateUnhandledOperationException(int index) {
        return new UnhandledInterruptException(_machine, Index, index);
    }
    
    /// <summary>
    /// Sets the Carry Flag in the CPU state and optionally on the interrupt stack.
    /// </summary>
    /// <param name="value">The value to set for the Carry Flag.</param>
    /// <param name="setOnStack">If set to true, the Carry Flag will also be set on the interrupt stack.</param>
    protected void SetCarryFlag(bool value, bool setOnStack) {
        _state.CarryFlag = value;
        if (_interruptStackPresent && setOnStack) {
            _cpu.SetFlagOnInterruptStack(Flags.Carry, value);
        }
    }

    /// <summary>
    /// Sets the Zero Flag in the CPU state and optionally on the interrupt stack.
    /// </summary>
    /// <param name="value">The value to set for the Zero Flag.</param>
    /// <param name="setOnStack">If set to true, the Zero Flag will also be set on the interrupt stack.</param>
    protected void SetZeroFlag(bool value, bool setOnStack) {
        _state.ZeroFlag = value;
        if (_interruptStackPresent && setOnStack) {
            _cpu.SetFlagOnInterruptStack(Flags.Zero, value);
        }
    }
    
    /// <inheritdoc />
    public void RunFromOverriden() {
        // When running from overriden code, this is a direct C# code so there is no stack to edit.
        _interruptStackPresent = false;
        Run();
        _interruptStackPresent = true;
    }
}