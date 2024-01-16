namespace Spice86.Core.Emulator.InterruptHandlers.Common.IndexBasedDispatcher;

/// <summary>
/// An action wrapped inside a IRunnable object. Calling Run invokes the action.
/// </summary>
public class RunnableAction : IRunnable {
    private readonly Action _action;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunnableAction"/> class.
    /// </summary>
    /// <param name="action">A class that encapsulates a method with no parameters which returns nothing.</param>
    public RunnableAction(Action action) {
        _action = action;
    }
    
    /// <inheritdoc/>
    public void Run() {
        _action.Invoke();
    }
}