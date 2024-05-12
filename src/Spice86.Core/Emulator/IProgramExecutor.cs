namespace Spice86.Core.Emulator;

using Spice86.Core.Emulator.InternalDebugger;

/// <summary>
/// The program executor is responsible for starting and controlling the emulated program. <br/>
/// It loads and executes a program following the given configuration in the emulator.<br/>
/// Currently only supports DOS EXE and COM files.
/// </summary>
public interface IProgramExecutor : IDisposable, IDebuggableComponent {
    /// <summary>
    /// Parses the input executable file, and initializes the internal emulator service container.
    /// </summary>
    void LoadFileToRun();
    
    /// <summary>
    /// Starts the emulated program.
    /// </summary>
    void Run();

    /// <summary>
    /// Dumps the emulator state to the specified directory.
    /// </summary>
    /// <param name="path">The directory used for dumping the emulator state.</param>
    void DumpEmulatorStateToDirectory(string path);

    /// <summary>
    /// Gets whether the emulator is currently paused.
    /// </summary>
    bool IsPaused { get; set; }

    /// <summary>
    /// Gets whether we can generate an unconditional GDB breakpoint.
    /// </summary>
    bool IsGdbCommandHandlerAvailable { get; }

    /// <summary>
    /// Steps a single instruction for the internal UI debugger
    /// </summary>
    /// <remarks>Depends on the presence of the GDBServer and GDBCommandHandler</remarks>
    void StepInstruction();
}