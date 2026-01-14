namespace Spice86.Core.Emulator.OperatingSystem.Shell;

using Serilog.Events;
using Spice86.Core.CLI;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Batch;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// DOS command shell (COMMAND.COM) implementation.
/// Handles batch file execution, internal commands, and command-line processing.
/// Based on DOSBox Staging's DOS_Shell implementation.
/// </summary>
public class DosShell {
    private readonly BatchFileManager _batchManager;
    private readonly DosFileManager _fileManager;
    private readonly DosDriveManager _driveManager;
    private readonly EnvironmentVariables _environment;
    private readonly Configuration _configuration;
    private readonly DosInt21Handler _int21;
    private readonly ILoggerService _logger;
    private bool _echo = true;
    private int _errorLevel = 0;
    private bool _shouldExitLoop = false;

    /// <summary>
    /// Gets or sets the current ERRORLEVEL value (set by programs on exit).
    /// </summary>
    public int ErrorLevel {
        get => _errorLevel;
        set => _errorLevel = value;
    }

    /// <summary>
    /// Initializes a new instance of the DOS shell.
    /// </summary>
    public DosShell(BatchFileManager batchManager, DosFileManager fileManager,
                    DosDriveManager driveManager, EnvironmentVariables environment,
                    Configuration configuration, ILoggerService logger, DosInt21Handler int21Handler) {
        _batchManager = batchManager;
        _fileManager = fileManager;
        _driveManager = driveManager;
        _environment = environment;
        _configuration = configuration;
        _int21 = int21Handler;
        _logger = logger;
    }

    /// <summary>
    /// Main shell execution loop. Reads and executes commands from batch files or user input.
    /// </summary>
    public void Run() {
        // Execute AUTOEXEC.BAT if it exists
        ExecuteAutoexec();

        // Main command loop
        while (!_shouldExitLoop) {
            string? line = ReadNextLine();
            if (line == null) {
                break; // No more input
            }

            if (!string.IsNullOrWhiteSpace(line)) {
                // If ParseAndExecute loaded an external program, exit loop to let CPU execute it
                // When program terminates, INT 22h vector will invoke shell callback to resume batch
                if (ParseAndExecute(line)) {
                    break;
                }
            }
        }

        // Batch file ended naturally (no more lines and no programs loading)
        // Clear all batch files only when truly exiting
        _batchManager.ClearAllBatchFiles();
    }

    /// <summary>
    /// Reads the next command line, either from a batch file or from user input.
    /// </summary>
    private string? ReadNextLine() {
        // If we're in a batch file, read from it
        if (_batchManager.IsExecutingBatch) {
            if (_batchManager.ReadBatchLine(out string line)) {
                // Echo the line if ECHO is ON
                if (_batchManager.Echo && !line.TrimStart().StartsWith("@")) {
                    WriteOutput(line);
                }
                return line;
            }
            // Batch file ended, continue with next batch or user input
            return ReadNextLine();
        }

        // For now, no interactive input - shell exits when no batch files remain
        return null;
    }

    /// <summary>
    /// Parses and executes a command line.
    /// </summary>
    /// <returns>True if an external program was loaded and shell should exit.</returns>
    public bool ParseAndExecute(string line) {
        // Remove leading @ sign (suppresses echo for this line only)
        if (line.StartsWith("@")) {
            line = line.Substring(1).TrimStart();
        }

        // Skip empty lines and comments (REM)
        if (string.IsNullOrWhiteSpace(line) || 
            line.TrimStart().StartsWith("REM", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        // Parse the command
        int spaceIndex = line.IndexOf(' ');
        string command = spaceIndex > 0 ? line.Substring(0, spaceIndex) : line;
        string args = spaceIndex > 0 ? line.Substring(spaceIndex + 1).TrimStart() : string.Empty;

        // Execute internal command or external program
        return ExecuteCommand(command.ToUpperInvariant(), args);
    }

    private bool ExecuteCommand(string command, string args) {
        // Internal commands
        switch (command) {
            case "ECHO":
                CmdEcho(args);
                return false;
            case "IF":
                CmdIf(args);
                return false;
            case "GOTO":
                CmdGoto(args);
                return false;
            case "CALL":
                CmdCall(args);
                return false;
            case "PAUSE":
                CmdPause();
                return false;
            case "SHIFT":
                CmdShift();
                return false;
            case "SET":
                CmdSet(args);
                return false;
            case "CLS":
                CmdCls();
                return false;
            case "EXIT":
                CmdExit();
                return false;
            case "REM":
                // Comment - do nothing
                return false;
            default:
                // Try to execute as external program or batch file
                return ExecuteExternalCommand(command, args);
        }
    }

    #region Internal Commands

    /// <summary>
    /// ECHO command - display messages or control command echoing.
    /// </summary>
    private void CmdEcho(string args) {
        args = args.Trim();

        if (string.IsNullOrEmpty(args)) {
            // Display current ECHO state
            WriteOutput(_batchManager.Echo ? "ECHO is on" : "ECHO is off");
        } else if (args.Equals("ON", StringComparison.OrdinalIgnoreCase)) {
            _batchManager.SetEcho(true);
            _echo = true;
        } else if (args.Equals("OFF", StringComparison.OrdinalIgnoreCase)) {
            _batchManager.SetEcho(false);
            _echo = false;
        } else {
            // Display the message
            WriteOutput(args);
        }
    }

    /// <summary>
    /// IF command - conditional execution.
    /// </summary>
    private void CmdIf(string args) {
        bool negate = false;
        string remaining = args.Trim();

        // Check for NOT
        if (remaining.StartsWith("NOT ", StringComparison.OrdinalIgnoreCase)) {
            negate = true;
            remaining = remaining.Substring(4).TrimStart();
        }

        bool condition = false;

        // IF ERRORLEVEL n
        if (remaining.StartsWith("ERRORLEVEL ", StringComparison.OrdinalIgnoreCase)) {
            remaining = remaining.Substring(11).TrimStart();
            int spaceIdx = remaining.IndexOf(' ');
            if (spaceIdx > 0) {
                string levelStr = remaining.Substring(0, spaceIdx);
                if (int.TryParse(levelStr, out int level)) {
                    condition = _errorLevel >= level;
                    remaining = remaining.Substring(spaceIdx + 1).TrimStart();
                } else {
                    return; // Invalid ERRORLEVEL
                }
            }
        }
        // IF EXIST filename
        else if (remaining.StartsWith("EXIST ", StringComparison.OrdinalIgnoreCase)) {
            remaining = remaining.Substring(6).TrimStart();
            int spaceIdx = remaining.IndexOf(' ');
            if (spaceIdx > 0) {
                string filename = remaining.Substring(0, spaceIdx);
                string? fullPath = _fileManager.TryGetFullHostPathFromDos(filename);
                condition = fullPath != null && File.Exists(fullPath);
                remaining = remaining.Substring(spaceIdx + 1).TrimStart();
            }
        }
        // IF string1==string2
        else {
            int equalsIdx = remaining.IndexOf("==");
            if (equalsIdx > 0) {
                string str1 = remaining.Substring(0, equalsIdx).Trim().Trim('"');
                remaining = remaining.Substring(equalsIdx + 2).TrimStart();
                int spaceIdx = remaining.IndexOf(' ');
                if (spaceIdx > 0) {
                    string str2 = remaining.Substring(0, spaceIdx).Trim().Trim('"');
                    condition = str1.Equals(str2, StringComparison.Ordinal);
                    remaining = remaining.Substring(spaceIdx + 1).TrimStart();
                } else {
                    string str2 = remaining.Trim().Trim('"');
                    condition = str1.Equals(str2, StringComparison.Ordinal);
                    remaining = string.Empty;
                }
            }
        }

        // Apply NOT if present
        if (negate) {
            condition = !condition;
        }

        // Execute command if condition is true
        // Note: We don't propagate the return value here because IF commands in batch files
        // don't suspend batch execution - the condition is evaluated and command executed inline
        if (condition && !string.IsNullOrEmpty(remaining)) {
            ParseAndExecute(remaining);
        }
    }

    /// <summary>
    /// GOTO command - jump to a label in a batch file.
    /// </summary>
    private void CmdGoto(string args) {
        if (!_batchManager.IsExecutingBatch) {
            WriteOutput("GOTO can only be used in batch files");
            return;
        }

        string label = args.Trim();
        if (string.IsNullOrEmpty(label)) {
            WriteOutput("Label not specified");
            return;
        }

        // Remove leading colon if present
        if (label.StartsWith(":")) {
            label = label.Substring(1);
        }

        if (!_batchManager.Goto(label)) {
            WriteOutput($"Label '{label}' not found");
        }
    }

    /// <summary>
    /// CALL command - execute a batch file or command and return.
    /// </summary>
    private void CmdCall(string args) {
        args = args.Trim();
        if (string.IsNullOrEmpty(args)) {
            return;
        }

        // Parse command and arguments
        int spaceIdx = args.IndexOf(' ');
        string batchFile = spaceIdx > 0 ? args.Substring(0, spaceIdx) : args;
        string batchArgs = spaceIdx > 0 ? args.Substring(spaceIdx + 1) : string.Empty;

        // Add .BAT extension if not present
        if (!batchFile.EndsWith(".BAT", StringComparison.OrdinalIgnoreCase)) {
            batchFile += ".BAT";
        }

        // Load the batch file
        string? fullPath = _fileManager.TryGetFullHostPathFromDos(batchFile);
        if (fullPath != null && File.Exists(fullPath)) {
            try {
                FileStream fileStream = File.OpenRead(fullPath);
                ILineReader reader = new FileLineReader(new SimpleVirtualFile(fileStream, Path.GetFileName(batchFile)));
                BatchFile batch = new BatchFile(reader, _environment,
                    Path.GetFileName(batchFile), batchArgs, _echo);
                _batchManager.PushBatchFile(batch);
            } catch (IOException e) {
                if (_logger.IsEnabled(LogEventLevel.Warning)) {
                    _logger.Warning(e, "Failed to open batch file: {Error}", e.Message);
                }
            }
        } else {
            WriteOutput($"Batch file not found: {batchFile}");
        }
    }

    /// <summary>
    /// PAUSE command - wait for user input.
    /// </summary>
    private void CmdPause() {
        WriteOutput("Press any key to continue . . .");
        // In real implementation, would wait for keypress
        // For now, this is a no-op in non-interactive mode
    }

    /// <summary>
    /// SHIFT command - shift batch file parameters.
    /// </summary>
    private void CmdShift() {
        if (_batchManager.IsExecutingBatch) {
            _batchManager.Shift();
        }
    }

    /// <summary>
    /// SET command - display or set environment variables.
    /// </summary>
    private void CmdSet(string args) {
        args = args.Trim();

        if (string.IsNullOrEmpty(args)) {
            // Display all environment variables
            foreach (var kvp in _environment) {
                WriteOutput($"{kvp.Key}={kvp.Value}");
            }
        } else {
            int equalsIdx = args.IndexOf('=');
            if (equalsIdx > 0) {
                string name = args.Substring(0, equalsIdx).Trim();
                string value = args.Substring(equalsIdx + 1).Trim();

                if (string.IsNullOrEmpty(value)) {
                    // Remove variable
                    _environment.Remove(name);
                } else {
                    // Set variable
                    _environment[name] = value;
                }
            }
        }
    }

    /// <summary>
    /// CLS command - clear the screen.
    /// </summary>
    private void CmdCls() {
        // In real implementation, would clear the console
        // For now, this is a no-op
        _logger.Information("CLS command executed");
    }

    /// <summary>
    /// EXIT command - exit the shell.
    /// </summary>
    private void CmdExit() {
        // Clear all batch files to exit
        _batchManager.ClearAllBatchFiles();
    }

    #endregion

    /// <summary>
    /// Executes an external command or batch file via DOS INT 21h/4Bh.
    /// </summary>
    /// <returns>True if an external program was loaded and shell should exit to let it execute.</returns>
    private bool ExecuteExternalCommand(string command, string args) {
        string batchFileName = command.EndsWith(".BAT", StringComparison.OrdinalIgnoreCase)
            ? command
            : command + ".BAT";

        string? batchHostPath = ResolveHostPath(batchFileName);
        if (batchHostPath != null && File.Exists(batchHostPath)) {
            // Use DosFileManager only for path resolution, open file directly via host file system
            try {
                FileStream fileStream = File.OpenRead(batchHostPath);
                ILineReader reader = new FileLineReader(new SimpleVirtualFile(fileStream, Path.GetFileName(batchFileName)));
                BatchFile batch = new BatchFile(reader, _environment,
                    Path.GetFileName(batchFileName), args, _echo);
                _batchManager.PushBatchFile(batch);
                return false;
            } catch (IOException e) {
                if (_logger.IsEnabled(LogEventLevel.Warning)) {
                    _logger.Warning(e, "Failed to open batch file: {Error}", e.Message);
                }
            }
        }

        string? dosProgramPath = ResolveExecutableDosPath(command);
        if (dosProgramPath != null) {
            return StartExternalProgram(dosProgramPath, args);
        }

        WriteOutput($"Command or batch file not found: {command}");
        return false;
    }

    private string? ResolveExecutableDosPath(string command) {
        List<string> candidates = new List<string>();

        if (command.EndsWith(".COM", StringComparison.OrdinalIgnoreCase) ||
            command.EndsWith(".EXE", StringComparison.OrdinalIgnoreCase)) {
            candidates.Add(command);
        } else {
            candidates.Add(command + ".COM");
            candidates.Add(command + ".EXE");
        }

        foreach (string candidate in candidates) {
            string? hostPath = ResolveHostPath(candidate);
            if (hostPath != null) {
                return BuildDosPathFromHost(hostPath);
            }
        }

        return null;
    }

    private string BuildDosPathFromHost(string hostPath) {
        string normalizedHostPath = Path.GetFullPath(hostPath);

        foreach (KeyValuePair<char, VirtualDrive> drive in _driveManager) {
            VirtualDrive? virtualDrive = drive.Value;
            if (virtualDrive == null) {
                continue;
            }

            string mountRoot = Path.GetFullPath(virtualDrive.MountedHostDirectory);
            if (normalizedHostPath.StartsWith(mountRoot, StringComparison.OrdinalIgnoreCase)) {
                string relative = normalizedHostPath[mountRoot.Length..];
                relative = relative.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string combined = Path.Combine($"{virtualDrive.DosVolume}{DosPathResolver.DirectorySeparatorChar}", relative);
                return combined.Replace(Path.AltDirectorySeparatorChar, DosPathResolver.DirectorySeparatorChar);
            }
        }

        string fileName = Path.GetFileName(normalizedHostPath);
        return $"{_driveManager.CurrentDrive.DosVolume}{DosPathResolver.DirectorySeparatorChar}{fileName}";
    }

    private string? ResolveHostPath(string dosPath) {
        string? hostPath = _fileManager.TryGetFullHostPathFromDos(dosPath);
        if (hostPath != null && File.Exists(hostPath)) {
            return hostPath;
        }

        return null;
    }

    private bool StartExternalProgram(string dosPath, string args) {
        DosExecParameterBlock paramBlock = new DosExecParameterBlock(new ByteArrayReaderWriter(new byte[DosExecParameterBlock.Size]), 0);
        DosExecResult result = _int21.LoadAndExecute(dosPath, paramBlock, commandTail: args);
        // Return true if program was loaded successfully - shell should exit loop to let program execute
        // When program terminates, it will return control via INT 22h which will resume batch processing
        return result.Success;
    }

    /// <summary>
    /// Executes the AUTOEXEC.BAT file if it exists in the root of drive C:.
    /// This is called via callback when the shell stub executes the FE 38 instruction.
    /// </summary>
    public void ExecuteAutoexec() {
        string dosPath = "AUTOEXEC.BAT";
        
        // Resolve DOS path to host path using DosFileManager
        string? hostPath = _fileManager.TryGetFullHostPathFromDos(dosPath);
        if (hostPath == null || !File.Exists(hostPath)) {
            return;
        }

        if (_logger.IsEnabled(LogEventLevel.Information)) {
            _logger.Information("Executing batch file: {BatchFile}", Path.GetFileName(hostPath));
        }

        // Open file using host file system directly to avoid DOS file handle complexity
        try {
            FileStream fileStream = File.OpenRead(hostPath);
            ILineReader reader = new FileLineReader(new SimpleVirtualFile(fileStream, Path.GetFileName(hostPath)));
            BatchFile batch = new BatchFile(reader, _environment, Path.GetFileName(hostPath), string.Empty, true);
            _batchManager.PushBatchFile(batch);
        } catch (IOException e) {
            if (_logger.IsEnabled(LogEventLevel.Warning)) {
                _logger.Warning(e, "Failed to open AUTOEXEC.BAT: {Error}", e.Message);
            }
        }
    }

    /// <summary>
    /// Writes output to the console/log.
    /// </summary>
    private void WriteOutput(string message) {
        // In real implementation, would write to DOS stdout
        _logger.Information("[SHELL] {Message}", message);
    }
}
