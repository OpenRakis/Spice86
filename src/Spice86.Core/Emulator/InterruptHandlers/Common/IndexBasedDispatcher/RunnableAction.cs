namespace Spice86.Core.Emulator.InterruptHandlers.Common.IndexBasedDispatcher;

/// <summary>
/// An action wrapped inside a IRunnable object. Calling Run invokes the action.
/// </summary>
public class RunnableAction : IRunnable {
    private readonly Action _action;

    public RunnableAction(Action action) {
        _action = action;
    }

    public void Run() {
        _action.Invoke();
    }
}