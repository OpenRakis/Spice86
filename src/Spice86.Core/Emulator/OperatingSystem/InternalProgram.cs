namespace Spice86.Core.Emulator.OperatingSystem;

/// <summary>
/// Base class of the managed internal programs (COMMAND.COM is one of them). Such a program runs as a
/// regular child DOS process: a generated stub on the Z drive crosses into C# through the shared
/// <c>CallbackNumbers.InternalProgram</c> callback, then <see cref="Run"/> executes the program logic.
/// </summary>
internal abstract class InternalProgram {
    private readonly DosProcessManager _processManager;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="processManager">The process manager hosting the current child process.</param>
    protected InternalProgram(DosProcessManager processManager) {
        _processManager = processManager;
    }

    /// <summary>
    /// Gets the process manager hosting the current child process.
    /// </summary>
    protected DosProcessManager ProcessManager => _processManager;

    /// <summary>
    /// Gets the command tail carried in the current child PSP.
    /// </summary>
    protected string CommandTail => _processManager.CurrentCommandTail;

    /// <summary>
    /// Runs the program logic. Control returns to the stub, which terminates the child process.
    /// </summary>
    public abstract void Run();
}
