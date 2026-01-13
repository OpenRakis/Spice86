namespace Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;

using System;

/// <summary>
/// Represents a batch command to be executed.
/// </summary>
public readonly struct BatchCommand {
    /// <summary>
    /// The type of command.
    /// </summary>
    public BatchCommandType Type { get; }

    /// <summary>
    /// The primary value (program name, label, or message depending on type).
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Secondary value (arguments for programs).
    /// </summary>
    public string Arguments { get; }

    /// <summary>
    /// For IF commands, indicates whether the condition should be negated (IF NOT).
    /// </summary>
    public bool Negate { get; }

    private BatchCommand(BatchCommandType type, string value = "", string arguments = "", bool negate = false) {
        Type = type;
        Value = value;
        Arguments = arguments;
        Negate = negate;
    }

    /// <summary>
    /// Creates an empty (no-op) command.
    /// </summary>
    public static BatchCommand Empty() => new(BatchCommandType.Empty);

    /// <summary>
    /// Creates a command to print a message.
    /// </summary>
    public static BatchCommand PrintMessage(string message) =>
        new(BatchCommandType.PrintMessage, message);

    /// <summary>
    /// Creates a command to show the current echo state.
    /// </summary>
    public static BatchCommand ShowEchoState(bool isOn) =>
        new(BatchCommandType.ShowEchoState, isOn ? "ON" : "OFF");

    /// <summary>
    /// Creates a command to execute an external program.
    /// </summary>
    public static BatchCommand ExecuteProgram(string program, string arguments) =>
        new(BatchCommandType.ExecuteProgram, program, arguments);

    /// <summary>
    /// Creates a GOTO command.
    /// </summary>
    public static BatchCommand Goto(string label) =>
        new(BatchCommandType.Goto, label);

    /// <summary>
    /// Creates a CALL command to invoke another batch file.
    /// </summary>
    public static BatchCommand CallBatch(string batchFile, string arguments) =>
        new(BatchCommandType.CallBatch, batchFile, arguments);

    /// <summary>
    /// Creates a SET command to set an environment variable.
    /// </summary>
    public static BatchCommand SetVariable(string name, string value) =>
        new(BatchCommandType.SetVariable, name, value);

    /// <summary>
    /// Creates a command to show all environment variables.
    /// </summary>
    public static BatchCommand ShowVariables() =>
        new(BatchCommandType.ShowVariables);

    /// <summary>
    /// Creates a command to show a specific environment variable.
    /// </summary>
    public static BatchCommand ShowVariable(string name) =>
        new(BatchCommandType.ShowVariable, name);

    /// <summary>
    /// Creates an IF conditional command.
    /// </summary>
    /// <param name="condition">The condition type (EXIST, ERRORLEVEL, or string comparison).</param>
    /// <param name="arguments">The condition arguments and command to execute.</param>
    /// <param name="negate">True if the condition should be negated (IF NOT).</param>
    public static BatchCommand If(string condition, string arguments, bool negate = false) =>
        new(BatchCommandType.If, condition, arguments, negate);

    /// <summary>
    /// Creates a SHIFT command.
    /// </summary>
    public static BatchCommand Shift() =>
        new(BatchCommandType.Shift);

    /// <summary>
    /// Creates a PAUSE command.
    /// </summary>
    public static BatchCommand Pause() =>
        new(BatchCommandType.Pause);

    /// <summary>
    /// Creates an EXIT command.
    /// </summary>
    public static BatchCommand Exit() =>
        new(BatchCommandType.Exit);

    /// <summary>
    /// Creates a FOR loop command.
    /// </summary>
    /// <param name="variable">The variable name (e.g., "%C").</param>
    /// <param name="set">The set of values to iterate over.</param>
    /// <param name="commandTemplate">The command template with variable placeholder.</param>
    public static BatchCommand For(string variable, string[] set, string commandTemplate) {
        // Encode the set using unit separator character (0x1F) for safety
        // This avoids issues with semicolons which are valid delimiters in FOR sets
        string encodedSet = string.Join("\x1F", set);
        return new(BatchCommandType.For, variable, encodedSet + "\0" + commandTemplate);
    }

    /// <summary>
    /// Gets the FOR command's set items (only valid for For command type).
    /// </summary>
    public string[] GetForSet() {
        if (Type != BatchCommandType.For) {
            return Array.Empty<string>();
        }
        int nullIndex = Arguments.IndexOf('\0');
        if (nullIndex < 0) {
            return Array.Empty<string>();
        }
        string setString = Arguments[..nullIndex];
        return setString.Split('\x1F', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Gets the FOR command's command template (only valid for For command type).
    /// </summary>
    public string GetForCommand() {
        if (Type != BatchCommandType.For) {
            return string.Empty;
        }
        int nullIndex = Arguments.IndexOf('\0');
        if (nullIndex < 0 || nullIndex >= Arguments.Length - 1) {
            return string.Empty;
        }
        return Arguments[(nullIndex + 1)..];
    }
}
