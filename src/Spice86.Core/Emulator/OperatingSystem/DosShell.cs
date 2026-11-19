namespace Spice86.Core.Emulator.OperatingSystem;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.OperatingSystem.Batch;

/// <summary>
/// The managed shell (COMMAND.COM). It runs the command tail carried in the child PSP through the
/// batch engine: internal commands execute directly, external programs load as a child of the shell.
/// The interactive shell loop (prompt and command reading) is out of scope.
/// </summary>
internal sealed class DosShell : InternalProgram {
    private readonly ILogger _loggerService;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="processManager">The process manager hosting the current child process.</param>
    /// <param name="loggerService">The logger service.</param>
    internal DosShell(DosProcessManager processManager, ILogger loggerService) : base(processManager) {
        _loggerService = loggerService;
    }

    /// <inheritdoc/>
    public override void Run() {
        string command = ExtractCommand(CommandTail);
        if (string.IsNullOrWhiteSpace(command)) {
            return;
        }
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation("SHELL: commandTail='{CommandTail}' command='{Command}'", CommandTail, command);
        }
        DosBatchExecutionEngine engine = ProcessManager.BatchExecutionEngine;
        if (!engine.TryExecuteCommandLine(command, out LaunchRequest launchRequest)) {
            return;
        }
        if (launchRequest is ContinueBatchExecutionLaunchRequest) {
            return;
        }
        if (!engine.ApplyRedirectionForLaunch(launchRequest)) {
            return;
        }
        ProcessManager.LoadBatchLaunchRequest(launchRequest);
    }

    /// <summary>
    /// Extracts the command from the shell command tail, dropping a leading /C switch.
    /// </summary>
    /// <param name="commandTail">The command tail carried in the child PSP.</param>
    /// <returns>The command to run, empty when the tail held no command.</returns>
    private static string ExtractCommand(string commandTail) {
        string trimmed = commandTail.TrimStart();
        if (trimmed.Length >= 2 && trimmed[0] == '/' && (trimmed[1] is 'C' or 'c')) {
            return trimmed.Substring(2).TrimStart();
        }
        return trimmed;
    }
}
