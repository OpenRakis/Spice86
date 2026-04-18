namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using Serilog.Events;

using Spice86.Core.Emulator.OperatingSystem.Structures;

using System;
using System.IO;

internal sealed partial class DosBatchExecutionEngine {
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

    private bool DoesFileExist(string dosPath) {
        string normalizedPath = NormalizeDosPath(Unquote(dosPath));
        if (_zDriveFiles.ContainsKey(normalizedPath)) {
            return true;
        }

        string? hostPath = _dosFileManager.TryGetFullHostPathFromDos(normalizedPath);
        return !string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath);
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
