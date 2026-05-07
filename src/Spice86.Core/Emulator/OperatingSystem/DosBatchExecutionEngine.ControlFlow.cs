namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using Serilog.Events;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System;
using System.Collections.Generic;
using System.Text;

internal sealed partial class DosBatchExecutionEngine {
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

        if (ResolveBatchCommandPath(resolvedTargetToken, out string batchTargetPath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: CALL - pushing batch file: '{Path}' with {ArgCount} args",
                    batchTargetPath, callArguments2.Length);
            }
            if (!PushBatchFile(batchTargetPath, callArguments2)) {
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

        for (int i = labelLineIndex + 1; i < lines.Length; i++) {
            string line = lines[i].TrimStart();
            if (line.StartsWith(':') && string.Equals(line[1..].Trim(), "EOF", StringComparison.OrdinalIgnoreCase)) {
                break;
            }

            subroutineLines.Add(lines[i]);
        }

        return [.. subroutineLines];
    }

    internal bool HandleGoto(string arguments) {
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
        if (!context.GoToLabel(label)) {
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

    internal bool HandleShift() {
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
        bool hasNot = ConsumeKeyword(ref working, "NOT");

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: IF not={Not} args={Args}", hasNot, working);
        }

        if (ConsumeKeyword(ref working, "ERRORLEVEL")) {
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
                return ApplyInheritedRedirection(inheritedRedirection, launched, ref launchRequest);
            }

            return false;
        }

        if (ConsumeKeyword(ref working, "EXIST")) {
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
                return ApplyInheritedRedirection(inheritedRedirection, launched, ref launchRequest);
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
            return ApplyInheritedRedirection(inheritedRedirection, launched, ref launchRequest);
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
        } else if (!ConsumeKeyword(ref afterIn, "IN")) {
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
        if (!ConsumeKeyword(ref afterList, "DO")) {
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

    private bool PushBatchFile(string dosPath, string[] arguments) {
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
}
