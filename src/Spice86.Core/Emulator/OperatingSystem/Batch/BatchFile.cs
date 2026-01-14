namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using Spice86.Core.Emulator.OperatingSystem.Structures;
using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Represents a batch file command processor similar to DOSBox's implementation.
/// Handles batch file execution including parameter expansion, GOTO, SHIFT, and ECHO.
/// </summary>
public class BatchFile : IDisposable {
    private readonly ILineReader _reader;
    private readonly EnvironmentVariables _environment;
    private readonly string _fileName;
    private readonly string[] _arguments;
    private int _argumentShiftOffset;
    private bool _echo;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchFile"/> class.
    /// </summary>
    /// <param name="reader">The line reader for reading batch file content.</param>
    /// <param name="environment">The environment variables for %VAR% expansion.</param>
    /// <param name="fileName">The name of the batch file (for %0 expansion).</param>
    /// <param name="commandLine">The command line arguments passed to the batch file.</param>
    /// <param name="echoOn">Initial ECHO state.</param>
    public BatchFile(ILineReader reader, EnvironmentVariables environment, string fileName, string commandLine, bool echoOn) {
        _reader = reader;
        _environment = environment;
        _fileName = fileName;
        _arguments = ParseCommandLine(commandLine);
        _argumentShiftOffset = 0;
        _echo = echoOn;
        _disposed = false;
    }

    /// <summary>
    /// Gets a value indicating whether ECHO is enabled.
    /// </summary>
    public bool Echo => _echo;

    /// <summary>
    /// Gets the batch file name.
    /// </summary>
    public string FileName => _fileName;

    /// <summary>
    /// Sets the ECHO state.
    /// </summary>
    /// <param name="echoOn">True to enable ECHO, false to disable.</param>
    public void SetEcho(bool echoOn) {
        _echo = echoOn;
    }

    /// <summary>
    /// Shifts the command-line arguments by one position (removes %1, %2 becomes %1, etc.).
    /// </summary>
    public void Shift() {
        if (_argumentShiftOffset < _arguments.Length) {
            _argumentShiftOffset++;
        }
    }

    /// <summary>
    /// Reads the next executable line from the batch file.
    /// Skips comments, labels, and empty lines.
    /// </summary>
    /// <param name="lineOut">The output line with variables expanded.</param>
    /// <returns>True if a line was read, false if end of file.</returns>
    public bool ReadLine(out string lineOut) {
        lineOut = string.Empty;

        string? line = GetLine();
        while (line != null && (line.Length == 0 || IsCommentOrLabel(line))) {
            line = GetLine();
        }

        if (line == null) {
            return false;
        }

        lineOut = ExpandBatchLine(line);
        return true;
    }

    /// <summary>
    /// Jumps to a label in the batch file.
    /// </summary>
    /// <param name="label">The label name to jump to (without the colon).</param>
    /// <returns>True if the label was found, false otherwise.</returns>
    public bool Goto(string label) {
        _reader.Reset();

        string? line = GetLine();
        while (line != null) {
            if (IsLabel(line, label)) {
                return true;
            }
            line = GetLine();
        }

        return false;
    }

    private string? GetLine() {
        string? line = _reader.ReadLine();
        if (line == null) {
            return null;
        }

        line = RemoveInvalidCharacters(line);
        return line.Trim();
    }

    private static string RemoveInvalidCharacters(string line) {
        StringBuilder result = new StringBuilder(line.Length);

        foreach (char c in line) {
            byte data = (byte)c;

            const byte Esc = 27;
            const byte UnitSeparator = 31;

            if (data <= UnitSeparator && data != '\t' && data != '\b' && 
                data != Esc && data != '\n' && data != '\r') {
                continue;
            }

            result.Append(c);
        }

        return result.ToString();
    }

    private static bool IsCommentOrLabel(string line) {
        int colonIndex = line.IndexOf(':');
        if (colonIndex == -1) {
            return false;
        }

        int firstNonWhitespace = 0;
        while (firstNonWhitespace < line.Length && 
               (line[firstNonWhitespace] == ' ' || 
                line[firstNonWhitespace] == '\t' || 
                line[firstNonWhitespace] == '=')) {
            firstNonWhitespace++;
        }

        return firstNonWhitespace == colonIndex;
    }

    private static bool IsLabel(string line, string targetLabel) {
        int labelStart = -1;
        int colonCount = 0;

        for (int i = 0; i < line.Length; i++) {
            if (line[i] == ':') {
                colonCount++;
                if (colonCount == 1 && labelStart == -1) {
                    labelStart = i + 1;
                }
            } else if (line[i] != ' ' && line[i] != '\t' && line[i] != '=') {
                if (labelStart == -1) {
                    return false;
                }
                break;
            }
        }

        if (colonCount != 1 || labelStart == -1) {
            return false;
        }

        string labelText = line.Substring(labelStart);
        int labelEnd = labelText.IndexOfAny(new[] { '\t', '\r', '\n', ' ' });
        if (labelEnd != -1) {
            labelText = labelText.Substring(0, labelEnd);
        }

        return string.Equals(labelText, targetLabel, StringComparison.OrdinalIgnoreCase);
    }

    private string ExpandBatchLine(string line) {
        StringBuilder expanded = new StringBuilder(line.Length * 2);

        int percentIndex = line.IndexOf('%');
        while (percentIndex != -1) {
            expanded.Append(line.Substring(0, percentIndex));
            line = line.Substring(percentIndex + 1);

            if (line.Length == 0) {
                break;
            }

            if (line[0] == '%') {
                expanded.Append('%');
                line = line.Substring(1);
            } else if (line[0] == '0') {
                expanded.Append(_fileName);
                line = line.Substring(1);
            } else if (line[0] >= '1' && line[0] <= '9') {
                int argIndex = (line[0] - '0') + _argumentShiftOffset;
                if (argIndex < _arguments.Length) {
                    expanded.Append(_arguments[argIndex]);
                }
                line = line.Substring(1);
            } else {
                int closingPercent = line.IndexOf('%');
                if (closingPercent == -1) {
                    break;
                }

                string varName = line.Substring(0, closingPercent);
                if (_environment.TryGetValue(varName, out string? value)) {
                    expanded.Append(value);
                }
                line = line.Substring(closingPercent + 1);
            }

            percentIndex = line.IndexOf('%');
        }

        expanded.Append(line);
        return expanded.ToString();
    }

    private static string[] ParseCommandLine(string commandLine) {
        List<string> args = new List<string>();
        StringBuilder currentArg = new StringBuilder();
        bool inQuotes = false;

        foreach (char c in commandLine) {
            if (c == '"') {
                inQuotes = !inQuotes;
            } else if (c == ' ' && !inQuotes) {
                if (currentArg.Length > 0) {
                    args.Add(currentArg.ToString());
                    currentArg.Clear();
                }
            } else {
                currentArg.Append(c);
            }
        }

        if (currentArg.Length > 0) {
            args.Add(currentArg.ToString());
        }

        return args.ToArray();
    }

    /// <summary>
    /// Disposes of resources used by the batch file, including the line reader and its underlying stream.
    /// </summary>
    public void Dispose() {
        if (!_disposed) {
            (_reader as IDisposable)?.Dispose();
            _disposed = true;
        }
    }
}
