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
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System;
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

    private IEmulatorLoggerState _loggerState = new Spice86LoggerState();
    private readonly InterruptServiceDeclarations _declarations;
    private HandlerServices? _services;
    private readonly ServiceCallEvents _serviceCallEvents;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="serviceCallEvents">The service call events the handler reports its service calls to.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    protected InterruptHandler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        ServiceCallEvents serviceCallEvents, ILogger loggerService)
        : this(memory, functionHandlerProvider, stack, state, serviceCallEvents, loggerService, ServiceSelector.Ah) {
    }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="serviceCallEvents">The service call events the handler reports its service calls to.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="serviceSelector">The register used to select a service.</param>
    protected InterruptHandler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        ServiceCallEvents serviceCallEvents, ILogger loggerService, ServiceSelector serviceSelector) : base(state, loggerService) {
        Memory = memory;
        _serviceCallEvents = serviceCallEvents;
        FunctionHandlerProvider = functionHandlerProvider;
        Stack = stack;
        ServiceSelector = serviceSelector;
        _declarations = InterruptServiceAttributes.Of(GetType(), serviceSelector);
        foreach ((MethodInfo method, InterruptServiceEntry entry) in _declarations.Services) {
            AddServiceAction(entry.SelectorValue, ServiceActionOf(method), entry);
        }
    }

    private Action ServiceActionOf(MethodInfo method) {
        return (Action)method.CreateDelegate(typeof(Action), this);
    }

    public void SetLoggerState(IEmulatorLoggerState loggerState) {
        _loggerState = loggerState;
    }

    /// <inheritdoc />
    public abstract byte VectorNumber { get; }

    /// <summary>Register the handler reads to select the service to run. Used only when the
    /// type declares its services with the selector form of
    /// <see cref="InterruptServiceAttribute"/>.</summary>
    protected ServiceSelector ServiceSelector { get; }

    /// <inheritdoc />
    public HandlerServices Services => _services ??= BuildServices();

    private HandlerServices BuildServices() {
        return _declarations.SingleService is { } single
            ? new HandlerServices.SingleService(single.Entry)
            : new HandlerServices.SelectedServices(ServiceSelector, _serviceEntries);
    }

    private readonly Dictionary<int, InterruptServiceEntry> _serviceEntries = new();

    /// <summary>
    /// Runs the runnables at the given index.
    /// </summary>
    public abstract void Run();

    /// <summary>
    /// The entry bookkeeping of a service that has one: the interpreted <see cref="Run()"/> entry
    /// calls it after the interrupt pushed its frame and before the service dispatches, and the
    /// generated typed service call runs it from the same lowered SP through
    /// <see cref="CSharpOverrideHelper.ServiceCall(ushort, ushort, InterruptHandler, Action)"/>.
    /// The caller decides whether the service about to dispatch has bookkeeping; the hook itself
    /// performs it unconditionally and must not consult the selector register.
    /// </summary>
    public virtual void OnInterruptEntry() {
    }

    /// <inheritdoc />
    public virtual SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        // Default implementation for most Interrupts:
        //  - Create a callback That will call the Run method
        //  - Write that in ram with an IRET

        // Write ASM
        SegmentedAddress interruptHandlerAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.RegisterAndWriteCallback(VectorNumber, StubServiceCallback);
        memoryAsmWriter.WriteIret();

        return interruptHandlerAddress;
    }

    /// <summary>The callback the stub of a <see cref="Run"/>-only handler registers:
    /// <see cref="RunServiceFromStub"/>, which tells the service call observer when one is set
    /// and runs <see cref="Run"/> either way.</summary>
    protected Action StubServiceCallback => RunServiceFromStub;

    /// <summary>The handler whose <see cref="Run"/> or <see cref="RunServiceFromStub"/>
    /// <paramref name="runnable"/> is a callback of, or <see langword="null"/> when it is another
    /// callback.</summary>
    internal static IInterruptHandler? ServiceCallbackHandler(Action runnable) {
        if (runnable.Target is IInterruptHandler handler
            && runnable.Method.Name is nameof(Run) or nameof(RunServiceFromStub)) {
            return handler;
        }
        return null;
    }

    /// <summary>
    /// The callback of a stub whose only C# step is <see cref="Run()"/>. Tells the service call observer
    /// about the call when the handler has a single service, then runs it. A handler that selects between
    /// services tells the observer from <see cref="Run(int)"/>, once it knows the selector value.
    /// A handler that writes its own stub registers this method where it would register <see cref="Run()"/>.
    /// </summary>
    protected internal void RunServiceFromStub() {
        if (_serviceCallEvents.ObserverFor(VectorNumber) is { } observer && _declarations.SingleService is { } single) {
            observer.OnServiceCall(new ServiceCall(VectorNumber, null, single.Entry, false,
                ServiceCallInputs.ReadAll(State, single.Entry)));
        }
        Run();
    }

    /// <summary>
    /// Tells the service call observer, when there is one, that the service selected by
    /// <paramref name="index"/> is about to run. <see cref="Run(int)"/> calls it. A stub callback that runs
    /// a service without going through <see cref="Run(int)"/> calls it itself.
    /// </summary>
    /// <param name="index">The selector value of the service.</param>
    protected void NotifyServiceCall(int index) {
        if (_serviceCallEvents.ObserverFor(VectorNumber) is not { } observer) {
            return;
        }
        InterruptServiceEntry entry = _serviceEntries.GetValueOrDefault(index) ?? InterruptServiceEntry.Unknown;
        observer.OnServiceCall(new ServiceCall(VectorNumber, index, entry, false,
            ServiceCallInputs.ReadAll(State, entry)));
    }

    /// <summary>
    /// Stores the Action at the given index, with <see cref="InterruptServiceEntry.Unknown"/> effects.
    /// </summary>
    /// <param name="index">The identifier for the action. Typically, it's the value in the AH register.</param>
    /// <param name="action">The C# method that implements the function called via the interrupt.</param>
    public void AddAction(int index, Action action) {
        AddServiceAction(index, action, new InterruptServiceEntry(index, action.Method.Name,
            ServiceContract.Unknown, ServiceStringArgument.None, null, []));
    }

    /// <summary>
    /// Stores the Action at the given index along with its service entry.
    /// </summary>
    /// <param name="index">The identifier for the action. Typically, it's the value in the AH register.</param>
    /// <param name="action">The C# method that implements the function called via the interrupt.</param>
    /// <param name="entry">The declared entry of the service.</param>
    private void AddServiceAction(int index, Action action, InterruptServiceEntry entry) {
        AddRunnable(index, new RunnableAction(action));
        _serviceEntries[index] = entry;
    }

    /// <inheritdoc />
    public override void Run(int index) {
        // By default Log the CS:IP of the caller which is more useful in most situations
        SegmentedAddress? csIp = FunctionHandlerProvider.FunctionHandlerInUse.PeekReturnAddressOnMachineStack(CallType.INTERRUPT);
        _loggerState.CsIp = csIp ?? _loggerState.CsIp;
        NotifyServiceCall(index);
        base.Run(index);
    }

    /// <inheritdoc />
    protected override UnhandledOperationException GenerateUnhandledOperationException(int index) {
        return new UnhandledInterruptException(State, VectorNumber, index);
    }

    /// <summary>
    /// Sets the Carry Flag in the CPU state and on the interrupt stack.
    /// </summary>
    /// <param name="value">The value to set for the Carry Flag.</param>
    protected void SetCarryFlag(bool value) {
        State.CarryFlag = value;
        Stack.SetFlagOnInterruptStack(Flags.Carry, value);
    }

    /// <summary>
    /// Sets the Zero Flag in the CPU state and on the interrupt stack.
    /// </summary>
    /// <param name="value">The value to set for the Zero Flag.</param>
    protected void SetZeroFlag(bool value) {
        State.ZeroFlag = value;
        Stack.SetFlagOnInterruptStack(Flags.Zero, value);
    }

    /// <summary>Reads the caller's Carry Flag from the stacked flag word of the interrupt frame.</summary>
    protected bool ReadCarryFlag() {
        return (Stack.PeekInterruptFrameFlags() & Flags.Carry) != 0;
    }

    /// <summary>Reads the caller's Zero Flag from the stacked flag word of the interrupt frame.</summary>
    protected bool ReadZeroFlag() {
        return (Stack.PeekInterruptFrameFlags() & Flags.Zero) != 0;
    }

    /// <summary>
    /// Pushes the FLAGS/CS:IP frame a hardware-style <c>int</c> pushes before the handler runs and
    /// clears IF: a generated terminate call replaces the whole interrupt entry, so it needs the
    /// same frame on the stack for the termination bookkeeping the interpreted path performs with
    /// the real frame, and for the emulated iret that consumes it.
    /// </summary>
    protected void PushInterruptFrame() {
        Stack.PushInterruptFrame(State.IpSegmentedAddress);
        State.InterruptFlag = false;
    }

    /// <summary>
    /// Finishes a terminate service invoked directly by generated code: halts when process
    /// termination already stopped the machine (a root program exiting back to its loader),
    /// otherwise unwinds to the resume address the termination poked on the restored parent
    /// stack, like the interpreted <c>int</c> stub's iret landing on the terminate address.
    /// </summary>
    /// <param name="terminatedPsp">The PSP segment of the process that terminated.</param>
    /// <param name="ipBefore">CS:IP before the termination ran: when the termination left a new
    /// value (a batch engine continue launching the next program) it is the resume target;
    /// otherwise the resume is the address it poked on the restored parent stack, where the
    /// stub's iret would land.</param>
    /// <param name="emulatesIret">When true the caller replaces the interrupt stub's iret (a
    /// generated terminate call): the frame the termination restored is consumed like iret does,
    /// landing on the poked address with the stacked flags restored. When false the frame stays
    /// in place for a following iret (an interpreted interrupt handler whose Run call unwound).</param>
    /// <exception cref="HaltRequestedException">The machine stopped.</exception>
    /// <exception cref="ProcessTerminatedException">Control resumes at another address.</exception>
    /// <returns>Never returns: the return type only lets a generated terminate call print as a
    /// method's returned action.</returns>
    protected Action ExitOrThrowProcessTerminated(ushort terminatedPsp, SegmentedAddress ipBefore, bool emulatesIret) {
        if (!State.IsRunning) {
            LoggerService.LogTrace("Program terminated; the machine is stopping.");
            throw new HaltRequestedException();
        }
        if (State.IpSegmentedAddress != ipBefore) {
            throw new ProcessTerminatedException(State.IpSegmentedAddress, terminatedPsp);
        }
        if (emulatesIret) {
            (SegmentedAddress returnAddress, ushort flags) = Stack.PopInterruptFrame();
            State.IpSegmentedAddress = returnAddress;
            State.Flags.FlagRegister16 = flags;
            throw new ProcessTerminatedException(State.IpSegmentedAddress, terminatedPsp);
        }
        throw new ProcessTerminatedException(Stack.PeekInterruptFrameReturnAddress(), terminatedPsp);
    }
}
