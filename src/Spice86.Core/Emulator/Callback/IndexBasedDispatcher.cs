namespace Spice86.Core.Emulator.Callback;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Base class for most classes having to dispatch operations depending on a numeric value, like interrupts.
/// </summary>
public abstract class IndexBasedDispatcher {
    /// <summary>
    /// Defines all the available callbacks. Each one has an index number and code associated with it.
    /// </summary>
    protected Dictionary<int, ICallback> _dispatchTable = new();

    /// <summary>
    /// The logger service implementation.
    /// </summary>
    protected readonly ILoggerService _loggerService;

    /// <summary>
    /// The emulator machine.
    /// </summary>
    protected readonly Machine _machine;

    /// <summary>
    /// Initializes a new instance of an <see cref="IndexBasedDispatcher"/>
    /// </summary>
    /// <param name="machine">The emulator machine instance</param>
    /// <param name="loggerService">The logging service to be used (eg. for recording warnings or errors).</param>
    public IndexBasedDispatcher(Machine machine, ILoggerService loggerService) {
        _machine = machine;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Adds a callable service to the <see cref="_dispatchTable"/> at the associated index.
    /// </summary>
    /// <param name="index">The index at which the callback will be made available. Must be unique.</param>
    /// <param name="runnable">The code that the callback will run when called.</param>
    public void AddService(int index, ICallback runnable) {
        _dispatchTable.Add(index, runnable);
    }

    
    /// <summary>
    /// Runs the callback.
    /// </summary>
    /// <param name="index">The index at which the callback is supposed to be available.</param>
    public void Run(int index) {
        ICallback callback = GetCallback(index);
        SegmentedAddress? csIp = _machine.Cpu.FunctionHandlerInUse.PeekReturnAddressOnMachineStack(CallType.INTERRUPT);
        _loggerService.LoggerPropertyBag.CsIp = csIp ?? _loggerService.LoggerPropertyBag.CsIp;
        _loggerService.Debug("Callback {Index:X2} called", index);
        callback.Run();
    }

    /// <summary>
    /// Runs the callback from C# code that overrides the target program's machine code.
    /// </summary>
    /// <param name="index">The index at which the callback is supposed to be available.</param>
    public void RunFromOverriden(int index) {
        GetCallback(index).RunFromOverriden();
    }

    /// <summary>
    /// Gets the callback from the <see cref="_dispatchTable"/>
    /// </summary>
    /// <param name="index">The index at which the callback is supposed to be available.</param>
    /// <returns></returns>
    /// <exception cref="UnhandledOperationException">If the callback is not found in the <see cref="_dispatchTable"/></exception>
    private ICallback GetCallback(int index) {
        if (!_dispatchTable.TryGetValue(index, out ICallback? handler)) {
            throw GenerateUnhandledOperationException(index);
        }
        return handler;
    }

    /// <summary>
    /// Helper method that generates an <see cref="UnhandledOperationException"/>
    /// </summary>
    /// <param name="index">The index at which the callback is supposed to be available.</param>
    /// <returns></returns>
    protected abstract UnhandledOperationException GenerateUnhandledOperationException(int index);
}