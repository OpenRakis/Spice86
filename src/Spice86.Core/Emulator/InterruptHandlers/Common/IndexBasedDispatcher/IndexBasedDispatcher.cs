namespace Spice86.Core.Emulator.InterruptHandlers.Common.IndexBasedDispatcher;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Shared.Interfaces;

/// <summary>
/// Base class for most classes having to dispatch operations depending on a numeric value, like interrupts.
/// </summary>
public abstract class IndexBasedDispatcher<T> where T: IRunnable {
    /// <summary>
    /// Defines all the available runnables. Each one has an index number and code associated with it.
    /// </summary>
    private readonly Dictionary<int, T> _dispatchTable = new();

    /// <summary>
    /// The logger service implementation.
    /// </summary>
    protected readonly ILoggerService LoggerService;

    /// <summary>
    /// The CPU state.
    /// </summary>
    protected readonly State State;

    /// <summary>
    /// Initializes a new instance of an <see cref="IndexBasedDispatcher"/>
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logging service to be used (eg. for recording warnings or errors).</param>
    public IndexBasedDispatcher(State state, ILoggerService loggerService) {
        State = state;
        LoggerService = loggerService;
    }

    /// <summary>
    /// list of runnable items registered
    /// </summary>
    public IEnumerable<T> AllRunnables => _dispatchTable.Values;

    /// <summary>
    /// Adds a runnables to the <see cref="_dispatchTable"/> at the associated index.
    /// </summary>
    /// <param name="index">The index at which the runnables will be made available. Must be unique.</param>
    /// <param name="runnable">The code that the runnables will run when called.</param>
    public void AddRunnable(int index, T runnable) {
        _dispatchTable.Add(index, runnable);
    }

    /// <summary>
    /// Runs the runnables at the given index.
    /// </summary>
    /// <param name="index">The index at which the runnables is supposed to be available.</param>
    public virtual void Run(int index) {
        GetRunnable(index).Run();
    }

    /// <summary>
    /// Returns whether the dispatch table contains this key.
    /// </summary>
    /// <param name="index">The index key.</param>
    /// <returns>A boolean value indicating whether the index is recognized.</returns>
    public bool HasRunnable(int index) {
        return _dispatchTable.ContainsKey(index);
    }

    /// <summary>
    /// Gets the runnables from the <see cref="_dispatchTable"/>
    /// </summary>
    /// <param name="index">The index at which the runnables is supposed to be available.</param>
    /// <returns>The piece of runnable C# code, in other words the function implementation.</returns>
    /// <exception cref="UnhandledOperationException">If the runnables is not found in the <see cref="_dispatchTable"/></exception>
    protected T GetRunnable(int index) {
        if (!_dispatchTable.TryGetValue(index, out T? handler)) {
            throw GenerateUnhandledOperationException(index);
        }
        return handler;
    }

    /// <summary>
    /// Helper method that generates an <see cref="UnhandledOperationException"/>
    /// </summary>
    /// <param name="index">The index at which the runnables is supposed to be available.</param>
    /// <returns>An <see cref="UnhandledOperationException"/> instance.</returns>
    protected abstract UnhandledOperationException GenerateUnhandledOperationException(int index);
}