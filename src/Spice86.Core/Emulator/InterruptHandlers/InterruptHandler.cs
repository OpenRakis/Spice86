namespace Spice86.Core.Emulator.InterruptHandlers;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Logging;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.IndexBasedDispatcher;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Reflection;

/// <summary>
/// Base class for interrupt handlers.
/// </summary>
public abstract class InterruptHandler : IndexBasedDispatcher<IRunnable>, IInterruptHandler, IInterruptLoggerStateConsumer {
    /// <summary>
    /// Call flow handler provider
    /// </summary>
    protected readonly IFunctionHandlerProvider FunctionHandlerProvider;

    /// <summary>
    /// The memory bus.
    /// </summary>
    protected IMemory Memory;

    /// <summary>
    /// The CPU stack.
    /// </summary>
    protected Stack Stack;

    /// <summary>
    /// Indicates whether the interrupt stack is present.
    /// </summary>
    private bool _interruptStackPresent = true;
    private IEmulatorLoggerState _loggerState = new Spice86LoggerState();
    private readonly InterruptServiceDeclarations _declarations;
    private HandlerServices? _services;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    protected InterruptHandler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state, ILogger loggerService) : base(state, loggerService) {
        Memory = memory;
        FunctionHandlerProvider = functionHandlerProvider;
        Stack = stack;
        _declarations = InterruptServiceAttributes.Of(GetType());
        foreach (InterruptServiceEntry entry in _declarations.Services) {
            AddServiceAction(entry.SelectorValue, ServiceActionOf(entry.Method), entry.Contract);
        }
    }

    private Action ServiceActionOf(MethodInfo method) {
        if (method.GetParameters().Length == 0) {
            return (Action)method.CreateDelegate(typeof(Action), this);
        }
        Action<bool> action = (Action<bool>)method.CreateDelegate(typeof(Action<bool>), this);
        return () => action(true);
    }

    public void SetLoggerState(IEmulatorLoggerState loggerState) {
        _loggerState = loggerState;
    }

    /// <inheritdoc />
    public abstract byte VectorNumber { get; }

    /// <summary>Register the handler reads to select the service to run. Used only when the
    /// type declares its services with the selector form of
    /// <see cref="InterruptServiceAttribute"/>.</summary>
    protected virtual ServiceSelector ServiceSelector => ServiceSelector.Ah;

    /// <inheritdoc />
    public HandlerServices Services => _services ??= BuildServices();

    private HandlerServices BuildServices() {
        return _declarations.SingleContract is { } single
            ? new HandlerServices.SingleService(single)
            : new HandlerServices.SelectedServices(ServiceSelector, _serviceContracts);
    }

    private readonly Dictionary<int, ServiceContract> _serviceContracts = new();

    /// <summary>
    /// Runs the runnables at the given index.
    /// </summary>
    public abstract void Run();

    /// <inheritdoc />
    public virtual SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        // Default implementation for most Interrupts:
        //  - Create a callback That will call the Run method
        //  - Write that in ram with an IRET

        // Write ASM
        SegmentedAddress interruptHandlerAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.RegisterAndWriteCallback(VectorNumber, Run);
        memoryAsmWriter.WriteIret();

        return interruptHandlerAddress;
    }

    /// <summary>
    /// Stores the Action at the given index, with <see cref="ServiceContract.Unknown"/> as its contract.
    /// </summary>
    /// <param name="index">The identifier for the action. Typically, it's the value in the AH register.</param>
    /// <param name="action">The C# method that implements the function called via the interrupt.</param>
    public void AddAction(int index, Action action) {
        AddServiceAction(index, action, ServiceContract.Unknown);
    }

    /// <summary>
    /// Stores the Action at the given index along with its service contract.
    /// </summary>
    /// <param name="index">The identifier for the action. Typically, it's the value in the AH register.</param>
    /// <param name="action">The C# method that implements the function called via the interrupt.</param>
    /// <param name="contract">The declared contract of the service.</param>
    private void AddServiceAction(int index, Action action, ServiceContract contract) {
        AddRunnable(index, new RunnableAction(action));
        _serviceContracts[index] = contract;
    }

    /// <inheritdoc />
    public override void Run(int index) {
        // By default Log the CS:IP of the caller which is more useful in most situations
        SegmentedAddress? csIp = FunctionHandlerProvider.FunctionHandlerInUse.PeekReturnAddressOnMachineStack(CallType.INTERRUPT);
        _loggerState.CsIp = csIp ?? _loggerState.CsIp;
        base.Run(index);
    }

    /// <inheritdoc />
    protected override UnhandledOperationException GenerateUnhandledOperationException(int index) {
        return new UnhandledInterruptException(State, VectorNumber, index);
    }

    /// <summary>
    /// Sets the Carry Flag in the CPU state and optionally on the interrupt stack.
    /// </summary>
    /// <param name="value">The value to set for the Carry Flag.</param>
    /// <param name="setOnStack">If set to true, the Carry Flag will also be set on the interrupt stack.</param>
    protected void SetCarryFlag(bool value, bool setOnStack) {
        State.CarryFlag = value;
        if (_interruptStackPresent && setOnStack) {
            Stack.SetFlagOnInterruptStack(Flags.Carry, value);
        }
    }

    /// <summary>
    /// Sets the Zero Flag in the CPU state and optionally on the interrupt stack.
    /// </summary>
    /// <param name="value">The value to set for the Zero Flag.</param>
    /// <param name="setOnStack">If set to true, the Zero Flag will also be set on the interrupt stack.</param>
    protected void SetZeroFlag(bool value, bool setOnStack) {
        State.ZeroFlag = value;
        if (_interruptStackPresent && setOnStack) {
            Stack.SetFlagOnInterruptStack(Flags.Zero, value);
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
