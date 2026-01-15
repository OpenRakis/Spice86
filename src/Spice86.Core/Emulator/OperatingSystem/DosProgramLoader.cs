namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Shell;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;
using System.IO;
using System.Text;

internal class DosProgramLoader : DosFileLoader {
    private readonly Configuration _configuration;
    private readonly DosInt21Handler _int21;
    private readonly CallbackHandler _callbackHandler;
    private DosShell? _shell;
    
    public DosProgramLoader(Configuration configuration, IMemory memory,
        State state, DosInt21Handler int21Handler,
        CallbackHandler callbackHandler,
        ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _configuration = configuration;
        _int21 = int21Handler;
        _callbackHandler = callbackHandler;
    }

    public override byte[] LoadFile(string file, string? arguments) {
        // Ensure root COMMAND.COM PSP exists before loading any programs
        _int21.ProcessManager.CreateRootCommandComPsp();
        
        // Determine C drive base path
        string? cDrive = _configuration.CDrive;

        if (string.IsNullOrWhiteSpace(cDrive)) {
            cDrive = Path.GetDirectoryName(file) ?? "C:\\";
        }

        // Convert host file path to DOS path relative to C drive
        string dosPath;

        if (file.Length >= cDrive.Length) {
            dosPath = $"C:{file[cDrive.Length..]}";
        } else {
            string fileName = Path.GetFileName(file);
            dosPath = $"C:\\{fileName}";
        }

        // Create AUTOEXEC.BAT that calls the program and then exits
        // This ensures the program runs as a child of the shell and returns properly
        CreateAutoexecBat(dosPath, arguments);

        // Create shell instance that will be invoked via callback
        _shell = new DosShell(
            _int21.BatchFileManager,
            _int21.FileManager,
            _int21.DriveManager,
            _int21.EnvironmentVariables,
            _configuration,
            _loggerService,
            _int21);

        // Write callback stub to memory and set CS:IP (same as ShellLoader)
        return GenerateShellInvocationStub();
    }

    /// <summary>
    /// Creates AUTOEXEC.BAT that runs the program and then exits.
    /// </summary>
    private void CreateAutoexecBat(string dosPath, string? arguments) {
        string dosAutoexecPath = "C:\\AUTOEXEC.BAT";

        // Build AUTOEXEC.BAT content:
        // - @ECHO OFF to suppress command echoing
        // - program.exe [args] to execute the program (it will return to shell on termination)
        // - EXIT to terminate the shell and emulator
        StringBuilder content = new StringBuilder();
        content.AppendLine("@ECHO OFF");
        
        string trimmedArgs = string.IsNullOrWhiteSpace(arguments) ? string.Empty : arguments.Trim();
        string commandLine = string.IsNullOrEmpty(trimmedArgs) ? dosPath : $"{dosPath} {trimmedArgs}";
        content.AppendLine(commandLine);
        content.AppendLine("EXIT");

        byte[] autoexecBytes = Encoding.ASCII.GetBytes(content.ToString());

        // Create AUTOEXEC.BAT via DosFileManager
        DosFileOperationResult createResult = _int21.FileManager.CreateFileUsingHandle(dosAutoexecPath, 0);
        if (createResult.IsError || !createResult.Value.HasValue) {
            throw new InvalidOperationException("Failed to create AUTOEXEC.BAT");
        }

        ushort handle = (ushort)createResult.Value.Value;
        WriteDataToFile(handle, autoexecBytes);
        _int21.FileManager.CloseFileOrDevice(handle);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            string? hostPath = _int21.FileManager.TryGetFullHostPathFromDos(dosAutoexecPath);
            _loggerService.Information("AUTOEXEC.BAT created at {HostPath} (DOS: {DosPath})", hostPath, dosAutoexecPath);
        }
    }

    /// <summary>
    /// Writes data to a file through DosFileManager.
    /// </summary>
    private void WriteDataToFile(ushort fileHandle, byte[] data) {
        if (data.Length == 0) {
            return;
        }

        // Find a free area in memory to temporarily hold the data
        ushort pspSegment = _int21.ProcessManager.PspTracker.GetCurrentPspSegment();
        uint tempBufferAddress = MemoryUtils.ToPhysicalAddress(pspSegment, 0x0200);

        // Write data to memory
        for (int i = 0; i < data.Length; i++) {
            _memory.UInt8[tempBufferAddress + (uint)i] = data[i];
        }

        // Write from memory to file via DosFileManager
        DosFileOperationResult writeResult = _int21.FileManager.WriteToFileOrDevice(
            fileHandle, 
            (ushort)data.Length, 
            tempBufferAddress);

        if (writeResult.IsError) {
            throw new InvalidOperationException("Failed to write data to file");
        }
    }

    /// <summary>
    /// Generates and writes a tiny COM program that invokes the shell via callback instruction.
    /// The stub is written to CS:0x100 in the COMMAND.COM PSP area.
    /// </summary>
    private byte[] GenerateShellInvocationStub() {
        // Get the current PSP segment (COMMAND.COM)
        ushort pspSegment = _int21.ProcessManager.PspTracker.GetCurrentPspSegment();
        SegmentedAddress stubAddress = new SegmentedAddress(pspSegment, 0x0100);

        // Use MemoryAsmWriter to write the callback stub directly to memory
        MemoryAsmWriter writer = new MemoryAsmWriter(_memory, stubAddress, _callbackHandler);

        // Write callback instruction - invokes shell command processing
        writer.RegisterAndWriteCallback(ShellCallbackHandler);

        // After callback returns, terminate COMMAND.COM
        writer.WriteUInt8(0xB4);  // mov ah, imm8
        writer.WriteUInt8(0x4C);  // 0x4C (terminate with return code)
        writer.WriteInt(0x21);    // int 0x21

        // Set CPU to start at CS:0x100
        _state.CS = pspSegment;
        _state.IP = 0x0100;

        // Set the shell PSP's terminate vector to point to the callback stub
        // This ensures that when child programs terminate, execution resumes at the callback stub
        DosProgramSegmentPrefix shellPsp = _int21.ProcessManager.PspTracker.GetCurrentPsp();
        shellPsp.TerminateAddress = MemoryUtils.To32BitAddress(stubAddress.Segment, stubAddress.Offset);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Shell callback stub written to {Address}", stubAddress);
            _loggerService.Information("Shell PSP terminate vector set to {Address} for batch resume", stubAddress);
        }

        // Return empty array since the stub is now in memory
        return Array.Empty<byte>();
    }

    /// <summary>
    /// Callback handler invoked by the CPU when it executes the callback instruction.
    /// This runs the shell's command processing loop to handle AUTOEXEC.BAT.
    /// </summary>
    private void ShellCallbackHandler() {
        if (_shell is null) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Shell instance is null when callback invoked");
            }
            return;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Shell callback invoked - starting shell loop");
        }

        try {
            // Execute the shell's main loop which handles AUTOEXEC.BAT
            _shell.Run();

            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("Shell callback completed - shell loop finished");
            }
        } catch (Exception ex) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error(ex, "Error during shell callback execution");
            }
            throw;
        }
    }
}
