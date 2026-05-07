namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using System;
using System.Collections.Generic;
using System.IO;

internal sealed class BatchFileContext {
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

    internal string FilePath => _filePath;

    internal bool EchoEnabled { get; set; } = true;

    internal string[] TemporaryFilesToCleanup => _temporaryFilesToCleanup;

    internal string[] GetAllLines() => _lines;

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

    internal bool GoToLabel(string label) {
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

    internal string? GetContainingDirectory() {
        if (string.IsNullOrWhiteSpace(_filePath) || _filePath.StartsWith('<')) {
            return null;
        }

        // Use DOS-aware parsing: search for the last backslash (or forward slash normalised to backslash)
        // instead of Path.GetDirectoryName, which does not treat '\' as a separator on Linux.
        string normalizedPath = _filePath.Replace('/', '\\');
        int lastSep = normalizedPath.LastIndexOf('\\');
        if (lastSep < 0) {
            return null;
        }

        // For drive-root paths such as "C:\FILE.BAT" lastSep == 2; keep the trailing backslash.
        if (lastSep == 2 && normalizedPath.Length > 1 && normalizedPath[1] == ':') {
            return normalizedPath[..(lastSep + 1)];
        }

        return normalizedPath[..lastSep];
    }
}