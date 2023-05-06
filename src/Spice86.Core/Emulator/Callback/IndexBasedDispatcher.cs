namespace Spice86.Core.Emulator.Callback;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;

/// <summary>
/// Base class for most classes having to dispatch operations depending on a numeric value, like interrupts.
/// </summary>
public abstract class IndexBasedDispatcher {
    protected Dictionary<int, ICallback> _dispatchTable = new();

    private readonly ILoggerService _loggerService;

    private readonly Machine _machine;

    public IndexBasedDispatcher(Machine machine, ILoggerService loggerService) {
        _machine = machine;
        _loggerService = loggerService;
    }

    public void AddService(int index, ICallback runnable) {
        _dispatchTable.Add(index, runnable);
    }

    public void Run(int index) {
        ICallback callback = GetCallback(index);
        SegmentedAddress? csIp = _machine.Cpu.FunctionHandlerInUse.PeekReturnAddressOnMachineStack(CallType.INTERRUPT);
        _loggerService.LoggerPropertyBag.CsIp = csIp ?? _loggerService.LoggerPropertyBag.CsIp;
        callback.Run();
    }

    public void RunFromOverriden(int index) {
        GetCallback(index).RunFromOverriden();
    }

    private ICallback GetCallback(int index) {
        if (!_dispatchTable.TryGetValue(index, out ICallback? handler)) {
            throw GenerateUnhandledOperationException(index);
        }
        return handler;
    }

    protected abstract UnhandledOperationException GenerateUnhandledOperationException(int index);
}