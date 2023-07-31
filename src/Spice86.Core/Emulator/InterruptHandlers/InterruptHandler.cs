namespace Spice86.Core.Emulator.InterruptHandlers;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.IndexBasedDispatcher;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Base class for interrupt handlers.
/// </summary>
public abstract class InterruptHandler : IndexBasedDispatcher<IRunnable>, IInterruptHandler {
    /// <summary>
    /// The CPU state.
    /// </summary>
    protected readonly State _state;

    /// <summary>
    /// The emulator CPU.
    /// </summary>
    protected readonly Cpu _cpu;

    /// <summary>
    /// The memory bus.
    /// </summary>
    protected IMemory _memory;

    /// <summary>
    /// Indicates whether the interrupt stack is present.
    /// </summary>
    private bool _interruptStackPresent = true;

    /// <summary>
    /// Constructs a new instance of the InterruptHandler class.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    protected InterruptHandler(IMemory memory, Cpu cpu, ILoggerService loggerService) : base(cpu.State, loggerService) {
        _memory = memory;
        _cpu = cpu;
        _state = cpu.State;
    }

    /// <inheritdoc />
    public abstract byte VectorNumber { get; }

    public abstract void Run();

    /// <inheritdoc />
    public virtual SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        // Default implementation for most Interrupts:
        //  - Create a callback That will call the Run method
        //  - Write that in ram with an IRET
        
        // Write ASM
        SegmentedAddress interruptHandlerAddress = memoryAsmWriter.GetCurrentAddressCopy();
        memoryAsmWriter.RegisterAndWriteCallback(VectorNumber, Run);
        memoryAsmWriter.WriteIret();

        return interruptHandlerAddress;
    }

    /// <summary>
    /// Stores the Action at the given index
    /// </summary>
    /// <param name="index"></param>
    /// <param name="action"></param>
    public void AddAction(int index, Action action) {
        AddRunnable(index, new RunnableAction(action));
    }

    /// <inheritdoc />
    public override void Run(int index) {
        // By default Log the CS:IP of the caller which is more useful in most situations
        SegmentedAddress? csIp = _cpu.FunctionHandlerInUse.PeekReturnAddressOnMachineStack(CallType.INTERRUPT);
        _loggerService.LoggerPropertyBag.CsIp = csIp ?? _loggerService.LoggerPropertyBag.CsIp;
        base.Run(index);
    }

    /// <inheritdoc />
    protected override UnhandledOperationException GenerateUnhandledOperationException(int index) {
        return new UnhandledInterruptException(_state, VectorNumber, index);
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
    
    /// <summary>
    /// Runs the C# code that replaces the machine code. <br/>
    /// While the C# code is run, the interrupt stack is disabled.
    /// </summary>
    public void RunFromOverriden() {
        // When running from overriden code, this is a direct C# code so there is no stack to edit.
        _interruptStackPresent = false;
        Run();
        _interruptStackPresent = true;
    }
}