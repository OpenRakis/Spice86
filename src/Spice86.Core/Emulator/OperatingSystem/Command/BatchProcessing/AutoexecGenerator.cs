namespace Spice86.Core.Emulator.OperatingSystem.Command.BatchProcessing;

using System.Collections.Generic;
using System.Text;

/// <summary>
/// Generates AUTOEXEC.BAT content for bootstrapping DOS programs.
/// </summary>
/// <remarks>
/// <para>
/// Based on DOSBox staging's autoexec.cpp implementation.
/// This class generates the content for a virtual AUTOEXEC.BAT file that:
/// - Sets up environment variables
/// - Mounts drives
/// - Starts the user's program
/// </para>
/// <para>
/// The generated AUTOEXEC.BAT provides a clean DOS environment without
/// relying on host file system paths.
/// </para>
/// </remarks>
public sealed class AutoexecGenerator {
    private readonly List<string> _initialCommands = new();
    private readonly List<string> _environmentVariables = new();
    private readonly List<string> _finalCommands = new();
    private bool _echoOff = true;

    /// <summary>
    /// Gets or sets whether ECHO is turned off at the start.
    /// </summary>
    public bool EchoOff {
        get => _echoOff;
        set => _echoOff = value;
    }

    /// <summary>
    /// Adds an initial command (executed before the main program).
    /// </summary>
    /// <param name="command">The command to add.</param>
    public void AddInitialCommand(string command) {
        _initialCommands.Add(command);
    }

    /// <summary>
    /// Sets an environment variable.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <param name="value">Variable value.</param>
    public void SetEnvironmentVariable(string name, string value) {
        _environmentVariables.Add($"@SET {name}={value}");
    }

    /// <summary>
    /// Sets the PATH environment variable.
    /// </summary>
    /// <param name="path">The path value.</param>
    public void SetPath(string path) {
        SetEnvironmentVariable("PATH", path);
    }

    /// <summary>
    /// Adds a command to execute the main program.
    /// </summary>
    /// <param name="programPath">The DOS path to the program.</param>
    /// <param name="arguments">Optional arguments.</param>
    public void AddProgramExecution(string programPath, string arguments = "") {
        string command = programPath;
        if (!string.IsNullOrEmpty(arguments)) {
            command += " " + arguments;
        }
        _finalCommands.Add(command);
    }

    /// <summary>
    /// Adds a CALL command for a batch file.
    /// </summary>
    /// <param name="batchPath">The DOS path to the batch file.</param>
    /// <param name="arguments">Optional arguments.</param>
    public void AddBatchCall(string batchPath, string arguments = "") {
        string command = "CALL " + batchPath;
        if (!string.IsNullOrEmpty(arguments)) {
            command += " " + arguments;
        }
        _finalCommands.Add(command);
    }

    /// <summary>
    /// Adds an exit command to exit COMMAND.COM after execution.
    /// </summary>
    public void AddExitCommand() {
        _finalCommands.Add("@EXIT");
    }

    /// <summary>
    /// Generates the AUTOEXEC.BAT content as a string array.
    /// </summary>
    /// <returns>An array of lines for the AUTOEXEC.BAT file.</returns>
    public string[] Generate() {
        List<string> lines = new();

        // Add ECHO OFF if enabled
        if (_echoOff) {
            lines.Add("@ECHO OFF");
        }

        // Add environment variables
        foreach (string envVar in _environmentVariables) {
            lines.Add(envVar);
        }

        // Add initial commands
        foreach (string cmd in _initialCommands) {
            lines.Add(cmd);
        }

        // Add final commands (program execution)
        foreach (string cmd in _finalCommands) {
            lines.Add(cmd);
        }

        return lines.ToArray();
    }

    /// <summary>
    /// Generates the AUTOEXEC.BAT content as a single string with DOS line endings.
    /// </summary>
    /// <returns>The complete AUTOEXEC.BAT content.</returns>
    public string GenerateAsString() {
        string[] lines = Generate();
        StringBuilder sb = new();
        foreach (string line in lines) {
            sb.Append(line);
            sb.Append("\r\n"); // DOS line endings
        }
        return sb.ToString();
    }

    /// <summary>
    /// Creates an AutoexecGenerator for a simple program execution.
    /// </summary>
    /// <param name="programPath">The DOS path to the program.</param>
    /// <param name="arguments">Optional arguments.</param>
    /// <param name="exitAfter">Whether to exit COMMAND.COM after execution.</param>
    /// <returns>A configured AutoexecGenerator instance.</returns>
    public static AutoexecGenerator ForProgram(string programPath, string arguments = "", bool exitAfter = true) {
        AutoexecGenerator generator = new();
        generator.AddProgramExecution(programPath, arguments);
        if (exitAfter) {
            generator.AddExitCommand();
        }
        return generator;
    }

    /// <summary>
    /// Creates an AutoexecGenerator for a batch file execution.
    /// </summary>
    /// <param name="batchPath">The DOS path to the batch file.</param>
    /// <param name="arguments">Optional arguments.</param>
    /// <param name="exitAfter">Whether to exit COMMAND.COM after execution.</param>
    /// <returns>A configured AutoexecGenerator instance.</returns>
    public static AutoexecGenerator ForBatch(string batchPath, string arguments = "", bool exitAfter = true) {
        AutoexecGenerator generator = new();
        generator.AddBatchCall(batchPath, arguments);
        if (exitAfter) {
            generator.AddExitCommand();
        }
        return generator;
    }
}
