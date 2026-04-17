namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using Serilog.Events;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

internal sealed partial class DosBatchExecutionEngine {
    private const string AutoExecPath = "Z:\\AUTOEXEC.BAT";

    private readonly DosFileManager _dosFileManager;
    private readonly IBatchDisplayCommandHandler _displayCommandHandler;
    private readonly IDosBatchExecutionHost _host;
    private readonly ILoggerService _loggerService;
    private readonly DosDriveManager _driveManager;
    private readonly Stack<BatchFileContext> _batchFileContexts = new();
    private readonly Dictionary<string, string[]> _zDriveFiles = new(StringComparer.OrdinalIgnoreCase);
    private VirtualFileBase? _savedStandardInput;
    private VirtualFileBase? _savedStandardOutput;
    private VirtualFileBase? _savedStandardError;
    private bool _stdinRedirected;
    private bool _stdoutRedirected;
    private bool _stderrRedirected;
    private bool _echoEnabled = true;
    private byte _lastExitCode;
    private DateTime _currentDateTime;

    private static readonly BatchCommandHandlers.IBatchCommandHandler[] KnownCommandHandlers = BatchCommandHandlers.CreateKnownCommandHandlers();

    internal DosBatchExecutionEngine(DosFileManager dosFileManager,
        DosDriveManager driveManager,
        IBatchDisplayCommandHandler displayCommandHandler,
        IDosBatchExecutionHost host,
        ILoggerService loggerService) {
        _dosFileManager = dosFileManager;
        _driveManager = driveManager;
        _displayCommandHandler = displayCommandHandler;
        _host = host;
        _loggerService = loggerService;
        _currentDateTime = DateTime.Now;
    }

    internal void ConfigureHostStartupProgram(string requestedProgramDosPath, string commandTail) {
        string line = BuildCallLine(requestedProgramDosPath, commandTail);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: ConfigureHostStartupProgram program={Program} tail={Tail} generatedLine={Line}",
                requestedProgramDosPath, commandTail, line);
        }
        _zDriveFiles[AutoExecPath] = [line];
        SyncAutoexecBatToMemoryDrive([line]);
        _batchFileContexts.Clear();
    }

    internal bool TryStart(out LaunchRequest launchRequest) {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: TryStart - beginning batch execution via AUTOEXEC.BAT");
        }
        _batchFileContexts.Clear();
        _lastExitCode = 0;
        return TryExecuteCommandLine($"CALL {AutoExecPath}", out launchRequest);
    }

    internal bool TryContinue(ushort lastChildReturnCode, out LaunchRequest launchRequest) {
        _lastExitCode = (byte)(lastChildReturnCode & 0x00FF);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: TryContinue lastChildReturnCode={ReturnCode} exitCode={ExitCode} contextDepth={Depth}",
                lastChildReturnCode, _lastExitCode, _batchFileContexts.Count);
        }
        return TryPump(out launchRequest);
    }

    internal bool TryApplyRedirectionForLaunch(LaunchRequest launchRequest) {
        RestoreStandardHandlesAfterLaunch();

        if (!launchRequest.Redirection.HasAny) {
            return true;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: Applying redirection for launch - stdin={Stdin} stdout={Stdout}(append={AppendOut}) stderr={Stderr}(append={AppendErr})",
                launchRequest.Redirection.InputPath, launchRequest.Redirection.OutputPath,
                launchRequest.Redirection.AppendOutput, launchRequest.Redirection.ErrorPath,
                launchRequest.Redirection.AppendError);
        }
        return TryApplyRedirection(launchRequest.Redirection);
    }

    internal void RestoreStandardHandlesAfterLaunch() {
        RestoreStandardHandle((ushort)DosStandardHandle.Stdin, ref _stdinRedirected, ref _savedStandardInput);
        RestoreStandardHandle((ushort)DosStandardHandle.Stdout, ref _stdoutRedirected, ref _savedStandardOutput);
        RestoreStandardHandle((ushort)DosStandardHandle.Stderr, ref _stderrRedirected, ref _savedStandardError);
    }

    private void RestoreStandardHandle(ushort handle, ref bool isRedirected, ref VirtualFileBase? savedHandle) {
        if (!isRedirected) {
            return;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BATCH: Restoring standard handle {Handle}", handle);
        }
        CloseRedirectedStandardHandle(handle);
        _dosFileManager.OpenFiles[handle] = savedHandle;
        savedHandle = null;
        isRedirected = false;
    }

    private bool TryPump(out LaunchRequest launchRequest) {
        launchRequest = ContinueBatchExecutionLaunchRequest.Instance;

        while (_batchFileContexts.Count > 0) {
            BatchFileContext current = _batchFileContexts.Peek();
            if (!current.TryReadNextLine(out string line)) {
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("BATCH: Pump - end of batch '{BatchFile}', popping (remaining depth={Depth})",
                        current.FilePath, _batchFileContexts.Count - 1);
                }
                CleanupTemporaryFiles(current.TemporaryFilesToCleanup);
                _batchFileContexts.Pop();
                continue;
            }

            string expandedLine = ExpandBatchLine(line, current);
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: [{BatchFile}] raw='{RawLine}' expanded='{ExpandedLine}'",
                    current.FilePath, line, expandedLine);
            }
            if (expandedLine.StartsWith('@')) {
                expandedLine = expandedLine[1..];
            }

            if (IsSkippableBatchLine(expandedLine)) {
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("BATCH: [{BatchFile}] SKIP '{Line}'",
                        current.FilePath, expandedLine);
                }
                continue;
            }

            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: [{BatchFile}] EXEC '{Line}'",
                    current.FilePath, expandedLine);
            }
            if (TryExecuteCommandLine(expandedLine, out launchRequest)) {
                return true;
            }
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: Pump - all batch contexts exhausted, execution complete");
        }
        return false;
    }

    internal bool TryExecuteCommandLine(string commandLine, out LaunchRequest launchRequest) {
        launchRequest = ContinueBatchExecutionLaunchRequest.Instance;

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: TryExecuteCommandLine: '{CommandLine}'", commandLine);
        }

        string[] pipelineSegments = Array.Empty<string>();
        bool hasPipe = ContainsPipeOutsideQuotes(commandLine);
        if (hasPipe && !TrySplitPipelineSegments(commandLine, out pipelineSegments)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BATCH: Failed to split pipeline segments for: {CommandLine}", commandLine);
            }
            return false;
        }

        if (hasPipe && pipelineSegments.Length > 1) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: Handling pipeline with {Count} segments", pipelineSegments.Length);
            }
            return TryHandlePipeline(pipelineSegments, out launchRequest);
        }

        if (!TryParseCommandLine(commandLine, out ParsedCommandLine parsedCommandLine)) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("BATCH: TryParseCommandLine failed for: {CommandLine}", commandLine);
            }
            return false;
        }

        string preprocessedLine = parsedCommandLine.CommandLineWithoutRedirection;
        if (!TryExtractCommandToken(preprocessedLine, out string commandToken, out string argumentPart)) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("BATCH: TryExtractCommandToken failed for: {Line}", preprocessedLine);
            }
            return false;
        }

        string resolvedToken = ResolveCommandTokenForCurrentBatchContext(commandToken);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: Parsed token='{Token}' resolved='{Resolved}' args='{Args}' redirection={HasRedir}",
                commandToken, resolvedToken, argumentPart, parsedCommandLine.Redirection.HasAny);
        }

        CommandExecutionContext commandExecutionContext = new(
            preprocessedLine,
            argumentPart,
            resolvedToken,
            parsedCommandLine.Redirection);

        bool knownCommandResult = TryExecuteKnownCommand(commandExecutionContext, out bool knownCommandMatched,
            out launchRequest);
        if (knownCommandMatched) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: Known command matched: '{Token}' result={Result}", resolvedToken, knownCommandResult);
            }
            return knownCommandResult;
        }

        if (IsBatchPath(commandExecutionContext.ResolvedCommandToken) &&
            !DosFileExists(commandExecutionContext.ResolvedCommandToken)) {
            _lastExitCode = 1;
            return false;
        }

        if (TryResolveBatchCommandPath(commandExecutionContext.ResolvedCommandToken, out string batchCommandPath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: Resolved as batch file: '{BatchPath}' (replacing current context)", batchCommandPath);
            }
            if (_batchFileContexts.Count > 0) {
                BatchFileContext replaced = _batchFileContexts.Pop();
                if (TryPushBatchFile(batchCommandPath, Array.Empty<string>())) {
                    CleanupTemporaryFiles(replaced.TemporaryFilesToCleanup);
                    return TryPump(out launchRequest);
                }

                _batchFileContexts.Push(replaced);
                return false;
            }

            if (TryPushBatchFile(batchCommandPath, Array.Empty<string>())) {
                return TryPump(out launchRequest);
            }

            return false;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: LAUNCH external program: '{Program}' args='{Args}'",
                commandExecutionContext.ResolvedCommandToken, commandExecutionContext.ArgumentPart);
        }
        launchRequest = new ProgramLaunchRequest(commandExecutionContext.ResolvedCommandToken,
            commandExecutionContext.ArgumentPart, commandExecutionContext.Redirection);
        return true;
    }

    private bool TryExecuteKnownCommand(CommandExecutionContext commandExecutionContext,
        out bool commandMatched, out LaunchRequest launchRequest) {
        launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
        for (int i = 0; i < KnownCommandHandlers.Length; i++) {
            BatchCommandHandlers.IBatchCommandHandler batchCommandHandler = KnownCommandHandlers[i];
            if (batchCommandHandler.TryExecute(this, commandExecutionContext,
                out bool commandResult, out launchRequest)) {
                commandMatched = true;
                return commandResult;
            }
        }

        commandMatched = false;
        return false;
    }

    internal bool ExecuteInternalCommandWithArgument(CommandExecutionContext commandExecutionContext,
        Func<string, bool> commandHandler) {
        return TryExecuteInternalCommandWithRedirection(commandExecutionContext.Redirection,
            () => commandHandler(commandExecutionContext.ArgumentPart));
    }

    internal bool ExecuteInternalCommandNoArgument(CommandExecutionContext commandExecutionContext,
        Func<bool> commandHandler) {
        return TryExecuteInternalCommandWithRedirection(commandExecutionContext.Redirection,
            commandHandler);
    }

    internal bool TryExecuteInternalCommandWithRedirection(CommandRedirection redirection, Func<bool> internalCommand) {
        if (redirection.HasAny && !TryApplyRedirectionForLaunch(new ProgramLaunchRequest(string.Empty, string.Empty, redirection))) {
            return false;
        }

        bool result = internalCommand();
        if (redirection.HasAny) {
            RestoreStandardHandlesAfterLaunch();
        }

        return result;
    }

    private static bool IsSkippableBatchLine(string line) {
        string trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return true;
        }

        if (trimmed.StartsWith("::", StringComparison.Ordinal)) {
            return true;
        }

        if (trimmed.StartsWith(':')) {
            return true;
        }

        return string.Equals(trimmed[..Math.Min(3, trimmed.Length)], "REM", StringComparison.OrdinalIgnoreCase) &&
            (trimmed.Length == 3 || char.IsWhiteSpace(trimmed[3]));
    }

    private bool TryApplyInheritedRedirection(CommandRedirection inheritedRedirection, bool launched, ref LaunchRequest launchRequest) {
        if (!launched) {
            return false;
        }

        if (!inheritedRedirection.HasAny) {
            return true;
        }

        CommandRedirection merged = MergeRedirections(launchRequest.Redirection, inheritedRedirection);
        launchRequest = launchRequest.WithRedirection(merged);
        return true;
    }

    internal static bool IsCommandToken(string resolvedCommandToken, string commandName) {
        return string.Equals(resolvedCommandToken, commandName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(resolvedCommandToken, $"{commandName}.COM", StringComparison.OrdinalIgnoreCase);
    }

    private void SyncAutoexecBatToMemoryDrive(string[] lines) {
        if (!_driveManager.TryGetMemoryDrive('Z', out MemoryDrive? zDrive)) {
            return;
        }

        // Build AUTOEXEC.BAT content from lines
        StringBuilder contentBuilder = new();
        foreach (string line in lines) {
            contentBuilder.AppendLine(line);
        }

        string content = contentBuilder.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
        byte[] bytes = Encoding.ASCII.GetBytes(content);
        zDrive.AddFile("AUTOEXEC.BAT", bytes);
    }

}