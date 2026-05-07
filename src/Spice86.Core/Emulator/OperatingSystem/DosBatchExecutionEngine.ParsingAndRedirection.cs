namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using Serilog.Events;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal sealed partial class DosBatchExecutionEngine {
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

        SplitCompactCommand(ref token, ref remaining);

        return true;
    }

    private static void SplitCompactCommand(ref string token, ref string remaining) {
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

    private static bool ConsumeKeyword(ref string text, string keyword) {
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
            string? environmentValue = _host.GetEnvironmentVariable(variableName);
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

    private bool ApplyRedirection(CommandRedirection redirection) {
        if (!string.IsNullOrWhiteSpace(redirection.InputPath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("BATCH: Redirecting stdin from {Path}", redirection.InputPath);
            }
            if (!RedirectStandardInput(redirection.InputPath)) {
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
            if (!RedirectStandardOutput(redirection.OutputPath, redirection.AppendOutput, (ushort)DosStandardHandle.Stdout)) {
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
            if (!RedirectStandardOutput(redirection.ErrorPath, redirection.AppendError, (ushort)DosStandardHandle.Stderr)) {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("BATCH: Failed to redirect stderr to {Path}", redirection.ErrorPath);
                }
                RestoreStandardHandlesAfterLaunch();
                return false;
            }
        }

        return true;
    }

    private bool RedirectStandardInput(string dosPath) {
        DosFileOperationResult openResult = _dosFileManager.OpenFileOrDevice(dosPath, FileAccessMode.ReadOnly);
        if (openResult.IsError || openResult.Value == null) {
            return false;
        }

        return MoveHandleToStandard((ushort)openResult.Value.Value, (ushort)DosStandardHandle.Stdin);
    }

    private bool RedirectStandardOutput(string dosPath, bool append, ushort standardHandle) {
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

        return MoveHandleToStandard(openedHandle, standardHandle);
    }

    private bool MoveHandleToStandard(ushort sourceHandle, ushort standardHandle) {
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
                if (!ReadRedirection(commandLine, ref i, redirectionBuilder)) {
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

    private static bool ReadRedirection(string commandLine, ref int index, RedirectionBuilder redirectionBuilder) {
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
}
