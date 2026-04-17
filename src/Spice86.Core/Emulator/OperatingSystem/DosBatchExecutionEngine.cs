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

internal sealed class DosBatchExecutionEngine {
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

    internal bool TryHandleSet(string arguments) {
        string trimmedArguments = arguments.TrimStart();
        if (trimmedArguments.StartsWith("/P ", StringComparison.OrdinalIgnoreCase) ||
            trimmedArguments.StartsWith("/P:", StringComparison.OrdinalIgnoreCase)) {
            WriteToStandardOutput("SET /P unsupported\r\n");
            return false;
        }

        if (trimmedArguments.Length == 0) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: SET - listing all environment variables");
            }
            IReadOnlyList<KeyValuePair<string, string>> entries = _host.GetEnvironmentVariablesSnapshot();
            for (int i = 0; i < entries.Count; i++) {
                KeyValuePair<string, string> entry = entries[i];
                WriteToStandardOutput($"{entry.Key}={entry.Value}\r\n");
            }

            return false;
        }

        int separator = trimmedArguments.IndexOf('=');
        if (separator >= 0) {
            string name = trimmedArguments[..separator].Trim();
            string value = trimmedArguments[(separator + 1)..];
            if (name.Length == 0) {
                return false;
            }

            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: SET {Name}={Value}", name, value);
            }
            _ = _host.TrySetEnvironmentVariable(name, value);
            return false;
        }

        IReadOnlyList<KeyValuePair<string, string>> variables = _host.GetEnvironmentVariablesSnapshot();
        for (int i = 0; i < variables.Count; i++) {
            KeyValuePair<string, string> variable = variables[i];
            if (variable.Key.StartsWith(trimmedArguments, StringComparison.OrdinalIgnoreCase)) {
                WriteToStandardOutput($"{variable.Key}={variable.Value}\r\n");
            }
        }

        return false;
    }

    internal bool TryHandleEcho(string arguments) {
        string rawArguments = arguments;
        string trimmedArguments = rawArguments.TrimStart();
        string normalizedArguments = trimmedArguments.TrimEnd();
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BATCH: ECHO args={Args}", trimmedArguments);
        }
        if (normalizedArguments.Length == 0) {
            bool currentEcho = _batchFileContexts.Count > 0 ? _batchFileContexts.Peek().EchoEnabled : _echoEnabled;
            WriteToStandardOutput(currentEcho ? "ECHO is ON.\r\n" : "ECHO is OFF.\r\n");
            return false;
        }

        if (string.Equals(normalizedArguments, "ON", StringComparison.OrdinalIgnoreCase)) {
            if (_batchFileContexts.Count > 0) {
                _batchFileContexts.Peek().EchoEnabled = true;
            } else {
                _echoEnabled = true;
            }
            return false;
        }

        if (string.Equals(normalizedArguments, "OFF", StringComparison.OrdinalIgnoreCase)) {
            if (_batchFileContexts.Count > 0) {
                _batchFileContexts.Peek().EchoEnabled = false;
            } else {
                _echoEnabled = false;
            }
            return false;
        }

        string outputText = rawArguments;
        if (outputText.Length > 0 && char.IsWhiteSpace(outputText[0])) {
            outputText = outputText[1..];
        }

        if (outputText.Length == 1 && outputText[0] == '.') {
            WriteToStandardOutput("\r\n");
            return false;
        }

        if (outputText.StartsWith(".", StringComparison.Ordinal)) {
            outputText = outputText[1..];
        }

        WriteToStandardOutput($"{outputText}\r\n");
        return false;
    }

    internal bool TryHandlePath(string arguments) {
        string trimmed = arguments.TrimStart();
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: PATH {Args}", trimmed);
        }
        if (trimmed.Length == 0) {
            string? pathValue = _host.TryGetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathValue)) {
                WriteToStandardOutput($"PATH={pathValue}\r\n");
            } else {
                WriteToStandardOutput("PATH=(null)\r\n");
            }

            return false;
        }

        string cleanedArgs = trimmed.TrimStart('=', ' ');
        if (cleanedArgs.Length == 1 && cleanedArgs[0] == ';') {
            _ = _host.TrySetEnvironmentVariable("PATH", string.Empty);
            return false;
        }

        _ = _host.TrySetEnvironmentVariable("PATH", cleanedArgs);
        return false;
    }

    internal void TryHandleCls() {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: CLS - clearing screen");
        }
        _displayCommandHandler.ClearScreen();
    }

    private bool TryHandlePipeline(string[] pipelineSegments, out LaunchRequest launchRequest) {
        launchRequest = ContinueBatchExecutionLaunchRequest.Instance;

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: Pipeline - building {Count} pipe segments", pipelineSegments.Length);
            for (int s = 0; s < pipelineSegments.Length; s++) {
                _loggerService.Debug("BATCH: Pipeline segment[{Index}]: {Segment}", s, pipelineSegments[s]);
            }
        }

        if (!TryBuildPipelineCommands(pipelineSegments, out string[] generatedCommands, out string[] temporaryDosFiles)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BATCH: Pipeline - failed to build pipeline commands");
            }
            return false;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            for (int g = 0; g < generatedCommands.Length; g++) {
                _loggerService.Debug("BATCH: Pipeline generated command[{Index}]: {Cmd}", g, generatedCommands[g]);
            }
        }

        _batchFileContexts.Push(new BatchFileContext("<PIPE>", generatedCommands, Array.Empty<string>(), temporaryDosFiles));
        return TryPump(out launchRequest);
    }

    private bool TryBuildPipelineCommands(string[] pipelineSegments, out string[] generatedCommands, out string[] temporaryDosFiles) {
        generatedCommands = Array.Empty<string>();
        temporaryDosFiles = Array.Empty<string>();

        for (int i = 0; i < pipelineSegments.Length; i++) {
            string segment = pipelineSegments[i].Trim();
            if (!IsValidPipelineSegment(segment)) {
                return false;
            }
        }

        int intermediateFileCount = pipelineSegments.Length - 1;
        List<string> tempFiles = new(intermediateFileCount);
        for (int i = 0; i < intermediateFileCount; i++) {
            string tempDosFile = BuildTemporaryPipeFilePath(i);
            if (!CanCreateTemporaryPipeFile(tempDosFile)) {
                CleanupTemporaryFiles([.. tempFiles]);
                return false;
            }

            tempFiles.Add(tempDosFile);
        }

        string[] commands = new string[pipelineSegments.Length];
        for (int i = 0; i < pipelineSegments.Length; i++) {
            string segment = pipelineSegments[i].Trim();
            if (segment.Length == 0) {
                CleanupTemporaryFiles([.. tempFiles]);
                return false;
            }

            StringBuilder builder = new(segment);
            if (i > 0) {
                builder.Append(" < ");
                builder.Append(EscapeIfNeeded(tempFiles[i - 1]));
            }

            if (i < pipelineSegments.Length - 1) {
                builder.Append(" > ");
                builder.Append(EscapeIfNeeded(tempFiles[i]));
            }

            commands[i] = builder.ToString();
        }

        generatedCommands = commands;
        temporaryDosFiles = [.. tempFiles];
        return true;
    }

    private static bool IsValidPipelineSegment(string commandSegment) {
        if (!TryParseCommandLine(commandSegment, out ParsedCommandLine parsedCommandLine)) {
            return false;
        }

        return TryExtractFirstToken(parsedCommandLine.CommandLineWithoutRedirection, out _, out _);
    }

    private bool CanCreateTemporaryPipeFile(string dosFilePath) {
        DosFileOperationResult createResult = _dosFileManager.CreateFileUsingHandle(dosFilePath, 0);
        if (createResult.IsError || createResult.Value == null) {
            return false;
        }

        ushort handle = (ushort)createResult.Value.Value;
        DosFileOperationResult closeResult = _dosFileManager.CloseFileOrDevice(handle);
        return !closeResult.IsError;
    }

    private void CleanupTemporaryFiles(string[] temporaryDosFiles) {
        if (temporaryDosFiles.Length > 0 && _loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BATCH: Cleaning up {Count} temporary files", temporaryDosFiles.Length);
        }
        for (int i = 0; i < temporaryDosFiles.Length; i++) {
            string? hostPath = _dosFileManager.TryGetFullHostPathFromDos(temporaryDosFiles[i]);
            if (string.IsNullOrWhiteSpace(hostPath)) {
                continue;
            }

            try {
                if (File.Exists(hostPath)) {
                    File.Delete(hostPath);
                    if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                        _loggerService.Verbose("BATCH: Deleted temp file {DosPath} -> {HostPath}", temporaryDosFiles[i], hostPath);
                    }
                }
            } catch (ArgumentNullException exception) {
                _loggerService.Warning(exception,
                    "BATCH: Failed to delete temporary file because path is null {DosPath} -> {HostPath}",
                    temporaryDosFiles[i], hostPath);
            } catch (ArgumentException exception) {
                _loggerService.Warning(exception,
                    "BATCH: Failed to delete temporary file because path is invalid {DosPath} -> {HostPath}",
                    temporaryDosFiles[i], hostPath);
            } catch (DirectoryNotFoundException exception) {
                _loggerService.Warning(exception,
                    "BATCH: Failed to delete temporary file because directory was not found {DosPath} -> {HostPath}",
                    temporaryDosFiles[i], hostPath);
            } catch (PathTooLongException exception) {
                _loggerService.Warning(exception,
                    "BATCH: Failed to delete temporary file because path is too long {DosPath} -> {HostPath}",
                    temporaryDosFiles[i], hostPath);
            } catch (IOException exception) {
                _loggerService.Warning(exception,
                    "BATCH: Failed to delete temporary file because of an I/O error {DosPath} -> {HostPath}",
                    temporaryDosFiles[i], hostPath);
            } catch (NotSupportedException exception) {
                _loggerService.Warning(exception,
                    "BATCH: Failed to delete temporary file because path format is not supported {DosPath} -> {HostPath}",
                    temporaryDosFiles[i], hostPath);
            } catch (UnauthorizedAccessException exception) {
                _loggerService.Warning(exception,
                    "BATCH: Failed to delete temporary file because access was denied {DosPath} -> {HostPath}",
                    temporaryDosFiles[i], hostPath);
            }
        }
    }

    private static string BuildTemporaryPipeFilePath(int index) {
        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        string fileName = $"P{index % 10}{suffix}.TMP";
        return $"C:\\{fileName}";
    }

    private void WriteToStandardOutput(string text) {
        VirtualFileBase? output = _dosFileManager.OpenFiles[(ushort)DosStandardHandle.Stdout];
        if (output == null) {
            return;
        }

        byte[] bytes = Encoding.ASCII.GetBytes(text);
        output.Write(bytes, 0, bytes.Length);
    }

    internal bool TryHandleType(string arguments) {
        string remaining = arguments.Trim();
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: TYPE {Args}", remaining);
        }
        if (remaining.Length == 0) {
            WriteToStandardOutput("Required parameter missing\r\n");
            return false;
        }

        while (TryExtractFirstToken(remaining, out string fileName, out remaining)) {
            DosFileOperationResult openResult = _dosFileManager.OpenFileOrDevice(fileName, FileAccessMode.ReadOnly);
            if (openResult.IsError || openResult.Value == null) {
                WriteToStandardOutput($"File not found - {fileName}\r\n");
                return false;
            }

            ushort handle = (ushort)openResult.Value.Value;
            VirtualFileBase? openedFile = _dosFileManager.OpenFiles[handle];
            VirtualFileBase? stdout = _dosFileManager.OpenFiles[(ushort)DosStandardHandle.Stdout];
            if (openedFile == null || stdout == null) {
                _dosFileManager.CloseFileOrDevice(handle);
                return false;
            }

            byte[] buf = new byte[1];
            while (openedFile.Read(buf, 0, 1) > 0) {
                if (buf[0] == 0x1A) {
                    break;
                }

                stdout.Write(buf, 0, 1);
            }

            _dosFileManager.CloseFileOrDevice(handle);
        }

        return false;
    }

    internal bool TryHandleChdir(string arguments) {
        string trimmed = arguments.Trim();
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: CD/CHDIR args={Args}", trimmed);
        }
        if (trimmed.Length == 0) {
            DosFileOperationResult result = _dosFileManager.GetCurrentDir(0, out string currentDir);
            if (!result.IsError) {
                char driveLetter = _driveManager.CurrentDrive.DriveLetter;
                WriteToStandardOutput($"{driveLetter}:\\{currentDir}\r\n");
            }

            return false;
        }

        if (trimmed.Length == 2 && trimmed[1] == ':') {
            byte driveNumber = (byte)(char.ToUpperInvariant(trimmed[0]) - 'A' + 1);
            DosFileOperationResult result = _dosFileManager.GetCurrentDir(driveNumber, out string currentDir);
            if (!result.IsError) {
                WriteToStandardOutput($"{char.ToUpperInvariant(trimmed[0])}:\\{currentDir}\r\n");
            } else {
                WriteToStandardOutput($"Invalid drive specification\r\n");
            }

            return false;
        }

        // Resolve relative "." and ".." against the current directory before
        // passing to SetCurrentDir, because the path resolver skips these
        // elements when they appear alone without a preceding directory.
        string resolved = ResolveRelativeDosPath(trimmed);
        DosFileOperationResult setResult = _dosFileManager.SetCurrentDir(resolved);
        if (setResult.IsError) {
            WriteToStandardOutput("Invalid directory\r\n");
        }

        return false;
    }

    /// <summary>
    /// Resolves a relative DOS path (e.g. ".", "..", "..\SUBDIR") against the
    /// current drive and directory to produce an absolute DOS path.
    /// </summary>
    private string ResolveRelativeDosPath(string dosPath) {
        if (dosPath.Length == 0) {
            return dosPath;
        }

        // Already absolute (starts with drive letter or backslash)
        char first = dosPath[0];
        if (dosPath.Length >= 2 && dosPath[1] == ':') {
            return dosPath;
        }
        if (first == '\\') {
            return dosPath;
        }

        // Build the current directory prefix
        char driveLetter = _driveManager.CurrentDrive.DriveLetter;
        DosFileOperationResult result = _dosFileManager.GetCurrentDir(0, out string currentDir);
        if (result.IsError) {
            return dosPath;
        }

        string basePath = string.IsNullOrEmpty(currentDir)
            ? $"{driveLetter}:\\"
            : $"{driveLetter}:\\{currentDir}";

        // Split into segments and apply . / .. navigation
        string combined = $"{basePath}\\{dosPath}";
        string[] parts = combined.Split(['\\'], StringSplitOptions.RemoveEmptyEntries);
        List<string> resolved = new();

        for (int i = 0; i < parts.Length; i++) {
            string part = parts[i];
            if (part == ".") {
                continue;
            }
            if (part == "..") {
                if (resolved.Count > 1) {
                    resolved.RemoveAt(resolved.Count - 1);
                }
                continue;
            }
            resolved.Add(part);
        }

        return string.Join("\\", resolved);
    }

    internal void HandleExit() {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: EXIT - clearing all {Count} batch contexts", _batchFileContexts.Count);
        }
        while (_batchFileContexts.Count > 0) {
            BatchFileContext context = _batchFileContexts.Pop();
            CleanupTemporaryFiles(context.TemporaryFilesToCleanup);
        }
    }

    internal bool TryHandleMkdir(string arguments) {
        string trimmed = arguments.Trim();
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: MKDIR {Path}", trimmed);
        }
        if (trimmed.Length == 0) {
            WriteToStandardOutput("Required parameter missing\r\n");
            return false;
        }

        DosFileOperationResult result = _dosFileManager.CreateDirectory(trimmed);
        if (result.IsError) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BATCH: MKDIR failed for {Path}", trimmed);
            }
            WriteToStandardOutput($"Unable to create directory\r\n");
        }

        return false;
    }

    internal bool TryHandleRmdir(string arguments) {
        string trimmed = arguments.Trim();
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: RMDIR {Path}", trimmed);
        }
        if (trimmed.Length == 0) {
            WriteToStandardOutput("Required parameter missing\r\n");
            return false;
        }

        DosFileOperationResult result = _dosFileManager.RemoveDirectory(trimmed);
        if (result.IsError) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BATCH: RMDIR failed for {Path}", trimmed);
            }
            WriteToStandardOutput($"Invalid path, not directory, or directory not empty\r\n");
        }

        return false;
    }

    internal bool TryHandleDel(string arguments) {
        string trimmed = arguments.Trim();
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: DEL {FileSpec}", trimmed);
        }
        if (trimmed.Length == 0) {
            WriteToStandardOutput("Required parameter missing\r\n");
            return false;
        }

        string[] matchingFileNames = _dosFileManager.FindMatchingFileNames(trimmed);
        if (matchingFileNames.Length == 0) {
            WriteToStandardOutput("File not found\r\n");
            return false;
        }

        bool deletedAny = false;
        string directory = GetDirectoryFromFileSpec(trimmed);
        for (int i = 0; i < matchingFileNames.Length; i++) {
            string fileName = matchingFileNames[i];
            string fullDosPath = string.IsNullOrEmpty(directory) ? fileName : $"{directory}\\{fileName}";
            DosFileOperationResult removeResult = _dosFileManager.RemoveFile(fullDosPath);
            if (!removeResult.IsError) {
                deletedAny = true;
            }
        }

        if (!deletedAny) {
            WriteToStandardOutput("File not found\r\n");
        }

        return false;
    }

    private static string GetDirectoryFromFileSpec(string fileSpec) {
        int lastSep = fileSpec.LastIndexOfAny(['\\', '/']);
        if (lastSep >= 0) {
            return fileSpec[..lastSep];
        }

        return string.Empty;
    }

    internal bool TryHandleRen(string arguments) {
        string trimmed = arguments.Trim();
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: REN {Args}", trimmed);
        }
        if (!TryExtractFirstToken(trimmed, out string source, out string tail)) {
            WriteToStandardOutput("Insufficient parameters\r\n");
            return false;
        }

        string target = tail.Trim();
        if (target.Length == 0) {
            WriteToStandardOutput("Insufficient parameters\r\n");
            return false;
        }

        // Check if source contains wildcards
        if (source.Contains('*') || source.Contains('?')) {
            string[] matchingFiles = _dosFileManager.FindMatchingFileNames(source);
            for (int i = 0; i < matchingFiles.Length; i++) {
                string matchedFile = matchingFiles[i];
                string newName = ApplyWildcardTargetPattern(matchedFile, source, target);
                DosFileOperationResult result = _dosFileManager.RenameFile(matchedFile, newName);
                if (result.IsError && _loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("BATCH: REN failed for {Source} -> {Target}", matchedFile, newName);
                }
            }
        } else {
            DosFileOperationResult result = _dosFileManager.RenameFile(source, target);
            if (result.IsError) {
                WriteToStandardOutput("File not found\r\n");
            }
        }

        return false;
    }

    private string ApplyWildcardTargetPattern(string matchedFile, string sourcePattern, string targetPattern) {
        // Simple wildcard matching: * in source matches name part, * in target copies matched portion
        string sourceName = Path.GetFileName(matchedFile);
        int asterisk = sourcePattern.IndexOf('*');
        if (asterisk < 0) {
            return targetPattern;
        }

        int questionMarks = 0;
        for (int i = asterisk; i < sourcePattern.Length && sourcePattern[i] == '?'; i++) {
            questionMarks++;
        }

        if (questionMarks > 0) {
            asterisk = sourcePattern.LastIndexOf('*');
        }

        string beforeWildcard = sourcePattern[..asterisk];
        string afterWildcard = asterisk + 1 < sourcePattern.Length ? sourcePattern[(asterisk + 1)..] : string.Empty;

        string targetBeforeWildcard = new string(' ', 0);
        int targetAsterisk = targetPattern.IndexOf('*');
        if (targetAsterisk >= 0) {
            targetBeforeWildcard = targetPattern[..targetAsterisk];
        }

        if (beforeWildcard.Length > sourceName.Length ||
            (afterWildcard.Length > 0 && !sourceName.EndsWith(afterWildcard, StringComparison.OrdinalIgnoreCase))) {
            return targetPattern;
        }

        int wildcardMatchStart = beforeWildcard.Length;
        int wildcardMatchEnd = sourceName.Length - afterWildcard.Length;
        if (wildcardMatchEnd < wildcardMatchStart) {
            return targetPattern;
        }

        string matchedPart = sourceName[wildcardMatchStart..wildcardMatchEnd];
        if (targetAsterisk >= 0) {
            string targetAfterWildcard = targetAsterisk + 1 < targetPattern.Length ? targetPattern[(targetAsterisk + 1)..] : string.Empty;
            return targetBeforeWildcard + matchedPart + targetAfterWildcard;
        }

        return targetPattern;
    }

    internal bool TryHandleDir(string arguments) {
        string[] tokens = ParseArguments(arguments);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: DIR {Args}", arguments);
        }
        bool bare = false;
        bool listDirectoriesOnly = false;
        bool listFilesOnly = false;
        bool sortByNameAscending = false;
        bool sortByNameDescending = false;
        string fileSpec = "*.*";

        for (int i = 0; i < tokens.Length; i++) {
            string token = tokens[i];
            if (token.StartsWith("/", StringComparison.Ordinal)) {
                if (string.Equals(token, "/B", StringComparison.OrdinalIgnoreCase)) {
                    bare = true;
                    continue;
                }

                if (string.Equals(token, "/AD", StringComparison.OrdinalIgnoreCase)) {
                    listDirectoriesOnly = true;
                    listFilesOnly = false;
                    continue;
                }

                if (string.Equals(token, "/A-D", StringComparison.OrdinalIgnoreCase)) {
                    listFilesOnly = true;
                    listDirectoriesOnly = false;
                    continue;
                }

                if (string.Equals(token, "/ON", StringComparison.OrdinalIgnoreCase)) {
                    sortByNameAscending = true;
                    sortByNameDescending = false;
                    continue;
                }

                if (string.Equals(token, "/O-N", StringComparison.OrdinalIgnoreCase)) {
                    sortByNameDescending = true;
                    sortByNameAscending = false;
                    continue;
                }

                continue;
            } else {
                fileSpec = token;
            }
        }

        DosFileOperationResult findResult = _dosFileManager.FindFirstMatchingFile(fileSpec, 0x37);
        if (findResult.IsError) {
            if (!bare) {
                WriteToStandardOutput("File Not Found\r\n");
            }

            return false;
        }

        if (!bare) {
            char driveLetter = _driveManager.CurrentDrive.DriveLetter;
            string label = _driveManager.CurrentDrive.Label;
            WriteToStandardOutput($" Volume in drive {driveLetter} is {label}\r\n");
            DosFileOperationResult dirResult = _dosFileManager.GetCurrentDir(0, out string currentDir);
            string dirDisplay = dirResult.IsError ? "\\" : $"\\{currentDir}";
            WriteToStandardOutput($" Directory of {driveLetter}:{dirDisplay}\r\n\r\n");
        }

        List<DirEntrySnapshot> entries = new();
        do {
            DosDiskTransferArea dta = _dosFileManager.DiskTransferArea;
            string fileName = dta.FileName;
            if (string.IsNullOrEmpty(fileName)) {
                break;
            }

            entries.Add(new DirEntrySnapshot(fileName, dta.FileAttributes, dta.FileSize, dta.FileDate, dta.FileTime));
        } while (!_dosFileManager.FindNextMatchingFile().IsError);

        if (sortByNameAscending) {
            entries.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.FileName, right.FileName));
        } else if (sortByNameDescending) {
            entries.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(right.FileName, left.FileName));
        }

        uint fileCount = 0;
        uint dirCount = 0;
        long totalBytes = 0;
        for (int i = 0; i < entries.Count; i++) {
            DirEntrySnapshot entry = entries[i];
            string fileName = entry.FileName;
            bool isDirectory = entry.IsDirectory;

            if (listDirectoriesOnly && !isDirectory) {
                continue;
            }

            if (listFilesOnly && isDirectory) {
                continue;
            }

            if (bare) {
                if (fileName != "." && fileName != "..") {
                    WriteToStandardOutput($"{fileName}\r\n");
                }
            } else {
                uint fileSize = entry.FileSize;
                ushort fileDate = entry.FileDate;
                ushort fileTime = entry.FileTime;
                int year = ((fileDate >> 9) & 0x7F) + 1980;
                int month = (fileDate >> 5) & 0x0F;
                int day = fileDate & 0x1F;
                int hour = (fileTime >> 11) & 0x1F;
                int minute = (fileTime >> 5) & 0x3F;

                string sizeOrDir = isDirectory ? "<DIR>     " : $"{fileSize,10}";
                WriteToStandardOutput($"{fileName,-13}{sizeOrDir} {month:D2}-{day:D2}-{year:D4} {hour:D2}:{minute:D2}\r\n");
            }

            if (isDirectory) {
                dirCount++;
            } else {
                fileCount++;
                totalBytes += entry.FileSize;
            }
        }

        if (!bare) {
            WriteToStandardOutput($"     {fileCount} File(s)     {totalBytes} bytes\r\n");
            WriteToStandardOutput($"     {dirCount} Dir(s)\r\n");
        }

        return false;
    }

    private readonly struct DirEntrySnapshot {
        internal DirEntrySnapshot(string fileName, byte fileAttributes, uint fileSize, ushort fileDate, ushort fileTime) {
            FileName = fileName;
            FileAttributes = fileAttributes;
            FileSize = fileSize;
            FileDate = fileDate;
            FileTime = fileTime;
        }

        internal string FileName { get; }
        internal byte FileAttributes { get; }
        internal uint FileSize { get; }
        internal ushort FileDate { get; }
        internal ushort FileTime { get; }
        internal bool IsDirectory => (FileAttributes & 0x10) != 0;
    }

    internal bool TryHandleCopy(string arguments) {
        string trimmed = arguments.Trim();
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: COPY {Args}", trimmed);
        }

        string[] parsedArguments = ParseArguments(trimmed);
        List<string> nonSwitchArguments = new();
        for (int i = 0; i < parsedArguments.Length; i++) {
            string token = parsedArguments[i];
            if (string.Equals(token, "/Y", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "/-Y", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "/V", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "/A", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "/B", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "/T", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            nonSwitchArguments.Add(token);
        }

        if (nonSwitchArguments.Count < 2) {
            WriteToStandardOutput("Required parameter missing\r\n");
            return false;
        }

        string source = nonSwitchArguments[0];
        string destination = nonSwitchArguments[1];

        if (source.Contains('+')) {
            return TryHandleCopyConcat(source, destination);
        }

        bool sourceHasWildcard = source.Contains('*') || source.Contains('?');
        if (sourceHasWildcard) {
            string sourceDir = GetDirectoryFromFileSpec(source);
            string[] matchingFileNames = _dosFileManager.FindMatchingFileNames(source);
            if (matchingFileNames.Length == 0) {
                WriteToStandardOutput($"File not found - {source}\r\n");
                return false;
            }

            uint copyCount = 0;
            for (int i = 0; i < matchingFileNames.Length; i++) {
                string fileName = matchingFileNames[i];
                string fullSource = string.IsNullOrEmpty(sourceDir) ? fileName : $"{sourceDir}\\{fileName}";
                if (IsDosDirectory(fullSource)) {
                    continue;
                }

                string destPath = $"{destination}\\{fileName}";
                if (CopySingleFile(fullSource, destPath)) {
                    copyCount++;
                }
            }

            WriteToStandardOutput($"     {copyCount} file(s) copied\r\n");
        } else {
            string actualDestination = destination;
            if (IsDosDirectory(destination)) {
                string sourceFileName = GetFileNameFromDosPath(source);
                actualDestination = $"{destination}\\{sourceFileName}";
            }

            if (!CopySingleFile(source, actualDestination)) {
                WriteToStandardOutput($"File not found - {source}\r\n");
                return false;
            }

            WriteToStandardOutput("     1 file(s) copied\r\n");
        }

        return false;
    }

    private bool CopySingleFile(string sourceDosPath, string destinationDosPath) {
        DosFileOperationResult openResult = _dosFileManager.OpenFileOrDevice(sourceDosPath, FileAccessMode.ReadOnly);
        if (openResult.IsError || openResult.Value == null) {
            return false;
        }

        ushort sourceHandle = (ushort)openResult.Value.Value;

        DosFileOperationResult createResult = _dosFileManager.CreateFileUsingHandle(destinationDosPath, 0);
        if (createResult.IsError || createResult.Value == null) {
            _dosFileManager.CloseFileOrDevice(sourceHandle);
            return false;
        }

        ushort destHandle = (ushort)createResult.Value.Value;

        VirtualFileBase? sourceFile = _dosFileManager.OpenFiles[sourceHandle];
        VirtualFileBase? destFile = _dosFileManager.OpenFiles[destHandle];
        if (sourceFile != null && destFile != null) {
            byte[] buffer = new byte[0x8000];
            int bytesRead;
            while ((bytesRead = sourceFile.Read(buffer, 0, buffer.Length)) > 0) {
                destFile.Write(buffer, 0, bytesRead);
            }
        }

        _dosFileManager.CloseFileOrDevice(sourceHandle);
        _dosFileManager.CloseFileOrDevice(destHandle);
        return true;
    }

    private bool TryHandleCopyConcat(string sourcesWithPlus, string destination) {
        string[] sources = sourcesWithPlus.Split('+');

        DosFileOperationResult createResult = _dosFileManager.CreateFileUsingHandle(destination, 0);
        if (createResult.IsError || createResult.Value == null) {
            WriteToStandardOutput($"Unable to create destination - {destination}\r\n");
            return false;
        }

        ushort destHandle = (ushort)createResult.Value.Value;
        VirtualFileBase? destFile = _dosFileManager.OpenFiles[destHandle];
        if (destFile == null) {
            _dosFileManager.CloseFileOrDevice(destHandle);
            return false;
        }

        byte[] buffer = new byte[0x8000];
        for (int i = 0; i < sources.Length; i++) {
            string sourcePath = sources[i].Trim();
            if (sourcePath.Length == 0) {
                continue;
            }

            DosFileOperationResult openResult = _dosFileManager.OpenFileOrDevice(sourcePath, FileAccessMode.ReadOnly);
            if (openResult.IsError || openResult.Value == null) {
                WriteToStandardOutput($"File not found - {sourcePath}\r\n");
                _dosFileManager.CloseFileOrDevice(destHandle);
                return false;
            }

            ushort sourceHandle = (ushort)openResult.Value.Value;
            VirtualFileBase? sourceFile = _dosFileManager.OpenFiles[sourceHandle];
            if (sourceFile != null) {
                int bytesRead;
                while ((bytesRead = sourceFile.Read(buffer, 0, buffer.Length)) > 0) {
                    destFile.Write(buffer, 0, bytesRead);
                }
            }

            _dosFileManager.CloseFileOrDevice(sourceHandle);
        }

        _dosFileManager.CloseFileOrDevice(destHandle);
        WriteToStandardOutput("     1 file(s) copied\r\n");
        return false;
    }

    internal bool TryHandleMove(string arguments) {
        string trimmed = arguments.Trim();
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: MOVE {Args}", trimmed);
        }
        if (!TryExtractFirstToken(trimmed, out string source, out string tail)) {
            WriteToStandardOutput("Required parameter missing\r\n");
            return false;
        }

        string destination = tail.Trim();
        if (destination.Length == 0) {
            WriteToStandardOutput("Required parameter missing\r\n");
            return false;
        }

        bool sourceHasWildcard = source.Contains('*') || source.Contains('?');
        if (sourceHasWildcard) {
            string sourceDir = GetDirectoryFromFileSpec(source);
            string[] matchingFileNames = _dosFileManager.FindMatchingFileNames(source);
            if (matchingFileNames.Length == 0) {
                WriteToStandardOutput($"File not found - {source}\r\n");
                return false;
            }

            uint moveCount = 0;
            for (int i = 0; i < matchingFileNames.Length; i++) {
                string fileName = matchingFileNames[i];
                string fullSource = string.IsNullOrEmpty(sourceDir) ? fileName : $"{sourceDir}\\{fileName}";
                if (IsDosDirectory(fullSource)) {
                    continue;
                }

                string destPath = $"{destination}\\{fileName}";
                DosFileOperationResult moveResult = _dosFileManager.MoveFile(fullSource, destPath);
                if (!moveResult.IsError) {
                    moveCount++;
                }
            }

            WriteToStandardOutput($"     {moveCount} file(s) moved\r\n");
        } else {
            string actualDestination = destination;
            if (IsDosDirectory(destination)) {
                string sourceFileName = GetFileNameFromDosPath(source);
                actualDestination = $"{destination}\\{sourceFileName}";
            }

            DosFileOperationResult result = _dosFileManager.MoveFile(source, actualDestination);
            if (result.IsError) {
                WriteToStandardOutput($"File not found - {source}\r\n");
                return false;
            }

            WriteToStandardOutput("     1 file(s) moved\r\n");
        }

        return false;
    }

    internal bool TryHandleDate(string arguments) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BATCH: DATE {Args}", arguments);
        }

        string[] tokens = ParseArguments(arguments);
        bool help = false;
        bool shortMode = false;
        bool hostSync = false;
        string setDateToken = string.Empty;
        for (int i = 0; i < tokens.Length; i++) {
            string token = tokens[i];
            if (string.Equals(token, "/?", StringComparison.OrdinalIgnoreCase)) {
                help = true;
                continue;
            }

            if (string.Equals(token, "/T", StringComparison.OrdinalIgnoreCase)) {
                shortMode = true;
                continue;
            }

            if (string.Equals(token, "/H", StringComparison.OrdinalIgnoreCase)) {
                hostSync = true;
                continue;
            }

            setDateToken = token;
        }

        if (help) {
            WriteToStandardOutput("DATE [/T] [/H] [MM-DD-YYYY]\r\n");
            return false;
        }

        if (hostSync) {
            _currentDateTime = DateTime.Now;
        }

        if (setDateToken.Length > 0) {
            if (!TryParseDateInput(setDateToken, out DateTime parsedDate)) {
                WriteToStandardOutput("Invalid date\r\n");
                return false;
            }

            _currentDateTime = new DateTime(parsedDate.Year, parsedDate.Month, parsedDate.Day,
                _currentDateTime.Hour, _currentDateTime.Minute, _currentDateTime.Second, _currentDateTime.Millisecond);
        }

        DateTime now = _currentDateTime;
        if (shortMode) {
            WriteToStandardOutput($"{now:MM-dd-yyyy}\r\n");
            return false;
        }

        WriteToStandardOutput($"Current date is {now:MM-dd-yyyy}\r\n");
        return false;
    }

    internal bool TryHandleTime(string arguments) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BATCH: TIME {Args}", arguments);
        }

        string[] tokens = ParseArguments(arguments);
        bool help = false;
        bool shortMode = false;
        bool hostSync = false;
        string setTimeToken = string.Empty;
        for (int i = 0; i < tokens.Length; i++) {
            string token = tokens[i];
            if (string.Equals(token, "/?", StringComparison.OrdinalIgnoreCase)) {
                help = true;
                continue;
            }

            if (string.Equals(token, "/T", StringComparison.OrdinalIgnoreCase)) {
                shortMode = true;
                continue;
            }

            if (string.Equals(token, "/H", StringComparison.OrdinalIgnoreCase)) {
                hostSync = true;
                continue;
            }

            setTimeToken = token;
        }

        if (help) {
            WriteToStandardOutput("TIME [/T] [/H] [HH:MM:SS]\r\n");
            return false;
        }

        if (hostSync) {
            _currentDateTime = DateTime.Now;
        }

        if (setTimeToken.Length > 0) {
            if (!TryParseTimeInput(setTimeToken, out TimeSpan parsedTime)) {
                WriteToStandardOutput("Invalid time\r\n");
                return false;
            }

            _currentDateTime = new DateTime(_currentDateTime.Year, _currentDateTime.Month, _currentDateTime.Day,
                parsedTime.Hours, parsedTime.Minutes, parsedTime.Seconds, 0);
        }

        DateTime now = _currentDateTime;
        if (shortMode) {
            WriteToStandardOutput($"{now:HH:mm:ss}\r\n");
            return false;
        }

        WriteToStandardOutput($"Current time is {now:HH:mm:ss.ff}\r\n");
        return false;
    }

    private static bool TryParseDateInput(string dateToken, out DateTime parsedDate) {
        string[] formats = [
            "MM-dd-yyyy",
            "MM/dd/yyyy",
            "yyyy-MM-dd",
            "yyyy/MM/dd"
        ];
        return DateTime.TryParseExact(dateToken, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out parsedDate);
    }

    private static bool TryParseTimeInput(string timeToken, out TimeSpan parsedTime) {
        string[] formats = [
            "hh\\:mm\\:ss",
            "h\\:mm\\:ss",
            "hh\\:mm",
            "h\\:mm"
        ];
        return TimeSpan.TryParseExact(timeToken, formats, CultureInfo.InvariantCulture, out parsedTime);
    }

    internal bool TryHandleVer() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BATCH: VER");
        }
        WriteToStandardOutput("Spice86 DOS version 5.00\r\n");
        return false;
    }

    internal bool TryHandleVol(string arguments) {
        string trimmed = arguments.Trim();
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BATCH: VOL {Args}", trimmed);
        }

        char driveLetter = _driveManager.CurrentDrive.DriveLetter;
        string label = _driveManager.CurrentDrive.Label;

        // For now, VOL accepts an optional drive argument (e.g., "VOL C:")
        // but always reports the current drive (simplified implementation)
        // Full DOSBox parity would require DriveManager enhancements

        WriteToStandardOutput($" Volume in drive {driveLetter} is {label}\r\n");
        return false;
    }

    internal bool TryHandleCall(string arguments, CommandRedirection commandRedirection, out LaunchRequest launchRequest) {
        launchRequest = ContinueBatchExecutionLaunchRequest.Instance;

        if (!TryExtractFirstToken(arguments, out string targetToken, out string tail)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BATCH: CALL - no target token in: {Args}", arguments);
            }
            return false;
        }

        string resolvedTargetToken = ResolveCommandTokenForCurrentBatchContext(targetToken);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: CALL target='{Target}' resolved='{Resolved}' tail='{Tail}'",
                targetToken, resolvedTargetToken, tail);
        }

        if (resolvedTargetToken.StartsWith(':')) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: CALL - label-style target (subroutine): {Target}", resolvedTargetToken);
            }
            string[] callArguments = ParseArguments(tail);
            if (!TryHandleCallLabel(resolvedTargetToken, callArguments, out launchRequest)) {
                return false;
            }
            return TryPump(out launchRequest);
        }

        string[] callArguments2 = ParseArguments(tail);

        if (TryResolveBatchCommandPath(resolvedTargetToken, out string batchTargetPath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: CALL - pushing batch file: '{Path}' with {ArgCount} args",
                    batchTargetPath, callArguments2.Length);
            }
            if (!TryPushBatchFile(batchTargetPath, callArguments2)) {
                _lastExitCode = 1;
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("BATCH: CALL - failed to push batch file: {Path}", batchTargetPath);
                }
                return false;
            }

            return TryPump(out launchRequest);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: CALL - launching external program: '{Program}'", resolvedTargetToken);
        }
        launchRequest = new ProgramLaunchRequest(resolvedTargetToken, JoinArguments(callArguments2), commandRedirection);
        return true;
    }

    private bool TryHandleCallLabel(string labelTarget, string[] callArguments, out LaunchRequest launchRequest) {
        launchRequest = ContinueBatchExecutionLaunchRequest.Instance;

        if (_batchFileContexts.Count == 0) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BATCH: CALL :LABEL - no batch context, ignoring");
            }
            return false;
        }

        string label = labelTarget.Trim();
        if (label.StartsWith(':')) {
            label = label[1..];
        }

        if (string.IsNullOrWhiteSpace(label)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BATCH: CALL :LABEL - empty label, returning");
            }
            return true;
        }

        BatchFileContext currentContext = _batchFileContexts.Peek();

        // Find the label line and extract subroutine lines
        string[] subroutineLines = ExtractSubroutineLines(currentContext, label);
        if (subroutineLines.Length == 0) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: CALL :LABEL - label not found: {Label}, continuing", label);
            }
            return true;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: CALL :LABEL - label='{Label}' subroutineLines={Count}",
                label, subroutineLines.Length);
            for (int i = 0; i < subroutineLines.Length; i++) {
                _loggerService.Debug("BATCH: CALL :LABEL subroutine[{Index}]: {Line}", i, subroutineLines[i]);
            }
        }

        _batchFileContexts.Push(new BatchFileContext($"<CALL:{label}>", subroutineLines, callArguments, Array.Empty<string>()));
        return true;
    }

    private string[] ExtractSubroutineLines(BatchFileContext context, string label) {
        List<string> subroutineLines = new();

        // First, find the label line
        int labelLineIndex = -1;
        string[] lines = context.GetAllLines();

        for (int i = 0; i < lines.Length; i++) {
            string line = lines[i].TrimStart();
            if (!line.StartsWith(':')) {
                continue;
            }

            string candidate = line[1..].Trim();
            if (string.Equals(candidate, label, StringComparison.OrdinalIgnoreCase)) {
                labelLineIndex = i;
                break;
            }
        }

        if (labelLineIndex < 0) {
            return Array.Empty<string>();
        }

        // Extract lines from the label to :EOF or end of file
        for (int i = labelLineIndex + 1; i < lines.Length; i++) {
            string line = lines[i].TrimStart();

            // Stop at :EOF label
            if (line.StartsWith(':') && string.Equals(line[1..].Trim(), "EOF", StringComparison.OrdinalIgnoreCase)) {
                break;
            }

            subroutineLines.Add(lines[i]);
        }

        return [.. subroutineLines];
    }

    internal bool TryHandleGoto(string arguments) {
        if (_batchFileContexts.Count == 0) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BATCH: GOTO outside of batch context, ignoring");
            }
            return false;
        }

        string label = arguments.Trim();
        if (label.StartsWith(':')) {
            label = label[1..];
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: GOTO label={Label}", label);
        }

        if (string.IsNullOrWhiteSpace(label)) {
            WriteToStandardOutput("Label not found\r\n");
            return false;
        }

        BatchFileContext context = _batchFileContexts.Peek();
        if (!context.TryGoto(label)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BATCH: GOTO - label not found: {Label}, popping context", label);
            }
            WriteToStandardOutput($"Label not found - {label}\r\n");
            _batchFileContexts.Pop();
        } else if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: GOTO - jumped to label: {Label}", label);
        }

        return false;
    }

    internal bool TryHandleShift() {
        if (_batchFileContexts.Count == 0) {
            return false;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BATCH: SHIFT - shifting arguments");
        }
        BatchFileContext context = _batchFileContexts.Peek();
        context.Shift();
        return false;
    }

    internal bool TryHandleIf(string arguments, CommandRedirection inheritedRedirection, out LaunchRequest launchRequest) {
        launchRequest = ContinueBatchExecutionLaunchRequest.Instance;

        string working = arguments.TrimStart();
        bool hasNot = TryConsumeKeyword(ref working, "NOT");

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: IF not={Not} args={Args}", hasNot, working);
        }

        if (TryConsumeKeyword(ref working, "ERRORLEVEL")) {
            string errorLevelExpression = TrimLeadingEqualsAndWhitespace(working);
            if (!TryExtractFirstToken(errorLevelExpression, out string levelToken, out string commandPart)) {
                return false;
            }

            if (!int.TryParse(levelToken, out int threshold)) {
                return false;
            }

            bool condition = _lastExitCode >= threshold;
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: IF ERRORLEVEL {Threshold} - exitCode={ExitCode} condition={Condition} (with NOT={Not})",
                    threshold, _lastExitCode, condition, hasNot);
            }
            if (condition != hasNot) {
                bool launched = TryExecuteCommandLine(commandPart, out launchRequest);
                return TryApplyInheritedRedirection(inheritedRedirection, launched, ref launchRequest);
            }

            return false;
        }

        if (TryConsumeKeyword(ref working, "EXIST")) {
            if (!TryExtractFirstToken(working, out string fileToken, out string commandPart)) {
                return false;
            }

            bool exists = DoesFileExist(fileToken);
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: IF EXIST {File} - exists={Exists} (with NOT={Not})",
                    fileToken, exists, hasNot);
            }
            if (exists != hasNot) {
                bool launched = TryExecuteCommandLine(commandPart, out launchRequest);
                return TryApplyInheritedRedirection(inheritedRedirection, launched, ref launchRequest);
            }

            return false;
        }

        int compareIndex = working.IndexOf("==", StringComparison.Ordinal);
        if (compareIndex < 0) {
            return false;
        }

        string left = working[..compareIndex].Trim();
        string rightAndCommand = working[(compareIndex + 2)..].TrimStart();
        if (!TryExtractIfComparisonToken(rightAndCommand, out string rightToken, out string commandSegment)) {
            return false;
        }

        bool equals = string.Equals(left, rightToken, StringComparison.Ordinal);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: IF compare left={Left} == right={Right} equals={Equals} (with NOT={Not})",
                left, rightToken, equals, hasNot);
        }
        if (equals != hasNot) {
            bool launched = TryExecuteCommandLine(commandSegment, out launchRequest);
            return TryApplyInheritedRedirection(inheritedRedirection, launched, ref launchRequest);
        }

        return false;
    }

    private static bool TryExtractIfComparisonToken(string input, out string token, out string remaining) {
        token = string.Empty;
        remaining = string.Empty;

        string trimmed = input.TrimStart();
        if (trimmed.Length == 0) {
            return false;
        }

        if (trimmed[0] == '"') {
            int closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote < 0) {
                return false;
            }

            token = trimmed[..(closingQuote + 1)];
            remaining = trimmed[(closingQuote + 1)..].TrimStart();
            return true;
        }

        int separatorIndex = trimmed.IndexOfAny([' ', '\t']);
        if (separatorIndex < 0) {
            token = trimmed;
            return true;
        }

        token = trimmed[..separatorIndex];
        remaining = trimmed[separatorIndex..].TrimStart();
        return token.Length > 0;
    }

    internal bool TryHandleFor(string arguments, CommandRedirection commandRedirection, out LaunchRequest launchRequest) {
        launchRequest = ContinueBatchExecutionLaunchRequest.Instance;

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: FOR {Args}", arguments);
        }

        string working = arguments.TrimStart();
        if (!TryExtractFirstToken(working, out string variableToken, out string restAfterVariable)) {
            return false;
        }

        if (!TryGetForVariable(variableToken, out char variableName)) {
            return false;
        }

        string afterIn = restAfterVariable.TrimStart();
        if (afterIn.StartsWith("IN(", StringComparison.OrdinalIgnoreCase)) {
            afterIn = afterIn[2..];
        } else if (!TryConsumeKeyword(ref afterIn, "IN")) {
            return false;
        }

        string inSegment = afterIn.TrimStart();
        if (inSegment.Length < 2 || inSegment[0] != '(') {
            return false;
        }

        int closeParen = FindClosingParenthesisOutsideQuotes(inSegment);
        if (closeParen < 0) {
            return false;
        }

        string listSegment = inSegment[1..closeParen];
        string afterList = inSegment[(closeParen + 1)..].TrimStart();
        if (!TryConsumeKeyword(ref afterList, "DO")) {
            return false;
        }

        string commandTemplate = afterList.TrimStart();
        if (string.IsNullOrWhiteSpace(commandTemplate)) {
            return false;
        }

        string[] listValues = ParseForList(listSegment);
        if (listValues.Length == 0) {
            return false;
        }

        string[] generatedCommands = new string[listValues.Length];
        for (int i = 0; i < listValues.Length; i++) {
            string generatedCommand = ReplaceForVariable(commandTemplate, variableName, listValues[i]);
            generatedCommands[i] = AppendRedirection(generatedCommand, commandRedirection);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: FOR variable={Var} list={ListCount} items, template={Template}",
                variableName, listValues.Length, commandTemplate);
            for (int g = 0; g < generatedCommands.Length; g++) {
                _loggerService.Debug("BATCH: FOR generated[{Index}]: {Cmd}", g, generatedCommands[g]);
            }
        }

        _batchFileContexts.Push(new BatchFileContext("<FOR>", generatedCommands, Array.Empty<string>(), Array.Empty<string>()));
        return TryPump(out launchRequest);
    }

    private bool TryPushBatchFile(string dosPath, string[] arguments) {
        if (!TryReadBatchFile(dosPath, out string[] lines)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BATCH: Failed to read batch file: {DosPath}", dosPath);
            }
            return false;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: Pushing batch file: '{DosPath}' ({LineCount} lines, {ArgCount} args, depth={Depth})",
                dosPath, lines.Length, arguments.Length, _batchFileContexts.Count + 1);
            for (int l = 0; l < lines.Length; l++) {
                _loggerService.Debug("BATCH:   [{LineNum}] {Line}", l, lines[l]);
            }
        }
        BatchFileContext context = new BatchFileContext(dosPath, lines, arguments, Array.Empty<string>());
        _batchFileContexts.Push(context);
        return true;
    }

    private bool TryReadBatchFile(string dosPath, out string[] lines) {
        lines = Array.Empty<string>();
        string normalizedPath = NormalizeDosPath(dosPath);

        if (string.Equals(normalizedPath, AutoExecPath, StringComparison.OrdinalIgnoreCase) &&
            _zDriveFiles.TryGetValue(AutoExecPath, out string[]? zFileLines)) {
            lines = zFileLines;
            return true;
        }

        DosFileOperationResult openResult = _dosFileManager.OpenFileOrDevice(normalizedPath, FileAccessMode.ReadOnly);
        if (openResult.IsError || openResult.Value == null) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BATCH: could not open file {DosPath}", normalizedPath);
            }

            return false;
        }

        ushort handle = (ushort)openResult.Value.Value;
        VirtualFileBase? openedFile = _dosFileManager.OpenFiles[handle];
        if (openedFile == null) {
            _dosFileManager.CloseFileOrDevice(handle);
            return false;
        }

        List<string> lineList = new();
        StringBuilder currentLine = new();
        byte[] buf = new byte[1];
        while (openedFile.Read(buf, 0, 1) > 0) {
            if (buf[0] == 0x1A) {
                break;
            }

            if (buf[0] == (byte)'\n') {
                lineList.Add(currentLine.ToString());
                currentLine.Clear();
                continue;
            }

            if (buf[0] != (byte)'\r') {
                currentLine.Append((char)buf[0]);
            }
        }

        if (currentLine.Length > 0) {
            lineList.Add(currentLine.ToString());
        }

        _dosFileManager.CloseFileOrDevice(handle);
        lines = [.. lineList];
        return true;
    }

    private static string BuildCallLine(string requestedProgramDosPath, string commandTail) {
        string escapedProgram = EscapeIfNeeded(requestedProgramDosPath);
        string[] parsedArguments = ParseArguments(commandTail);
        if (parsedArguments.Length == 0) {
            return $"CALL {escapedProgram}";
        }

        return $"CALL {escapedProgram} {JoinArguments(parsedArguments)}";
    }

    private static string EscapeIfNeeded(string token) {
        if (token.Contains(' ') || token.Contains('\t')) {
            return $"\"{token}\"";
        }

        return token;
    }

    private static bool IsBatchPath(string path) {
        return path.EndsWith(".BAT", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveBatchCommandPath(string commandToken, out string batchPath) {
        batchPath = commandToken;

        if (IsBatchPath(commandToken)) {
            return DosFileExists(commandToken);
        }

        if (commandToken.Contains('*') || commandToken.Contains('?') || Path.HasExtension(commandToken)) {
            return false;
        }

        string candidatePath = commandToken + ".BAT";
        if (DosFileExists(candidatePath)) {
            batchPath = candidatePath;
            return true;
        }

        return false;
    }

    private bool DosFileExists(string dosPath) {
        string normalizedPath = NormalizeDosPath(dosPath);
        if (_zDriveFiles.ContainsKey(normalizedPath)) {
            return true;
        }

        return _dosFileManager.FileOrDeviceExists(normalizedPath);
    }

    private bool IsDosDirectory(string dosPath) {
        DosFileOperationResult findResult = _dosFileManager.FindFirstMatchingFile(dosPath, 0x10);
        if (findResult.IsError) {
            return false;
        }

        return (_dosFileManager.DiskTransferArea.FileAttributes & 0x10) != 0;
    }

    private static string GetFileNameFromDosPath(string dosPath) {
        int lastSep = dosPath.LastIndexOfAny(['\\', '/']);
        return lastSep >= 0 ? dosPath[(lastSep + 1)..] : dosPath;
    }

    private string ResolveCommandTokenForCurrentBatchContext(string commandToken) {
        if (!IsRelativeCommandToken(commandToken)) {
            if (!Path.HasExtension(commandToken)) {
                string? resolvedNonRelative = TryResolveExecutablePath(commandToken);
                if (resolvedNonRelative != null) {
                    return resolvedNonRelative;
                }
            }

            return commandToken;
        }

        if (_batchFileContexts.Count > 0) {
            BatchFileContext context = _batchFileContexts.Peek();
            string? directoryPath = context.TryGetContainingDirectory();
            if (!string.IsNullOrWhiteSpace(directoryPath)) {
                string candidate = NormalizeDosPath($"{directoryPath}\\{commandToken}");
                string? resolvedInBatchDirectory = TryResolveExecutablePath(candidate);
                if (resolvedInBatchDirectory != null) {
                    if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                        _loggerService.Verbose("BATCH: Resolved relative command {Token} -> {Candidate} (from batch dir)",
                            commandToken, resolvedInBatchDirectory);
                    }
                    return resolvedInBatchDirectory;
                }
            }
        }

        // Search PATH directories with .COM -> .EXE -> .BAT probe order.
        string? pathResolved = TryResolveCommandFromPath(commandToken);
        if (pathResolved != null) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("BATCH: Resolved command {Token} -> {Path} (from PATH)", commandToken, pathResolved);
            }
            return pathResolved;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BATCH: Command token {Token} not resolved, using as-is", commandToken);
        }
        return commandToken;
    }

    private string? TryResolveCommandFromPath(string commandToken) {
        string? pathEnv = _host.TryGetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) {
            return null;
        }

        string[] pathDirs = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (int d = 0; d < pathDirs.Length; d++) {
            string dir = pathDirs[d].TrimEnd('\\');
            string candidatePrefix = $"{dir}\\{commandToken}";
            string? resolvedCandidate = TryResolveExecutablePath(candidatePrefix);
            if (resolvedCandidate != null) {
                return resolvedCandidate;
            }
        }

        return null;
    }

    private string? TryResolveExecutablePath(string candidatePrefix) {
        if (DosFileExists(candidatePrefix)) {
            return candidatePrefix;
        }

        string[] extensions = [".COM", ".EXE", ".BAT"];
        for (int i = 0; i < extensions.Length; i++) {
            string candidate = candidatePrefix + extensions[i];
            if (DosFileExists(candidate)) {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsRelativeCommandToken(string commandToken) {
        if (string.IsNullOrWhiteSpace(commandToken)) {
            return false;
        }

        return !commandToken.Contains(':') && !commandToken.Contains('\\') && !commandToken.Contains('/');
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

    private static bool TryExtractFirstToken(string input, out string token, out string remaining) {
        token = string.Empty;
        remaining = string.Empty;

        string trimmed = input.TrimStart();
        if (trimmed.Length == 0) {
            return false;
        }

        if (trimmed[0] == '"') {
            int closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote < 0) {
                token = trimmed[1..];
                return !string.IsNullOrWhiteSpace(token);
            }

            token = trimmed.Substring(1, closingQuote - 1);
            remaining = trimmed[(closingQuote + 1)..].TrimStart();
            return !string.IsNullOrWhiteSpace(token);
        }

        int separatorIndex = trimmed.IndexOfAny([' ', '\t']);
        if (separatorIndex < 0) {
            token = trimmed;
            return true;
        }

        token = trimmed[..separatorIndex];
        remaining = trimmed[separatorIndex..].TrimStart();
        return true;
    }

    private static bool TryExtractCommandToken(string input, out string token, out string remaining) {
        token = string.Empty;
        remaining = string.Empty;

        string trimmed = input.TrimStart();
        if (trimmed.Length == 0) {
            return false;
        }

        if (trimmed[0] == '"') {
            int closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote < 0) {
                token = trimmed[1..];
                return !string.IsNullOrWhiteSpace(token);
            }

            token = trimmed.Substring(1, closingQuote - 1);
            remaining = trimmed[(closingQuote + 1)..];
            return !string.IsNullOrWhiteSpace(token);
        }

        int separatorIndex = trimmed.IndexOfAny([' ', '\t']);
        if (separatorIndex < 0) {
            token = trimmed;
        } else {
            token = trimmed[..separatorIndex];
            remaining = trimmed[separatorIndex..];
        }

        // DOS allows certain short internal commands to be followed directly by
        // path characters without a space (e.g. CD.., CD\, MD\SUBDIR, DIR/W).
        TrySplitCompactCommand(ref token, ref remaining);

        return true;
    }

    /// <summary>
    /// Splits a compact command token like "CD..", "CD\TEMP", "DIR/W" into the
    /// command portion and the argument portion.  In DOS, the space between a
    /// short internal command and its argument is optional when the argument
    /// starts with '.', '\', or '/'.
    /// </summary>
    private static void TrySplitCompactCommand(ref string token, ref string remaining) {
        ReadOnlySpan<string> compactCommands = [
            "CD", "CHDIR", "MD", "MKDIR", "RD", "RMDIR", "DIR"
        ];
        for (int i = 0; i < compactCommands.Length; i++) {
            string cmd = compactCommands[i];
            if (token.Length > cmd.Length &&
                token.StartsWith(cmd, StringComparison.OrdinalIgnoreCase) &&
                (token[cmd.Length] == '.' || token[cmd.Length] == '\\' || token[cmd.Length] == '/')) {
                string extraArg = token[cmd.Length..];
                token = cmd;
                remaining = remaining.Length > 0 && !char.IsWhiteSpace(remaining[0])
                    ? extraArg + " " + remaining
                    : extraArg + remaining;
                return;
            }
        }
    }

    private static string TrimLeadingEqualsAndWhitespace(string text) {
        int i = 0;
        while (i < text.Length) {
            char c = text[i];
            if (char.IsWhiteSpace(c) || c == '=') {
                i++;
                continue;
            }

            break;
        }

        return i == 0 ? text : text[i..];
    }

    private static bool TryConsumeKeyword(ref string text, string keyword) {
        string trimmed = text.TrimStart();
        if (!trimmed.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        int keywordLength = keyword.Length;
        if (trimmed.Length > keywordLength && !char.IsWhiteSpace(trimmed[keywordLength])) {
            return false;
        }

        text = trimmed[keywordLength..].TrimStart();
        return true;
    }

    private static int FindClosingParenthesisOutsideQuotes(string text) {
        bool inQuotes = false;
        for (int i = 1; i < text.Length; i++) {
            char current = text[i];
            if (current == '"') {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && current == ')') {
                return i;
            }
        }

        return -1;
    }

    private static string[] ParseArguments(string tail) {
        List<string> result = new();
        string remaining = tail.TrimStart();
        while (TryExtractFirstToken(remaining, out string token, out string next)) {
            result.Add(token);
            remaining = next;
            if (string.IsNullOrWhiteSpace(remaining)) {
                break;
            }
        }

        return [.. result];
    }

    private static string JoinArguments(string[] arguments) {
        if (arguments.Length == 0) {
            return string.Empty;
        }

        StringBuilder builder = new();
        for (int i = 0; i < arguments.Length; i++) {
            if (i > 0) {
                builder.Append(' ');
            }

            builder.Append(EscapeIfNeeded(arguments[i]));
        }

        return builder.ToString();
    }

    private static bool TryGetForVariable(string token, out char variable) {
        variable = '\0';
        if (token.Length < 2 || token[0] != '%') {
            return false;
        }

        int variableIndex = token[1] == '%' ? 2 : 1;
        if (token.Length <= variableIndex) {
            return false;
        }

        variable = char.ToUpperInvariant(token[variableIndex]);
        return char.IsLetterOrDigit(variable);
    }

    private string[] ParseForList(string listSegment) {
        List<string> values = new();
        string[] split = SplitForListItemsRespectingQuotes(listSegment);
        for (int i = 0; i < split.Length; i++) {
            string item = split[i];
            if (item.Contains('*') || item.Contains('?')) {
                string[] matchingFiles = _dosFileManager.FindMatchingFileNames(item);
                for (int j = 0; j < matchingFiles.Length; j++) {
                    values.Add(matchingFiles[j]);
                }
            } else {
                values.Add(item);
            }
        }

        return [.. values];
    }

    private static string[] SplitForListItemsRespectingQuotes(string listSegment) {
        List<string> items = new();
        StringBuilder current = new();
        bool inQuotes = false;

        for (int i = 0; i < listSegment.Length; i++) {
            char ch = listSegment[i];
            if (ch == '"') {
                inQuotes = !inQuotes;
                current.Append(ch);
                continue;
            }

            bool isSeparator = !inQuotes && (ch == ' ' || ch == '\t' || ch == ',' || ch == ';' || ch == '=');
            if (isSeparator) {
                AddCurrentListTokenIfAny(items, current);
                continue;
            }

            current.Append(ch);
        }

        AddCurrentListTokenIfAny(items, current);
        return [.. items];
    }

    private static void AddCurrentListTokenIfAny(List<string> items, StringBuilder current) {
        if (current.Length == 0) {
            return;
        }

        items.Add(current.ToString());
        current.Clear();
    }

    private static string ReplaceForVariable(string template, char variableName, string value) {
        StringBuilder builder = new();
        int i = 0;
        while (i < template.Length) {
            char current = template[i];
            if (current == '%' && i + 1 < template.Length) {
                char next = template[i + 1];
                if (char.ToUpperInvariant(next) == variableName) {
                    builder.Append(value);
                    i += 2;
                    continue;
                }

                if (next == '%' && i + 2 < template.Length && char.ToUpperInvariant(template[i + 2]) == variableName) {
                    builder.Append(value);
                    i += 3;
                    continue;
                }
            }

            builder.Append(current);
            i++;
        }

        return builder.ToString();
    }

    private string ExpandBatchLine(string line, BatchFileContext context) {
        StringBuilder builder = new();
        int i = 0;
        while (i < line.Length) {
            char current = line[i];
            if (current != '%') {
                builder.Append(current);
                i++;
                continue;
            }

            if (i + 1 >= line.Length) {
                builder.Append('%');
                i++;
                continue;
            }

            char marker = line[i + 1];
            if (marker == '%') {
                builder.Append('%');
                i += 2;
                continue;
            }

            if (marker >= '0' && marker <= '9') {
                int index = marker - '0';
                string argValue = context.GetArgument(index);
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("BATCH: Expand %%{Index} -> {Value}", index, argValue);
                }
                builder.Append(argValue);
                i += 2;
                continue;
            }

            int closingPercent = line.IndexOf('%', i + 1);
            if (closingPercent < 0) {
                builder.Append('%');
                i++;
                continue;
            }

            string variableName = line[(i + 1)..closingPercent];
            string? environmentValue = _host.TryGetEnvironmentVariable(variableName);
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("BATCH: Expand %%{VarName}%% -> {Value}", variableName, environmentValue ?? "(null)");
            }
            if (!string.IsNullOrEmpty(environmentValue)) {
                builder.Append(environmentValue);
            }

            i = closingPercent + 1;
        }

        return builder.ToString();
    }

    private bool DoesFileExist(string dosPath) {
        string normalizedPath = NormalizeDosPath(Unquote(dosPath));
        if (_zDriveFiles.ContainsKey(normalizedPath)) {
            return true;
        }

        string? hostPath = _dosFileManager.TryGetFullHostPathFromDos(normalizedPath);
        return !string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath);
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

    private bool TryApplyRedirection(CommandRedirection redirection) {
        if (!string.IsNullOrWhiteSpace(redirection.InputPath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("BATCH: Redirecting stdin from {Path}", redirection.InputPath);
            }
            if (!TryRedirectStandardInput(redirection.InputPath)) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("BATCH: Failed to redirect stdin from {Path}", redirection.InputPath);
                }
                RestoreStandardHandlesAfterLaunch();
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(redirection.OutputPath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("BATCH: Redirecting stdout to {Path} (append={Append})", redirection.OutputPath, redirection.AppendOutput);
            }
            if (!TryRedirectStandardOutput(redirection.OutputPath, redirection.AppendOutput, (ushort)DosStandardHandle.Stdout)) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("BATCH: Failed to redirect stdout to {Path}", redirection.OutputPath);
                }
                RestoreStandardHandlesAfterLaunch();
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(redirection.ErrorPath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("BATCH: Redirecting stderr to {Path} (append={Append})", redirection.ErrorPath, redirection.AppendError);
            }
            if (!TryRedirectStandardOutput(redirection.ErrorPath, redirection.AppendError, (ushort)DosStandardHandle.Stderr)) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("BATCH: Failed to redirect stderr to {Path}", redirection.ErrorPath);
                }
                RestoreStandardHandlesAfterLaunch();
                return false;
            }
        }

        return true;
    }

    private bool TryRedirectStandardInput(string dosPath) {
        DosFileOperationResult openResult = _dosFileManager.OpenFileOrDevice(dosPath, FileAccessMode.ReadOnly);
        if (openResult.IsError || openResult.Value == null) {
            return false;
        }

        return TryMoveHandleToStandard((ushort)openResult.Value.Value, (ushort)DosStandardHandle.Stdin);
    }

    private bool TryRedirectStandardOutput(string dosPath, bool append, ushort standardHandle) {
        DosFileOperationResult openResult;
        bool fileAlreadyExisted = false;
        if (append) {
            openResult = _dosFileManager.OpenFileOrDevice(dosPath, FileAccessMode.WriteOnly);
            if (!openResult.IsError && openResult.Value != null) {
                fileAlreadyExisted = true;
            } else {
                openResult = _dosFileManager.CreateFileUsingHandle(dosPath, 0);
            }
        } else {
            openResult = _dosFileManager.CreateFileUsingHandle(dosPath, 0);
        }

        if (openResult.IsError || openResult.Value == null) {
            return false;
        }

        ushort openedHandle = (ushort)openResult.Value.Value;
        if (append && fileAlreadyExisted) {
            DosFileOperationResult seekResult = _dosFileManager.MoveFilePointerUsingHandle(SeekOrigin.End, openedHandle, 0);
            if (seekResult.IsError) {
                _dosFileManager.CloseFileOrDevice(openedHandle);
                return false;
            }
        }

        return TryMoveHandleToStandard(openedHandle, standardHandle);
    }

    private bool TryMoveHandleToStandard(ushort sourceHandle, ushort standardHandle) {
        VirtualFileBase? redirectedFile = _dosFileManager.OpenFiles[sourceHandle];
        if (redirectedFile == null) {
            return false;
        }

        TrackOriginalStandardHandle(standardHandle);

        _dosFileManager.OpenFiles[standardHandle] = redirectedFile;
        _dosFileManager.OpenFiles[sourceHandle] = null;
        return true;
    }

    private void TrackOriginalStandardHandle(ushort standardHandle) {
        switch ((DosStandardHandle)standardHandle) {
            case DosStandardHandle.Stdin:
                if (!_stdinRedirected) {
                    _savedStandardInput = _dosFileManager.OpenFiles[(ushort)DosStandardHandle.Stdin];
                    _stdinRedirected = true;
                }
                break;
            case DosStandardHandle.Stdout:
                if (!_stdoutRedirected) {
                    _savedStandardOutput = _dosFileManager.OpenFiles[(ushort)DosStandardHandle.Stdout];
                    _stdoutRedirected = true;
                }
                break;
            case DosStandardHandle.Stderr:
                if (!_stderrRedirected) {
                    _savedStandardError = _dosFileManager.OpenFiles[(ushort)DosStandardHandle.Stderr];
                    _stderrRedirected = true;
                }
                break;
        }
    }

    private void CloseRedirectedStandardHandle(ushort handle) {
        VirtualFileBase? redirectedHandle = _dosFileManager.OpenFiles[handle];
        if (redirectedHandle is DosFile) {
            _dosFileManager.CloseFileOrDevice(handle);
        } else {
            _dosFileManager.OpenFiles[handle] = null;
        }
    }

    private static CommandRedirection MergeRedirections(CommandRedirection current, CommandRedirection inherited) {
        string inputPath = string.IsNullOrWhiteSpace(current.InputPath) ? inherited.InputPath : current.InputPath;
        string outputPath = string.IsNullOrWhiteSpace(current.OutputPath) ? inherited.OutputPath : current.OutputPath;
        bool appendOutput = string.IsNullOrWhiteSpace(current.OutputPath) ? inherited.AppendOutput : current.AppendOutput;
        string errorPath = string.IsNullOrWhiteSpace(current.ErrorPath) ? inherited.ErrorPath : current.ErrorPath;
        bool appendError = string.IsNullOrWhiteSpace(current.ErrorPath) ? inherited.AppendError : current.AppendError;

        return new CommandRedirection(inputPath, outputPath, appendOutput, errorPath, appendError);
    }

    internal static string AppendRedirection(string command, CommandRedirection redirection) {
        if (!redirection.HasAny) {
            return command;
        }

        StringBuilder builder = new(command);
        if (!string.IsNullOrWhiteSpace(redirection.InputPath)) {
            builder.Append(' ');
            builder.Append('<');
            builder.Append(' ');
            builder.Append(EscapeIfNeeded(redirection.InputPath));
        }

        if (!string.IsNullOrWhiteSpace(redirection.OutputPath)) {
            builder.Append(' ');
            builder.Append(redirection.AppendOutput ? ">>" : ">");
            builder.Append(' ');
            builder.Append(EscapeIfNeeded(redirection.OutputPath));
        }

        if (!string.IsNullOrWhiteSpace(redirection.ErrorPath)) {
            builder.Append(' ');
            builder.Append('2');
            builder.Append(redirection.AppendError ? ">>" : ">");
            builder.Append(' ');
            builder.Append(EscapeIfNeeded(redirection.ErrorPath));
        }

        return builder.ToString();
    }

    private static bool TryParseCommandLine(string commandLine, out ParsedCommandLine parsedCommandLine) {
        parsedCommandLine = default;
        StringBuilder commandBuilder = new();
        RedirectionBuilder redirectionBuilder = new();
        bool inQuotes = false;

        int i = 0;
        while (i < commandLine.Length) {
            char current = commandLine[i];
            if (current == '"') {
                inQuotes = !inQuotes;
                commandBuilder.Append(current);
                i++;
                continue;
            }

            if (!inQuotes && current == '|') {
                break;
            }

            if (!inQuotes && IsRedirectionStart(commandLine, i)) {
                if (!TryReadRedirection(commandLine, ref i, redirectionBuilder)) {
                    return false;
                }

                continue;
            }

            commandBuilder.Append(current);
            i++;
        }

        string commandWithoutRedirection = commandBuilder.ToString();
        parsedCommandLine = new ParsedCommandLine(commandWithoutRedirection, redirectionBuilder.Build());
        return !string.IsNullOrWhiteSpace(commandWithoutRedirection);
    }

    private static bool IsRedirectionStart(string commandLine, int index) {
        char current = commandLine[index];
        if (current == '>' || current == '<') {
            return true;
        }

        if (!char.IsDigit(current)) {
            return false;
        }

        return index + 1 < commandLine.Length && (commandLine[index + 1] == '>' || commandLine[index + 1] == '<');
    }

    private static bool ContainsPipeOutsideQuotes(string commandLine) {
        bool inQuotes = false;
        for (int i = 0; i < commandLine.Length; i++) {
            char current = commandLine[i];
            if (current == '"') {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && current == '|') {
                return true;
            }
        }

        return false;
    }

    private static bool TrySplitPipelineSegments(string commandLine, out string[] segments) {
        segments = Array.Empty<string>();
        bool inQuotes = false;
        int start = 0;
        List<string> parsedSegments = new();

        for (int i = 0; i < commandLine.Length; i++) {
            char current = commandLine[i];
            if (current == '"') {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && current == '|') {
                string segment = commandLine[start..i].Trim();
                if (segment.Length == 0) {
                    return false;
                }

                parsedSegments.Add(segment);
                start = i + 1;
            }
        }

        if (parsedSegments.Count == 0) {
            return false;
        }

        string lastSegment = commandLine[start..].Trim();
        if (lastSegment.Length == 0) {
            return false;
        }

        parsedSegments.Add(lastSegment);
        segments = [.. parsedSegments];
        return true;
    }

    private static bool TryReadRedirection(string commandLine, ref int index, RedirectionBuilder redirectionBuilder) {
        int originalIndex = index;
        int descriptor = -1;
        if (char.IsDigit(commandLine[index]) && index + 1 < commandLine.Length &&
            (commandLine[index + 1] == '>' || commandLine[index + 1] == '<')) {
            descriptor = commandLine[index] - '0';
            index++;
        }

        char operation = commandLine[index];
        if (operation != '>' && operation != '<') {
            index = originalIndex;
            return false;
        }

        bool append = false;
        if (index + 1 < commandLine.Length && commandLine[index + 1] == operation) {
            append = true;
            index++;
        }

        index++;
        while (index < commandLine.Length && char.IsWhiteSpace(commandLine[index])) {
            index++;
        }

        if (!TryReadRedirectionTarget(commandLine, ref index, out string target)) {
            index = originalIndex;
            return false;
        }

        string normalizedTarget = NormalizeRedirectionTarget(Unquote(target));
        if (string.IsNullOrWhiteSpace(normalizedTarget)) {
            return true;
        }

        if (operation == '<') {
            if (descriptor == -1 || descriptor == 0) {
                redirectionBuilder.SetInput(normalizedTarget);
            }
            return true;
        }

        int outputDescriptor = descriptor == -1 ? 1 : descriptor;
        if (outputDescriptor == 1) {
            redirectionBuilder.SetOutput(normalizedTarget, append);
        } else if (outputDescriptor == 2) {
            redirectionBuilder.SetError(normalizedTarget, append);
        }

        return true;
    }

    private static string NormalizeRedirectionTarget(string target) {
        string normalized = target.Trim();
        if (normalized.Length > 1 && normalized.EndsWith(":", StringComparison.Ordinal) &&
            normalized.IndexOf('\\') < 0 && normalized.IndexOf('/') < 0 && normalized.IndexOf(':') == normalized.Length - 1) {
            normalized = normalized[..^1];
        }

        return normalized;
    }

    private static bool TryReadRedirectionTarget(string commandLine, ref int index, out string target) {
        target = string.Empty;
        if (index >= commandLine.Length) {
            return false;
        }

        if (commandLine[index] == '"') {
            int start = index;
            index++;
            while (index < commandLine.Length && commandLine[index] != '"') {
                index++;
            }

            if (index >= commandLine.Length) {
                return false;
            }

            index++;
            target = commandLine[start..index];
            return true;
        }

        int tokenStart = index;
        while (index < commandLine.Length) {
            char current = commandLine[index];
            if (char.IsWhiteSpace(current) || current == '|' || current == '<' || current == '>') {
                break;
            }

            index++;
        }

        if (index == tokenStart) {
            return false;
        }

        target = commandLine[tokenStart..index];
        return true;
    }

    private static string Unquote(string value) {
        string trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"') {
            return trimmed[1..^1];
        }

        return trimmed;
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

    private static string NormalizeDosPath(string dosPath) {
        string normalized = dosPath.Trim().Replace('/', '\\');
        if (normalized.Length < 2 || normalized[1] != ':') {
            return normalized;
        }

        char drive = char.ToUpperInvariant(normalized[0]);
        string rest = normalized[2..];
        if (rest.Length == 0) {
            rest = "\\";
        } else if (rest[0] != '\\') {
            rest = "\\" + rest;
        }

        return $"{drive}:{rest}";
    }
}