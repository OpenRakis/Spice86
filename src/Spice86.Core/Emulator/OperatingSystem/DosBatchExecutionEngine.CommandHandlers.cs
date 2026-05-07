namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.Devices.CdRom.Image;
using Spice86.Core.Emulator.InterruptHandlers.Mscdex;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.FileSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

internal sealed partial class DosBatchExecutionEngine {
    internal bool HandleSet(string arguments) {
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

    internal bool HandleEcho(string arguments) {
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

    internal bool HandlePath(string arguments) {
        string trimmed = arguments.TrimStart();
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: PATH {Args}", trimmed);
        }
        if (trimmed.Length == 0) {
            string? pathValue = _host.GetEnvironmentVariable("PATH");
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

    internal void HandleCls() {
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
            string? hostPath = _dosFileManager.GetFullHostPathFromDos(temporaryDosFiles[i]);
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

    internal bool HandleType(string arguments) {
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

    internal bool HandleChdir(string arguments) {
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

        // The path resolver natively honors the drive's current directory and
        // applies '.' / '..' segments, so a relative path can be passed through.
        DosFileOperationResult setResult = _dosFileManager.SetCurrentDir(trimmed);
        if (setResult.IsError) {
            WriteToStandardOutput("Invalid directory\r\n");
        }

        return false;
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

    internal bool HandleMkdir(string arguments) {
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

    internal bool HandleRmdir(string arguments) {
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

    internal bool HandleDel(string arguments) {
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

    internal bool HandleRen(string arguments) {
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

    internal bool HandleDir(string arguments) {
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

    internal bool HandleCopy(string arguments) {
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
            return HandleCopyConcat(source, destination);
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

    private bool HandleCopyConcat(string sourcesWithPlus, string destination) {
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

    internal bool HandleMove(string arguments) {
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

    internal bool HandleDate(string arguments) {
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

    internal bool HandleTime(string arguments) {
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

    internal bool HandleVer() {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("BATCH: VER");
        }
        WriteToStandardOutput("Spice86 DOS version 5.00\r\n");
        return false;
    }

    internal bool HandleVol(string arguments) {
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

    /// <summary>
    /// Handles the MOUNT command which mounts a host folder as a DOS drive.
    /// Syntax: MOUNT &lt;driveLetter&gt; &lt;hostPath&gt; [-t cdrom|floppy|hdd]
    /// The host path may be an absolute host path, a relative path (resolved against the
    /// process working directory), or a path starting with a mounted DOS drive letter
    /// (e.g. <c>C:\games</c> where C: is already mounted — resolved via the drive table).
    /// Paths that contain spaces must be surrounded by double-quotes.
    /// </summary>
    internal bool HandleMount(string arguments) {
        string trimmed = arguments.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            WriteToStandardOutput("Usage: MOUNT <drive> <path> [-t cdrom|floppy|hdd]\r\n");
            return false;
        }

        string[] parts = BatchArgumentParser.SplitWithQuotes(trimmed);
        if (parts.Length < 2) {
            WriteToStandardOutput("MOUNT: missing path argument\r\n");
            return false;
        }

        string driveSpec = parts[0];
        if (driveSpec.Length < 1 || !char.IsLetter(driveSpec[0])) {
            WriteToStandardOutput($"MOUNT: invalid drive letter '{driveSpec}'\r\n");
            return false;
        }

        char driveLetter = char.ToUpperInvariant(driveSpec[0]);
        string hostPath;
        try {
            hostPath = HostPathResolver.Resolve(parts[1], _driveManager);
        } catch (ArgumentException ex) {
            WriteToStandardOutput($"MOUNT: invalid path '{parts[1]}': {ex.Message}\r\n");
            return false;
        } catch (NotSupportedException ex) {
            WriteToStandardOutput($"MOUNT: invalid path format '{parts[1]}': {ex.Message}\r\n");
            return false;
        } catch (PathTooLongException ex) {
            WriteToStandardOutput($"MOUNT: path too long '{parts[1]}': {ex.Message}\r\n");
            return false;
        }

        // Parse optional -t type flag
        string driveType = "hdd";
        for (int i = 2; i < parts.Length - 1; i++) {
            if (parts[i].Equals("-t", StringComparison.OrdinalIgnoreCase)) {
                driveType = parts[i + 1].ToLowerInvariant();
                break;
            }
        }

        if (!Directory.Exists(hostPath)) {
            WriteToStandardOutput($"MOUNT: path not found: {hostPath}\r\n");
            return false;
        }

        if (driveType == "floppy") {
            _driveManager.MountFloppyFolder(driveLetter, hostPath);
        } else if (driveType == "cdrom") {
            if (!MountCdRomFolder(driveLetter, hostPath)) {
                return false;
            }
        } else {
            _driveManager.MountFolderDrive(driveLetter, hostPath);
        }

        WriteToStandardOutput($"Drive {driveLetter}: mounted as {hostPath}\r\n");
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: MOUNT {Drive}: = {Path} (type={Type})", driveLetter, hostPath, driveType);
        }
        return false;
    }

    /// <summary>
    /// Handles the IMGMOUNT command which mounts one or more disk image files as a DOS drive.
    /// Syntax: IMGMOUNT &lt;driveLetter&gt; &lt;image1&gt; [&lt;image2&gt; ...] [-t floppy|iso|cue]
    /// Image paths may be absolute host paths, relative paths (resolved against the process
    /// working directory), or paths starting with a mounted DOS drive letter
    /// (e.g. <c>C:\games\disc.iso</c> resolved via the drive table).
    /// Paths that contain spaces must be surrounded by double-quotes.
    /// Multiple images enable Ctrl-F4 disc switching.
    /// </summary>
    internal bool HandleImgMount(string arguments) {
        string trimmed = arguments.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            WriteToStandardOutput("Usage: IMGMOUNT <drive> <image> [-t floppy|iso|cue]\r\n");
            return false;
        }

        string[] parts = BatchArgumentParser.SplitWithQuotes(trimmed);
        if (parts.Length < 2) {
            WriteToStandardOutput("IMGMOUNT: missing image path\r\n");
            return false;
        }

        string driveSpec = parts[0];
        if (driveSpec.Length < 1 || !char.IsLetter(driveSpec[0])) {
            WriteToStandardOutput($"IMGMOUNT: invalid drive letter '{driveSpec}'\r\n");
            return false;
        }

        char driveLetter = char.ToUpperInvariant(driveSpec[0]);

        // Collect image paths: all tokens between the drive letter and an optional -t flag
        string imageType = string.Empty;
        List<string> imagePaths = new();
        for (int i = 1; i < parts.Length; i++) {
            if (parts[i].Equals("-t", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 < parts.Length) {
                    imageType = parts[i + 1].ToLowerInvariant();
                }
                break;
            }
            string resolved;
            try {
                resolved = HostPathResolver.Resolve(parts[i], _driveManager);
            } catch (ArgumentException ex) {
                WriteToStandardOutput($"IMGMOUNT: invalid path '{parts[i]}': {ex.Message}\r\n");
                return false;
            } catch (NotSupportedException ex) {
                WriteToStandardOutput($"IMGMOUNT: invalid path format '{parts[i]}': {ex.Message}\r\n");
                return false;
            } catch (PathTooLongException ex) {
                WriteToStandardOutput($"IMGMOUNT: path too long '{parts[i]}': {ex.Message}\r\n");
                return false;
            }
            imagePaths.Add(resolved);
        }

        if (imagePaths.Count == 0) {
            WriteToStandardOutput("IMGMOUNT: missing image path\r\n");
            return false;
        }

        // Auto-detect type from the first image if not specified
        if (string.IsNullOrEmpty(imageType)) {
            string ext = Path.GetExtension(imagePaths[0]).ToLowerInvariant();
            if (ext == ".img" || ext == ".ima" || ext == ".vfd") {
                imageType = "floppy";
            } else if (ext == ".iso") {
                imageType = "iso";
            } else if (ext == ".cue") {
                imageType = "cue";
            } else {
                WriteToStandardOutput($"IMGMOUNT: cannot detect image type for '{imagePaths[0]}'. Use -t floppy|iso|cue.\r\n");
                return false;
            }
        }

        // Validate that all image files exist
        foreach (string path in imagePaths) {
            if (!File.Exists(path)) {
                WriteToStandardOutput($"IMGMOUNT: image file not found: {path}\r\n");
                return false;
            }
        }

        if (imageType == "floppy") {
            MountFloppyImages(driveLetter, imagePaths);
        } else if (imageType == "iso" || imageType == "cue") {
            MountCdRomImages(driveLetter, imagePaths, imageType);
        } else {
            WriteToStandardOutput($"IMGMOUNT: unsupported image type '{imageType}'\r\n");
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: IMGMOUNT {Drive}: = [{Paths}] (type={Type})",
                driveLetter, string.Join(", ", imagePaths), imageType);
        }
        return false;
    }

    private void MountFloppyImages(char driveLetter, IReadOnlyList<string> imagePaths) {
        byte[] firstImageData = File.ReadAllBytes(imagePaths[0]);
        _driveManager.MountFloppyImage(driveLetter, firstImageData, imagePaths[0]);
        for (int i = 1; i < imagePaths.Count; i++) {
            byte[] extraData = File.ReadAllBytes(imagePaths[i]);
            _driveManager.AddFloppyImage(driveLetter, extraData, imagePaths[i]);
        }
        string paths = string.Join(", ", imagePaths);
        WriteToStandardOutput($"Drive {driveLetter}: mounted {imagePaths.Count} floppy image(s): {paths}\r\n");
    }

    private void MountCdRomImages(char driveLetter, IReadOnlyList<string> imagePaths, string imageType) {
        try {
            List<ICdRomImage> images = new();
            foreach (string path in imagePaths) {
                ICdRomImage image;
                if (imageType == "cue") {
                    image = new CueBinImage(path);
                } else {
                    image = new IsoImage(path);
                }
                images.Add(image);
            }
            CdRomDrive drive = new CdRomDrive(images);
            CdAudioPlayer audioPlayer = new CdAudioPlayer(_channelCreator);
            audioPlayer.SetDrive(drive);
            drive.SetAudioPlayer(audioPlayer);
            byte driveIndex = DosDriveManager.DriveLetters.TryGetValue(driveLetter, out byte idx) ? idx : (byte)3;
            MscdexDriveEntry entry = new MscdexDriveEntry(driveLetter, driveIndex, drive);
            _mscdex.AddDrive(entry);
            _driveManager.RegisterCdRomDriveLetter(driveLetter, string.Empty);
            string paths = string.Join(", ", imagePaths);
            WriteToStandardOutput($"Drive {driveLetter}: mounted {imagePaths.Count} CD-ROM image(s): {paths}\r\n");
        } catch (IOException ex) {
            WriteToStandardOutput($"IMGMOUNT: failed to open image: {ex.Message}\r\n");
        }
    }

    private bool MountCdRomFolder(char driveLetter, string hostPath) {
        string volumeLabel = Path.GetFileName(hostPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(volumeLabel)) {
            volumeLabel = driveLetter.ToString();
        }
        try {
            VirtualIsoImage image = new VirtualIsoImage(hostPath, volumeLabel);
            CdRomDrive drive = new CdRomDrive(image);
            CdAudioPlayer audioPlayer = new CdAudioPlayer(_channelCreator);
            audioPlayer.SetDrive(drive);
            drive.SetAudioPlayer(audioPlayer);
            byte driveIndex = DosDriveManager.DriveLetters.TryGetValue(driveLetter, out byte idx) ? idx : (byte)3;
            MscdexDriveEntry entry = new MscdexDriveEntry(driveLetter, driveIndex, drive);
            _mscdex.AddDrive(entry);
            _driveManager.RegisterCdRomDriveLetter(driveLetter, hostPath);
            return true;
        } catch (IOException ex) {
            WriteToStandardOutput($"MOUNT: failed to create CD-ROM drive from folder: {ex.Message}\r\n");
            return false;
        }
    }

    /// <summary>
    /// Handles the <c>BOOT</c> internal command, matching DOSBox Staging's
    /// <c>BOOT [image] [-l A|B]</c> for booting from a floppy image. The first
    /// 512 bytes of the floppy image are loaded at physical 0x7C00 and the CPU
    /// is set to <c>CS:IP = 0000:7C00</c> with <c>DL</c> = drive number (0 or 1).
    /// Booting from hard disk drive images is intentionally unsupported.
    /// </summary>
    /// <param name="arguments">Argument tail after the BOOT token.</param>
    /// <param name="launchRequest">Receives a <see cref="BootFloppyLaunchRequest"/> on success or <see cref="ContinueBatchExecutionLaunchRequest.Instance"/> on failure.</param>
    /// <returns><c>true</c> when boot setup is requested (caller must yield to host); <c>false</c> on parse/validation failure.</returns>
    internal bool TryHandleBoot(string arguments, out LaunchRequest launchRequest) {
        launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
        string trimmed = arguments.Trim();
        string[] parts = BatchArgumentParser.SplitWithQuotes(trimmed);

        char driveLetter = 'A';
        bool driveExplicit = false;
        List<string> imagePaths = new();
        for (int i = 0; i < parts.Length; i++) {
            if (parts[i].Equals("-l", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= parts.Length) {
                    WriteToStandardOutput("BOOT: missing drive letter after -l\r\n");
                    return false;
                }
                string driveSpec = parts[i + 1];
                if (driveSpec.Length < 1 || !char.IsLetter(driveSpec[0])) {
                    WriteToStandardOutput($"BOOT: invalid drive letter '{driveSpec}'\r\n");
                    return false;
                }
                driveLetter = char.ToUpperInvariant(driveSpec[0]);
                driveExplicit = true;
                i++;
                continue;
            }
            string resolved;
            try {
                resolved = HostPathResolver.Resolve(parts[i], _driveManager);
            } catch (ArgumentException ex) {
                WriteToStandardOutput($"BOOT: invalid path '{parts[i]}': {ex.Message}\r\n");
                return false;
            } catch (NotSupportedException ex) {
                WriteToStandardOutput($"BOOT: invalid path format '{parts[i]}': {ex.Message}\r\n");
                return false;
            } catch (PathTooLongException ex) {
                WriteToStandardOutput($"BOOT: path too long '{parts[i]}': {ex.Message}\r\n");
                return false;
            }
            imagePaths.Add(resolved);
        }

        if (driveLetter != 'A' && driveLetter != 'B') {
            WriteToStandardOutput($"BOOT: only floppy drives A: and B: are supported (got '{driveLetter}')\r\n");
            return false;
        }

        if (imagePaths.Count > 0) {
            foreach (string path in imagePaths) {
                if (!File.Exists(path)) {
                    WriteToStandardOutput($"BOOT: image file not found: {path}\r\n");
                    return false;
                }
            }
            byte[] firstImageData = File.ReadAllBytes(imagePaths[0]);
            _driveManager.MountFloppyImage(driveLetter, firstImageData, imagePaths[0]);
            for (int i = 1; i < imagePaths.Count; i++) {
                byte[] extra = File.ReadAllBytes(imagePaths[i]);
                _driveManager.AddFloppyImage(driveLetter, extra, imagePaths[i]);
            }
        }

        if (!_driveManager.TryGetFloppyDrive(driveLetter, out FloppyDiskDrive? floppy) ||
            floppy.GetCurrentImageData() is not byte[] imageData) {
            WriteToStandardOutput($"BOOT: no floppy image mounted on {driveLetter}:\r\n");
            return false;
        }

        if (imageData.Length < 512) {
            WriteToStandardOutput($"BOOT: image on {driveLetter}: is smaller than one sector\r\n");
            return false;
        }

        if (imageData[510] != 0x55 || imageData[511] != 0xAA) {
            WriteToStandardOutput($"BOOT: image on {driveLetter}: has no valid boot signature (0xAA55)\r\n");
            return false;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("BATCH: BOOT from {Drive}: image='{Path}' (explicit={Explicit})",
                driveLetter, floppy.ImagePath, driveExplicit);
        }
        launchRequest = new BootFloppyLaunchRequest(driveLetter, default);
        return true;
    }

    /// <summary>
    /// Handles the <c>SUBST</c> internal command, matching DOSBox Staging's
    /// <c>SUBST [drive: path]</c> / <c>SUBST drive: /D</c>:
    /// <list type="bullet">
    ///   <item><c>SUBST</c> with no arguments lists active SUBST drives.</item>
    ///   <item><c>SUBST X: \DOS\PATH</c> creates a virtual drive X: that maps
    ///     to the specified path. The path is resolved through <see cref="HostPathResolver"/>,
    ///     so DOS-style paths like <c>C:\GAMES</c> as well as host paths work.</item>
    ///   <item><c>SUBST X: /D</c> removes a previously-created SUBST drive.</item>
    /// </list>
    /// </summary>
    /// <param name="arguments">Argument tail after the SUBST token.</param>
    /// <returns><c>false</c> — SUBST never yields a launch request.</returns>
    internal bool HandleSubst(string arguments) {
        string trimmed = arguments.Trim();
        if (string.IsNullOrEmpty(trimmed)) {
            IReadOnlyDictionary<char, string> subst = _driveManager.SubstDrives;
            if (subst.Count == 0) {
                WriteToStandardOutput("No SUBSTitutions in effect\r\n");
                return false;
            }
            foreach (KeyValuePair<char, string> entry in subst) {
                if (_driveManager.TryGetValue(entry.Key, out VirtualDrive? drive)) {
                    WriteToStandardOutput($"{entry.Key}:\\: => {drive.MountedHostDirectory}\r\n");
                }
            }
            return false;
        }

        string[] parts = BatchArgumentParser.SplitWithQuotes(trimmed);
        if (parts.Length < 1) {
            WriteToStandardOutput("Usage: SUBST drive: path | SUBST drive: /D\r\n");
            return false;
        }

        string driveSpec = parts[0];
        if (driveSpec.Length < 2 || !char.IsLetter(driveSpec[0]) || driveSpec[1] != ':') {
            WriteToStandardOutput($"SUBST: invalid drive specification '{driveSpec}'\r\n");
            return false;
        }
        char driveLetter = char.ToUpperInvariant(driveSpec[0]);

        if (parts.Length >= 2 && parts[1].Equals("/D", StringComparison.OrdinalIgnoreCase)) {
            if (!_driveManager.UnmountSubstDrive(driveLetter)) {
                WriteToStandardOutput($"SUBST: drive {driveLetter}: is not SUBSTed\r\n");
                return false;
            }
            WriteToStandardOutput($"Drive {driveLetter}: un-SUBSTed\r\n");
            return false;
        }

        if (parts.Length < 2) {
            WriteToStandardOutput("Usage: SUBST drive: path | SUBST drive: /D\r\n");
            return false;
        }

        if (driveLetter == 'A' || driveLetter == 'B') {
            WriteToStandardOutput($"SUBST: cannot SUBST a floppy drive ({driveLetter}:)\r\n");
            return false;
        }
        if (_driveManager.TryGetValue(driveLetter, out VirtualDrive? existing)
            && !_driveManager.IsSubstDrive(driveLetter)
            && !string.IsNullOrEmpty(existing.MountedHostDirectory)) {
            WriteToStandardOutput($"SUBST: drive {driveLetter}: is already in use\r\n");
            return false;
        }

        string hostPath;
        try {
            hostPath = HostPathResolver.Resolve(parts[1], _driveManager);
        } catch (ArgumentException ex) {
            WriteToStandardOutput($"SUBST: invalid path '{parts[1]}': {ex.Message}\r\n");
            return false;
        } catch (NotSupportedException ex) {
            WriteToStandardOutput($"SUBST: invalid path format '{parts[1]}': {ex.Message}\r\n");
            return false;
        } catch (PathTooLongException ex) {
            WriteToStandardOutput($"SUBST: path too long '{parts[1]}': {ex.Message}\r\n");
            return false;
        }

        if (!Directory.Exists(hostPath)) {
            WriteToStandardOutput($"SUBST: path not found: {hostPath}\r\n");
            return false;
        }

        _driveManager.MountSubstDrive(driveLetter, hostPath, parts[1]);
        WriteToStandardOutput($"Drive {driveLetter}: SUBSTed to {hostPath}\r\n");
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("BATCH: SUBST {Drive}: = {Path}", driveLetter, hostPath);
        }
        return false;
    }

    /// <summary>
    /// Changes the current DOS drive to the specified letter.
    /// Mirrors COMMAND.COM's internal drive-change handling in DOSBox Staging:
    /// typing <c>X:</c> at a prompt (or in a batch file) switches the default drive.
    /// </summary>
    /// <param name="driveLetter">The upper-case drive letter to select.</param>
    internal void ChangeDrive(char driveLetter) {
        if (_driveManager.TryGetValue(driveLetter, out VirtualDrive? drive)) {
            _driveManager.CurrentDrive = drive;
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("BATCH: Changed drive to {Drive}:", driveLetter);
            }
        } else {
            WriteToStandardOutput($"Invalid drive specification\r\n");
        }
    }
}
