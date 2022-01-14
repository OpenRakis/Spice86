namespace Spice86.Emulator.Callback;

using Spice86.Emulator.Errors;

using System.Collections.Generic;

/// <summary>
/// Base class for most classes having to dispatch operations depending on a numeric value, like interrupts.
/// </summary>
public abstract class IndexBasedDispatcher<T> where T : IRunnable {
    protected Dictionary<int, ICallback> _dispatchTable = new();

    public void AddService(int index, ICallback runnable) {
        this._dispatchTable.Add(index, runnable);
    }

    public void Run(int index) {
        if (_dispatchTable.TryGetValue(index, out var handler) == false) {
            throw GenerateUnhandledOperationException(index);
        }

        handler.Run();
    }

    protected abstract UnhandledOperationException GenerateUnhandledOperationException(int index);
}