namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Shell;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System;
using System.IO;

/// <summary>
/// Shell processor that handles AUTOEXEC.BAT processing via INT 2Fh.
/// Called by the minimal COMMAND.COM loop to process batch file commands.
/// </summary>
public class ShellProcessor {
    private readonly DosShell _shell;
    private readonly State _state;
    private readonly ILoggerService _loggerService;
    private readonly DosInt21Handler _int21;
    private bool _autoexecOpened = false;

    public ShellProcessor(DosShell shell, State state, DosInt21Handler int21Handler, ILoggerService loggerService) {
        _shell = shell;
        _state = state;
        _int21 = int21Handler;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Processes one command from AUTOEXEC.BAT.
    /// Called via INT 2Fh from COMMAND.COM loop.
    /// </summary>
    /// <returns>0 if should continue looping, 1 if should exit</returns>
    public byte ProcessNextCommand() {
        // First time: open AUTOEXEC.BAT
        if (!_autoexecOpened) {
            _shell.ExecuteAutoexec();
            _autoexecOpened = true;
        }

        // Read next line from batch file
        string? line = ReadNextLine();
        
        if (line == null) {
            // EOF - exit
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("ShellProcessor: AUTOEXEC.BAT EOF reached, exiting");
            }
            return 1; // Exit
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("ShellProcessor: Processing line: {Line}", line);
        }

        // Process the command
        bool shouldExit = ProcessLine(line);
        
        return shouldExit ? (byte)1 : (byte)0;
    }

    private string? ReadNextLine() {
        // Use the shell's batch manager to read the next line
        if (_shell.BatchFileManager.IsExecutingBatch) {
            if (_shell.BatchFileManager.ReadBatchLine(out string line)) {
                return line;
            }
        }
        return null;
    }

    private bool ProcessLine(string line) {
        // Trim and check for empty or comment
        line = line.Trim();
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("REM", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        // Check for EXIT command
        if (line.Equals("EXIT", StringComparison.OrdinalIgnoreCase)) {
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("ShellProcessor: EXIT command encountered");
            }
            return true; // Exit
        }

        // Check for ECHO OFF
        if (line.StartsWith("@ECHO OFF", StringComparison.OrdinalIgnoreCase) ||
            line.Equals("ECHO OFF", StringComparison.OrdinalIgnoreCase)) {
            // Echo is controlled by the batch file itself
            return false;
        }

        // Otherwise, try to execute as external program
        bool programLoaded = ExecuteProgram(line);
        
        // If a program was loaded, the CPU state (CS:IP) has been updated
        // We return false (continue) because after the child terminates,
        // INT 22h will bring us back to the COMMAND.COM loop
        return false;
    }

    private bool ExecuteProgram(string commandLine) {
        // Parse command line into program and arguments
        commandLine = commandLine.Trim();
        int spaceIndex = commandLine.IndexOf(' ');
        string programPath;
        string arguments;
        
        if (spaceIndex > 0) {
            programPath = commandLine.Substring(0, spaceIndex);
            arguments = commandLine.Substring(spaceIndex + 1).Trim();
        } else {
            programPath = commandLine;
            arguments = string.Empty;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("ShellProcessor: Executing program: {Program} with args: {Args}", 
                programPath, arguments);
        }

        // Execute via INT 21h/4Bh EXEC
        try {
            DosExecParameterBlock paramBlock = new DosExecParameterBlock(
                new Memory.ReaderWriter.ByteArrayReaderWriter(new byte[DosExecParameterBlock.Size]), 
                0);
            
            DosExecResult result = _int21.LoadAndExecute(programPath, paramBlock, commandTail: arguments);
            
            if (result.Success) {
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("ShellProcessor: Program loaded successfully, CS:IP now at {CS:X4}:{IP:X4}", 
                        _state.CS, _state.IP);
                }
                return true;
            } else {
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("ShellProcessor: Failed to load program: {Error}", result.ErrorCode);
                }
                return false;
            }
        } catch (Exception ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(ex, "ShellProcessor: Exception executing program");
            }
            return false;
        }
    }
}
