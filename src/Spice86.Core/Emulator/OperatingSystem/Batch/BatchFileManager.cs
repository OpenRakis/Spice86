namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using Spice86.Shared.Interfaces;
using System.Collections.Generic;

/// <summary>
/// Manages the execution stack of batch files.
/// Handles nested batch file calls (CALL command) and batch file termination.
/// </summary>
/// <remarks>
/// This is designed to be used by a shell/command processor (like COMMAND.COM), NOT by the DOS kernel.
/// In DOS, batch file execution works like this:
/// 1. COMMAND.COM detects a .BAT file is being executed
/// 2. It opens the batch file and pushes it onto this stack
/// 3. For each line, COMMAND.COM reads it, expands variables, and executes it as a command
/// 4. This is similar to DOSBox's approach where the shell handles batch files independently from INT 21h/4Bh
/// </remarks>
public class BatchFileManager {
    private readonly Stack<BatchFile> _batchStack;
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchFileManager"/> class.
    /// </summary>
    /// <param name="loggerService">The logger service for diagnostic output.</param>
    public BatchFileManager(ILoggerService loggerService) {
        _batchStack = new Stack<BatchFile>();
        _loggerService = loggerService;
    }

    /// <summary>
    /// Gets the currently executing batch file, if any.
    /// </summary>
    public BatchFile? CurrentBatchFile => _batchStack.Count > 0 ? _batchStack.Peek() : null;

    /// <summary>
    /// Gets a value indicating whether a batch file is currently executing.
    /// </summary>
    public bool IsExecutingBatch => _batchStack.Count > 0;

    /// <summary>
    /// Gets the current ECHO state from the active batch file, or true if no batch is running.
    /// </summary>
    public bool Echo => CurrentBatchFile?.Echo ?? true;

    /// <summary>
    /// Pushes a new batch file onto the execution stack.
    /// </summary>
    /// <param name="batchFile">The batch file to execute.</param>
    public void PushBatchFile(BatchFile batchFile) {
        _loggerService.Verbose("Starting batch file: {FileName}", batchFile.FileName);
        _batchStack.Push(batchFile);
    }

    /// <summary>
    /// Pops the current batch file from the execution stack and disposes it.
    /// </summary>
    public void PopBatchFile() {
        if (_batchStack.Count > 0) {
            BatchFile batch = _batchStack.Pop();
            _loggerService.Verbose("Ending batch file: {FileName}", batch.FileName);
            batch.Dispose();
        }
    }

    /// <summary>
    /// Clears all batch files from the execution stack and disposes them.
    /// Used when a program terminates or Control-C is pressed.
    /// </summary>
    public void ClearAllBatchFiles() {
        if (_batchStack.Count > 0) {
            _loggerService.Verbose("Clearing all batch files from stack");
            while (_batchStack.Count > 0) {
                BatchFile batch = _batchStack.Pop();
                batch.Dispose();
            }
        }
    }

    /// <summary>
    /// Reads the next line from the current batch file.
    /// </summary>
    /// <param name="line">The output line with variables expanded.</param>
    /// <returns>True if a line was read, false if the batch file has ended.</returns>
    public bool ReadBatchLine(out string line) {
        line = string.Empty;

        BatchFile? batch = CurrentBatchFile;
        if (batch == null) {
            return false;
        }

        if (!batch.ReadLine(out line)) {
            PopBatchFile();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sets the ECHO state for the current batch file.
    /// </summary>
    /// <param name="echoOn">True to enable ECHO, false to disable.</param>
    public void SetEcho(bool echoOn) {
        CurrentBatchFile?.SetEcho(echoOn);
    }

    /// <summary>
    /// Shifts the arguments in the current batch file.
    /// </summary>
    public void Shift() {
        CurrentBatchFile?.Shift();
    }

    /// <summary>
    /// Jumps to a label in the current batch file.
    /// </summary>
    /// <param name="label">The label name to jump to (without the colon).</param>
    /// <returns>True if the label was found, false otherwise.</returns>
    public bool Goto(string label) {
        BatchFile? batch = CurrentBatchFile;
        if (batch == null) {
            return false;
        }

        bool found = batch.Goto(label);
        if (!found) {
            _loggerService.Warning("Label not found in batch file {FileName}: {Label}", 
                batch.FileName, label);
        }

        return found;
    }
}
