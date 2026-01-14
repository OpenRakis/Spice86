namespace Spice86.Core.Emulator.OperatingSystem.Shell;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Parses command lines for batch files and programs, handling arguments, parameters, and switches.
/// Based on DOSBox Staging's CommandLine class.
/// </summary>
public class CommandLine {
    private readonly List<string> _commands = new();
    private string _fileName = string.Empty;

    /// <summary>
    /// Initializes a new instance from a filename and command-line string.
    /// </summary>
    /// <param name="fileName">The name of the batch file or program (for %0).</param>
    /// <param name="commandLine">The command-line arguments.</param>
    public CommandLine(string fileName, string commandLine) {
        _fileName = fileName;
        ParseCommandLine(commandLine);
    }

    /// <summary>
    /// Gets the file name (for %0 expansion in batch files).
    /// </summary>
    public string GetFileName() => _fileName;

    /// <summary>
    /// Gets the number of arguments.
    /// </summary>
    public int GetCount() => _commands.Count;

    /// <summary>
    /// Finds a command argument by index (1-based, like DOS).
    /// </summary>
    /// <param name="which">The 1-based index of the argument.</param>
    /// <param name="value">The argument value if found.</param>
    /// <returns>True if the argument exists.</returns>
    public bool FindCommand(int which, out string value) {
        value = string.Empty;
        if (which < 1 || which > _commands.Count) {
            return false;
        }
        value = _commands[which - 1];
        return true;
    }

    /// <summary>
    /// Shifts the arguments left, moving argument 1 to be the new filename.
    /// </summary>
    /// <param name="amount">Number of positions to shift (default 1).</param>
    public void Shift(int amount = 1) {
        for (int i = 0; i < amount && _commands.Count > 0; i++) {
            _fileName = _commands[0];
            _commands.RemoveAt(0);
        }
    }

    /// <summary>
    /// Gets all remaining arguments as a single string.
    /// </summary>
    /// <param name="value">The concatenated arguments.</param>
    /// <returns>True if there are arguments.</returns>
    public bool GetStringRemain(out string value) {
        if (_commands.Count == 0) {
            value = string.Empty;
            return false;
        }
        value = string.Join(" ", _commands);
        return true;
    }

    /// <summary>
    /// Checks if a switch/parameter exists (case-insensitive).
    /// </summary>
    /// <param name="name">The switch name (e.g., "/C" or "-c").</param>
    /// <param name="remove">Whether to remove the switch if found.</param>
    /// <returns>True if the switch exists.</returns>
    public bool FindExist(string name, bool remove = false) {
        int index = _commands.FindIndex(cmd => 
            string.Equals(cmd, name, StringComparison.OrdinalIgnoreCase));
        
        if (index == -1) {
            return false;
        }

        if (remove) {
            _commands.RemoveAt(index);
        }
        return true;
    }

    /// <summary>
    /// Finds a string argument following a switch.
    /// </summary>
    /// <param name="name">The switch name.</param>
    /// <param name="value">The value following the switch.</param>
    /// <param name="remove">Whether to remove both switch and value.</param>
    /// <returns>True if found.</returns>
    public bool FindString(string name, out string value, bool remove = false) {
        value = string.Empty;
        int index = _commands.FindIndex(cmd => 
            string.Equals(cmd, name, StringComparison.OrdinalIgnoreCase));
        
        if (index == -1 || index + 1 >= _commands.Count) {
            return false;
        }

        value = _commands[index + 1];
        if (remove) {
            _commands.RemoveRange(index, 2);
        }
        return true;
    }

    /// <summary>
    /// Gets all arguments as a list.
    /// </summary>
    public List<string> GetArguments() => new List<string>(_commands);

    private void ParseCommandLine(string commandLine) {
        if (string.IsNullOrEmpty(commandLine)) {
            return;
        }

        bool inQuotes = false;
        string current = string.Empty;

        foreach (char c in commandLine) {
            if (c == '"') {
                inQuotes = !inQuotes;
            } else if (c == ' ' && !inQuotes) {
                if (!string.IsNullOrEmpty(current)) {
                    _commands.Add(current);
                    current = string.Empty;
                }
            } else {
                current += c;
            }
        }

        if (!string.IsNullOrEmpty(current)) {
            _commands.Add(current);
        }
    }
}
