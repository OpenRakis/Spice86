namespace Spice86.Core.Emulator.Callback;

using Spice86.Core.Emulator.Errors;

using System.Collections.Generic;

/// <summary>
/// Base class for most classes having to dispatch operations depending on a numeric value, like interrupts.
/// </summary>
public abstract class IndexBasedDispatcher {
    protected Dictionary<int, ICallback> _dispatchTable = new();

    public void AddService(int index, ICallback runnable) {
        _dispatchTable.Add(index, runnable);
    }

    public void Run(int index) {
        GetCallback(index).Run();
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