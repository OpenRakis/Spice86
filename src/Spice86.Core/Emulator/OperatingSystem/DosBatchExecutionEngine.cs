namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal sealed class DosBatchExecutionEngine {
    private const string AutoExecPath = "Z:\\AUTOEXEC.BAT";

    private readonly DosFileManager _dosFileManager;
    private readonly Func<string, string?> _environmentValueAccessor;
    private readonly Func<IReadOnlyList<KeyValuePair<string, string>>> _environmentSnapshotAccessor;
    private readonly Func<string, string, bool> _environmentValueSetter;
    private readonly ILoggerService _loggerService;
    private readonly DosDriveManager? _driveManager;
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

    internal DosBatchExecutionEngine(DosFileManager dosFileManager,
        Func<string, string?> environmentValueAccessor,
        Func<IReadOnlyList<KeyValuePair<string, string>>> environmentSnapshotAccessor,
        Func<string, string, bool> environmentValueSetter,
            ILoggerService loggerService,
            DosDriveManager? driveManager = null) {
        _dosFileManager = dosFileManager;
        _environmentValueAccessor = environmentValueAccessor;
        _environmentSnapshotAccessor = environmentSnapshotAccessor;
        _environmentValueSetter = environmentValueSetter;
        _loggerService = loggerService;
        _driveManager = driveManager;
    }

    internal readonly struct LaunchRequest {
        internal LaunchRequest(string programName, string commandTail, CommandRedirection redirection) {
            ProgramName = programName;
            CommandTail = commandTail;
            Redirection = redirection;
        }

        internal string ProgramName { get; }
        internal string CommandTail { get; }
        internal CommandRedirection Redirection { get; }
    }

    internal readonly struct CommandRedirection {
        internal CommandRedirection(string inputPath, string outputPath, bool appendOutput, string errorPath, bool appendError) {
            InputPath = inputPath;
            OutputPath = outputPath;
            AppendOutput = appendOutput;
            ErrorPath = errorPath;
            AppendError = appendError;
        }

        internal string InputPath { get; }
        internal string OutputPath { get; }
        internal bool AppendOutput { get; }
        internal string ErrorPath { get; }
        internal bool AppendError { get; }
        internal bool HasAny => !string.IsNullOrWhiteSpace(InputPath) || !string.IsNullOrWhiteSpace(OutputPath) || !string.IsNullOrWhiteSpace(ErrorPath);
    }

    internal void ConfigureHostStartupProgram(string requestedProgramDosPath, string commandTail) {
        string line = BuildCallLine(requestedProgramDosPath, commandTail);
        _zDriveFiles[AutoExecPath] = new[] { line };
        SyncAutoexecBatToMemoryDrive(new[] { line });
        _batchFileContexts.Clear();
    }

    internal bool TryStart(out LaunchRequest launchRequest) {
        _batchFileContexts.Clear();
        _lastExitCode = 0;
        return TryExecuteCommandLine($"CALL {AutoExecPath}", out launchRequest);
    }

    internal bool TryContinue(ushort lastChildReturnCode, out LaunchRequest launchRequest) {
        _lastExitCode = (byte)(lastChildReturnCode & 0x00FF);
        return TryPump(out launchRequest);
    }

    internal bool TryApplyRedirectionForLaunch(LaunchRequest launchRequest) {
        RestoreStandardHandlesAfterLaunch();

        if (!launchRequest.Redirection.HasAny) {
            return true;
        }

        return TryApplyRedirection(launchRequest.Redirection);
    }

    internal void RestoreStandardHandlesAfterLaunch() {
        if (_stdinRedirected) {
            CloseRedirectedStandardHandle(0);
            _dosFileManager.OpenFiles[0] = _savedStandardInput;
            _savedStandardInput = null;
            _stdinRedirected = false;
        }

        if (_stdoutRedirected) {
            CloseRedirectedStandardHandle(1);
            _dosFileManager.OpenFiles[1] = _savedStandardOutput;
            _savedStandardOutput = null;
            _stdoutRedirected = false;
        }

        if (_stderrRedirected) {
            CloseRedirectedStandardHandle(2);
            _dosFileManager.OpenFiles[2] = _savedStandardError;
            _savedStandardError = null;
            _stderrRedirected = false;
        }
    }

    private bool TryPump(out LaunchRequest launchRequest) {
        launchRequest = default;

        while (_batchFileContexts.Count > 0) {
            BatchFileContext current = _batchFileContexts.Peek();
            if (!current.TryReadNextLine(out string line)) {
                CleanupTemporaryFiles(current.TemporaryFilesToCleanup);
                _batchFileContexts.Pop();
                continue;
            }

            string expandedLine = ExpandBatchLine(line, current);
            if (expandedLine.StartsWith('@')) {
                expandedLine = expandedLine[1..];
            }

            if (IsSkippableBatchLine(expandedLine)) {
                continue;
            }

            if (TryExecuteCommandLine(expandedLine, out launchRequest)) {
                return true;
            }
        }

        return false;
    }

    private bool TryExecuteCommandLine(string commandLine, out LaunchRequest launchRequest) {
        launchRequest = default;

        string[] pipelineSegments = Array.Empty<string>();
        bool hasPipe = ContainsPipeOutsideQuotes(commandLine);
        if (hasPipe && !TrySplitPipelineSegments(commandLine, out pipelineSegments)) {
            return false;
        }

        if (hasPipe && pipelineSegments.Length > 1) {
            return TryHandlePipeline(pipelineSegments, out launchRequest);
        }

        if (!TryParseCommandLine(commandLine, out ParsedCommandLine parsedCommandLine)) {
            return false;
        }

        string preprocessedLine = parsedCommandLine.CommandLineWithoutRedirection;

        if (!TryExtractCommandToken(preprocessedLine, out string commandToken, out string argumentPart)) {
            return false;
        }

        string resolvedCommandToken = ResolveCommandTokenForCurrentBatchContext(commandToken);

        if (string.Equals(resolvedCommandToken, "REM", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (string.Equals(resolvedCommandToken, "CALL", StringComparison.OrdinalIgnoreCase)) {
            return TryHandleCall(argumentPart, parsedCommandLine.Redirection, out launchRequest);
        }

        if (string.Equals(resolvedCommandToken, "GOTO", StringComparison.OrdinalIgnoreCase)) {
            return TryHandleGoto(argumentPart);
        }

        if (string.Equals(resolvedCommandToken, "SHIFT", StringComparison.OrdinalIgnoreCase)) {
            return TryHandleShift();
        }

        if (string.Equals(resolvedCommandToken, "IF", StringComparison.OrdinalIgnoreCase)) {
            return TryHandleIf(argumentPart, parsedCommandLine.Redirection, out launchRequest);
        }

        if (string.Equals(resolvedCommandToken, "FOR", StringComparison.OrdinalIgnoreCase)) {
            return TryHandleFor(argumentPart, parsedCommandLine.Redirection, out launchRequest);
        }

        if (string.Equals(resolvedCommandToken, "SET", StringComparison.OrdinalIgnoreCase)) {
            return TryExecuteInternalCommandWithRedirection(parsedCommandLine.Redirection,
                () => TryHandleSet(argumentPart));
        }

        if (string.Equals(resolvedCommandToken, "ECHO", StringComparison.OrdinalIgnoreCase)) {
            return TryExecuteInternalCommandWithRedirection(parsedCommandLine.Redirection,
                () => TryHandleEcho(argumentPart));
        }

        // Handle ECHO.TEXT shorthand: "ECHO.TEXT" is equivalent to "ECHO TEXT" in DOS batch.
        // Preserve all content after "ECHO" from the original preprocessed line to keep embedded spaces.
        if (resolvedCommandToken.StartsWith("ECHO.", StringComparison.OrdinalIgnoreCase)) {
            string echoArgs = preprocessedLine.TrimStart()[4..];
            return TryExecuteInternalCommandWithRedirection(parsedCommandLine.Redirection,
                () => TryHandleEcho(echoArgs));
        }

        if (TryResolveBatchCommandPath(resolvedCommandToken, out string batchCommandPath)) {
            if (TryPushBatchFile(batchCommandPath, Array.Empty<string>())) {
                return TryPump(out launchRequest);
            }

            return false;
        }

        launchRequest = new LaunchRequest(resolvedCommandToken, argumentPart, parsedCommandLine.Redirection);
        return true;
    }

    private bool TryExecuteInternalCommandWithRedirection(CommandRedirection redirection, Func<bool> internalCommand) {
        if (redirection.HasAny && !TryApplyRedirectionForLaunch(new LaunchRequest(string.Empty, string.Empty, redirection))) {
            return false;
        }

        bool result = internalCommand();
        if (redirection.HasAny) {
            RestoreStandardHandlesAfterLaunch();
        }

        return result;
    }

    private bool TryHandleSet(string arguments) {
        string trimmedArguments = arguments.TrimStart();
        if (trimmedArguments.Length == 0) {
            IReadOnlyList<KeyValuePair<string, string>> entries = _environmentSnapshotAccessor();
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

            _ = _environmentValueSetter(name, value);
            return false;
        }

        IReadOnlyList<KeyValuePair<string, string>> variables = _environmentSnapshotAccessor();
        for (int i = 0; i < variables.Count; i++) {
            KeyValuePair<string, string> variable = variables[i];
            if (variable.Key.StartsWith(trimmedArguments, StringComparison.OrdinalIgnoreCase)) {
                WriteToStandardOutput($"{variable.Key}={variable.Value}\r\n");
            }
        }

        return false;
    }

    private bool TryHandleEcho(string arguments) {
        string trimmedArguments = arguments.TrimStart();
        if (trimmedArguments.Length == 0) {
            WriteToStandardOutput(_echoEnabled ? "ECHO is ON.\r\n" : "ECHO is OFF.\r\n");
            return false;
        }

        if (string.Equals(trimmedArguments, "ON", StringComparison.OrdinalIgnoreCase)) {
            _echoEnabled = true;
            return false;
        }

        if (string.Equals(trimmedArguments, "OFF", StringComparison.OrdinalIgnoreCase)) {
            _echoEnabled = false;
            return false;
        }

        string outputText = trimmedArguments;
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

    private bool TryHandlePipeline(string[] pipelineSegments, out LaunchRequest launchRequest) {
        launchRequest = default;

        if (!TryBuildPipelineCommands(pipelineSegments, out string[] generatedCommands, out string[] temporaryDosFiles)) {
            return false;
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
                CleanupTemporaryFiles(tempFiles.ToArray());
                return false;
            }

            tempFiles.Add(tempDosFile);
        }

        string[] commands = new string[pipelineSegments.Length];
        for (int i = 0; i < pipelineSegments.Length; i++) {
            string segment = pipelineSegments[i].Trim();
            if (segment.Length == 0) {
                CleanupTemporaryFiles(tempFiles.ToArray());
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
        temporaryDosFiles = tempFiles.ToArray();
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
        for (int i = 0; i < temporaryDosFiles.Length; i++) {
            string? hostPath = _dosFileManager.TryGetFullHostPathFromDos(temporaryDosFiles[i]);
            if (string.IsNullOrWhiteSpace(hostPath)) {
                continue;
            }

            try {
                if (File.Exists(hostPath)) {
                    File.Delete(hostPath);
                }
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            } catch (ArgumentException) {
            } catch (NotSupportedException) {
            }
        }
    }

    private static string BuildTemporaryPipeFilePath(int index) {
        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        string fileName = $"P{index % 10}{suffix}.TMP";
        return $"C:\\{fileName}";
    }

    private void WriteToStandardOutput(string text) {
        VirtualFileBase? output = _dosFileManager.OpenFiles[1];
        if (output == null) {
            return;
        }

        byte[] bytes = Encoding.ASCII.GetBytes(text);
        output.Write(bytes, 0, bytes.Length);
    }

    private bool TryHandleCall(string arguments, CommandRedirection commandRedirection, out LaunchRequest launchRequest) {
        launchRequest = default;

        if (!TryExtractFirstToken(arguments, out string targetToken, out string tail)) {
            return false;
        }

        string resolvedTargetToken = ResolveCommandTokenForCurrentBatchContext(targetToken);

        if (resolvedTargetToken.StartsWith(':')) {
            return false;
        }

        string[] callArguments = ParseArguments(tail);

        if (TryResolveBatchCommandPath(resolvedTargetToken, out string batchTargetPath)) {
            if (!TryPushBatchFile(batchTargetPath, callArguments)) {
                _lastExitCode = 1;
                return false;
            }

            return TryPump(out launchRequest);
        }

        launchRequest = new LaunchRequest(resolvedTargetToken, JoinArguments(callArguments), commandRedirection);
        return true;
    }

    private bool TryHandleGoto(string arguments) {
        if (_batchFileContexts.Count == 0) {
            return false;
        }

        string label = arguments.Trim();
        if (label.StartsWith(':')) {
            label = label[1..];
        }

        if (string.IsNullOrWhiteSpace(label)) {
            return false;
        }

        BatchFileContext context = _batchFileContexts.Peek();
        _ = context.TryGoto(label);
        return false;
    }

    private bool TryHandleShift() {
        if (_batchFileContexts.Count == 0) {
            return false;
        }

        BatchFileContext context = _batchFileContexts.Peek();
        context.Shift();
        return false;
    }

    private bool TryHandleIf(string arguments, CommandRedirection inheritedRedirection, out LaunchRequest launchRequest) {
        launchRequest = default;

        string working = arguments.TrimStart();
        bool hasNot = TryConsumeKeyword(ref working, "NOT");

        if (TryConsumeKeyword(ref working, "ERRORLEVEL")) {
            if (!TryExtractFirstToken(working, out string levelToken, out string commandPart)) {
                return false;
            }

            if (!int.TryParse(levelToken, out int threshold)) {
                return false;
            }

            bool condition = _lastExitCode >= threshold;
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
        if (!TryExtractComparisonToken(rightAndCommand, out string rightToken, out string commandSegment)) {
            return false;
        }

        string normalizedLeft = Unquote(left);
        string normalizedRight = Unquote(rightToken);
        bool equals = string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        if (equals != hasNot) {
            bool launched = TryExecuteCommandLine(commandSegment, out launchRequest);
            return TryApplyInheritedRedirection(inheritedRedirection, launched, ref launchRequest);
        }

        return false;
    }

    private static bool TryExtractComparisonToken(string input, out string token, out string remaining) {
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

            token = trimmed.Substring(1, closingQuote - 1);
            remaining = trimmed[(closingQuote + 1)..].TrimStart();
            return true;
        }

        return TryExtractFirstToken(trimmed, out token, out remaining);
    }

    private bool TryHandleFor(string arguments, CommandRedirection commandRedirection, out LaunchRequest launchRequest) {
        launchRequest = default;

        string working = arguments.TrimStart();
        if (!TryExtractFirstToken(working, out string variableToken, out string restAfterVariable)) {
            return false;
        }

        if (!TryGetForVariable(variableToken, out char variableName)) {
            return false;
        }

        string afterIn = restAfterVariable;
        if (!TryConsumeKeyword(ref afterIn, "IN")) {
            return false;
        }

        string inSegment = afterIn.TrimStart();
        if (inSegment.Length < 2 || inSegment[0] != '(') {
            return false;
        }

        int closeParen = inSegment.IndexOf(')');
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

        _batchFileContexts.Push(new BatchFileContext("<FOR>", generatedCommands, Array.Empty<string>(), Array.Empty<string>()));
        return TryPump(out launchRequest);
    }

    private bool TryPushBatchFile(string dosPath, string[] arguments) {
        if (!TryReadBatchFile(dosPath, out string[] lines)) {
            return false;
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

        string? hostPath = _dosFileManager.TryGetFullHostExecutablePathFromDos(normalizedPath);
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("BATCH: could not open file {DosPath}", normalizedPath);
            }

            return false;
        }

        try {
            lines = File.ReadAllLines(hostPath, Encoding.ASCII);
            return true;
        } catch (IOException) {
            return false;
        } catch (UnauthorizedAccessException) {
            return false;
        } catch (ArgumentException) {
            return false;
        } catch (NotSupportedException) {
            return false;
        }
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
            return true;
        }

        if (commandToken.Contains('*') || commandToken.Contains('?') || Path.HasExtension(commandToken)) {
            return false;
        }

        string? hostPath = _dosFileManager.TryGetFullHostExecutablePathFromDos(commandToken);
        if (string.IsNullOrWhiteSpace(hostPath)) {
            return false;
        }

        return string.Equals(Path.GetExtension(hostPath), ".BAT", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveCommandTokenForCurrentBatchContext(string commandToken) {
        if (!IsRelativeCommandToken(commandToken)) {
            return commandToken;
        }

        if (_batchFileContexts.Count == 0) {
            return commandToken;
        }

        BatchFileContext context = _batchFileContexts.Peek();
        string? directoryPath = context.TryGetContainingDirectory();
        if (string.IsNullOrWhiteSpace(directoryPath)) {
            return commandToken;
        }

        string candidate = NormalizeDosPath($"{directoryPath}\\{commandToken}");
        string? hostPath = _dosFileManager.TryGetFullHostExecutablePathFromDos(candidate);
        return string.IsNullOrWhiteSpace(hostPath) ? commandToken : candidate;
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

        int separatorIndex = trimmed.IndexOfAny(new[] { ' ', '\t' });
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
            remaining = trimmed[(closingQuote + 1)..].TrimStart();
            return !string.IsNullOrWhiteSpace(token);
        }

        int separatorIndex = trimmed.IndexOfAny(new[] { ' ', '\t' });
        if (separatorIndex < 0) {
            token = trimmed;
            return true;
        }

        token = trimmed[..separatorIndex];
        remaining = trimmed[separatorIndex..].TrimStart();

        return true;
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

        return result.ToArray();
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

    private static string[] ParseForList(string listSegment) {
        List<string> values = new();
        string[] split = listSegment.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < split.Length; i++) {
            values.Add(split[i]);
        }

        return values.ToArray();
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
                builder.Append(context.GetArgument(index));
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
            string? environmentValue = _environmentValueAccessor(variableName);
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
        launchRequest = new LaunchRequest(launchRequest.ProgramName, launchRequest.CommandTail, merged);
        return true;
    }

    private bool TryApplyRedirection(CommandRedirection redirection) {
        if (!string.IsNullOrWhiteSpace(redirection.InputPath) &&
            !TryRedirectStandardInput(redirection.InputPath)) {
            RestoreStandardHandlesAfterLaunch();
            return false;
        }

        if (!string.IsNullOrWhiteSpace(redirection.OutputPath) &&
            !TryRedirectStandardOutput(redirection.OutputPath, redirection.AppendOutput, 1)) {
            RestoreStandardHandlesAfterLaunch();
            return false;
        }

        if (!string.IsNullOrWhiteSpace(redirection.ErrorPath) &&
            !TryRedirectStandardOutput(redirection.ErrorPath, redirection.AppendError, 2)) {
            RestoreStandardHandlesAfterLaunch();
            return false;
        }

        return true;
    }

    private bool TryRedirectStandardInput(string dosPath) {
        DosFileOperationResult openResult = _dosFileManager.OpenFileOrDevice(dosPath, FileAccessMode.ReadOnly);
        if (openResult.IsError || openResult.Value == null) {
            return false;
        }

        return TryMoveHandleToStandard((ushort)openResult.Value.Value, 0);
    }

    private bool TryRedirectStandardOutput(string dosPath, bool append, ushort standardHandle) {
        DosFileOperationResult openResult;
        if (append) {
            openResult = _dosFileManager.OpenFileOrDevice(dosPath, FileAccessMode.WriteOnly);
        } else {
            openResult = _dosFileManager.CreateFileUsingHandle(dosPath, 0);
        }

        if (openResult.IsError || openResult.Value == null) {
            return false;
        }

        ushort openedHandle = (ushort)openResult.Value.Value;
        if (append) {
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

        if (standardHandle == 0 && !_stdinRedirected) {
            _savedStandardInput = _dosFileManager.OpenFiles[0];
            _stdinRedirected = true;
        }

        if (standardHandle == 1 && !_stdoutRedirected) {
            _savedStandardOutput = _dosFileManager.OpenFiles[1];
            _stdoutRedirected = true;
        }

        if (standardHandle == 2 && !_stderrRedirected) {
            _savedStandardError = _dosFileManager.OpenFiles[2];
            _stderrRedirected = true;
        }

        _dosFileManager.OpenFiles[standardHandle] = redirectedFile;
        _dosFileManager.OpenFiles[sourceHandle] = null;
        return true;
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

    private static string AppendRedirection(string command, CommandRedirection redirection) {
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
        segments = parsedSegments.ToArray();
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
        if (_driveManager == null) {
            return;
        }

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

    private sealed class BatchFileContext {
        private readonly string _filePath;
        private readonly string[] _lines;
        private readonly List<string> _arguments;
        private readonly string[] _temporaryFilesToCleanup;
        private int _lineIndex;

        internal BatchFileContext(string filePath, string[] lines, string[] arguments, string[] temporaryFilesToCleanup) {
            _filePath = filePath;
            _lines = lines;
            _arguments = new List<string>(arguments);
            _temporaryFilesToCleanup = temporaryFilesToCleanup;
            _lineIndex = 0;
        }

        internal string[] TemporaryFilesToCleanup => _temporaryFilesToCleanup;

        internal bool TryReadNextLine(out string line) {
            line = string.Empty;

            if (_lineIndex >= _lines.Length) {
                return false;
            }

            line = _lines[_lineIndex];
            _lineIndex++;
            return true;
        }

        internal string GetArgument(int index) {
            if (index == 0) {
                return _filePath;
            }

            int argumentIndex = index - 1;
            if (argumentIndex < 0 || argumentIndex >= _arguments.Count) {
                return string.Empty;
            }

            return _arguments[argumentIndex];
        }

        internal void Shift() {
            if (_arguments.Count > 0) {
                _arguments.RemoveAt(0);
            }
        }

        internal bool TryGoto(string label) {
            string target = label.Trim();
            for (int i = 0; i < _lines.Length; i++) {
                string line = _lines[i].TrimStart();
                if (!line.StartsWith(':')) {
                    continue;
                }

                string candidate = line[1..].Trim();
                if (string.Equals(candidate, target, StringComparison.OrdinalIgnoreCase)) {
                    _lineIndex = i + 1;
                    return true;
                }
            }

            return false;
        }

        internal string? TryGetContainingDirectory() {
            if (string.IsNullOrWhiteSpace(_filePath) || _filePath.StartsWith('<')) {
                return null;
            }

            string normalizedPath = _filePath.Replace('/', '\\');
            string? containingDirectory = Path.GetDirectoryName(normalizedPath);
            return string.IsNullOrWhiteSpace(containingDirectory) ? null : containingDirectory;
        }
    }

    private readonly struct ParsedCommandLine {
        internal ParsedCommandLine(string commandLineWithoutRedirection, CommandRedirection redirection) {
            CommandLineWithoutRedirection = commandLineWithoutRedirection;
            Redirection = redirection;
        }

        internal string CommandLineWithoutRedirection { get; }
        internal CommandRedirection Redirection { get; }
    }

    private sealed class RedirectionBuilder {
        private string _inputPath = string.Empty;
        private string _outputPath = string.Empty;
        private bool _appendOutput;
        private string _errorPath = string.Empty;
        private bool _appendError;

        internal void SetInput(string path) {
            _inputPath = path;
        }

        internal void SetOutput(string path, bool append) {
            _outputPath = path;
            _appendOutput = append;
        }

        internal void SetError(string path, bool append) {
            _errorPath = path;
            _appendError = append;
        }

        internal CommandRedirection Build() {
            return new CommandRedirection(_inputPath, _outputPath, _appendOutput, _errorPath, _appendError);
        }
    }
}