namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.CLI;
using Serilog.Events;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.LoadableFile;
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

/// <summary>
/// Loader that creates a tiny COM program to invoke the DOS shell via a callback.
/// Follows the DOSBox Staging approach where the shell runs within the emulation loop via CPU callbacks.
/// The callback instruction (FE 38 XX XX) triggers the shell to process batch files.
/// </summary>
internal sealed class ShellLoader : ExecutableFileLoader {
    private readonly Configuration _configuration;
    private readonly DosInt21Handler _int21Handler;
    private readonly CallbackHandler _callbackHandler;
    private DosShell? _shell;

    public override bool DosInitializationNeeded => true;

    public ShellLoader(Configuration configuration, IMemory memory, State state,
        DosInt21Handler int21Handler, CallbackHandler callbackHandler, ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _configuration = configuration;
        _int21Handler = int21Handler;
        _callbackHandler = callbackHandler;
    }

    public override byte[] LoadFile(string file, string? arguments) {
        _int21Handler.ProcessManager.CreateRootCommandComPsp();

        // Copy batch file to C: drive if needed and get DOS path
        string dosBatchPath = EnsureBatchFileAvailable(file);

        // Create AUTOEXEC.BAT via DosFileManager
        CreateAutoexecBat(dosBatchPath, arguments);

        // Create shell instance that will be invoked via callback
        _shell = new DosShell(
            _int21Handler.BatchFileManager,
            _int21Handler.FileManager,
            _int21Handler.DriveManager,
            _int21Handler.EnvironmentVariables,
            _configuration,
            _loggerService,
            _int21Handler);

        // Write callback stub to memory and set CS:IP
        return GenerateShellInvocationStub();
    }

    /// <summary>
    /// Ensures the batch file is available and returns its DOS path.
    /// If the file is already on a mounted drive, returns its DOS path.
    /// Otherwise copies it to C: and returns C:\filename
    /// </summary>
    private string EnsureBatchFileAvailable(string hostBatchPath) {
        if (!File.Exists(hostBatchPath)) {
            throw new FileNotFoundException($"Batch file not found: {hostBatchPath}");
        }

        string fileName = Path.GetFileName(hostBatchPath);
        
        // First try to find the file on the C: drive using DosFileManager's path resolution
        string dosPath = $"C:\\{fileName}";
        string? hostPath = _int21Handler.FileManager.TryGetFullHostPathFromDos(dosPath);
        
        if (hostPath != null && File.Exists(hostPath)) {
            // File already exists on C:
            return dosPath;
        }

        // File doesn't exist on C:, create it via DosFileManager
        byte[] batchContent = File.ReadAllBytes(hostBatchPath);
        
        DosFileOperationResult createResult = _int21Handler.FileManager.CreateFileUsingHandle(dosPath, 0);
        if (createResult.IsError || !createResult.Value.HasValue) {
            throw new InvalidOperationException($"Failed to create batch file: {dosPath}");
        }

        ushort handle = (ushort)createResult.Value.Value;
        WriteDataToFile(handle, batchContent);
        _int21Handler.FileManager.CloseFileOrDevice(handle);

        return dosPath;
    }

    /// <summary>
    /// Creates AUTOEXEC.BAT that calls the user's batch file.
    /// </summary>
    private void CreateAutoexecBat(string dosBatchPath, string? arguments) {
        string dosAutoexecPath = "C:\\AUTOEXEC.BAT";

        // Build AUTOEXEC.BAT content
        StringBuilder content = new StringBuilder();
        content.AppendLine("@ECHO OFF");
        
        string trimmedArgs = string.IsNullOrWhiteSpace(arguments) ? string.Empty : arguments.Trim();
        string commandLine = string.IsNullOrEmpty(trimmedArgs) ? dosBatchPath : $"{dosBatchPath} {trimmedArgs}";
        content.AppendLine(commandLine);

        byte[] autoexecBytes = Encoding.ASCII.GetBytes(content.ToString());

        // Create AUTOEXEC.BAT via DosFileManager
        DosFileOperationResult createResult = _int21Handler.FileManager.CreateFileUsingHandle(dosAutoexecPath, 0);
        if (createResult.IsError || !createResult.Value.HasValue) {
            throw new InvalidOperationException("Failed to create AUTOEXEC.BAT");
        }

        ushort handle = (ushort)createResult.Value.Value;
        WriteDataToFile(handle, autoexecBytes);
        _int21Handler.FileManager.CloseFileOrDevice(handle);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            string? hostPath = _int21Handler.FileManager.TryGetFullHostPathFromDos(dosAutoexecPath);
            _loggerService.Information("AUTOEXEC.BAT created at {HostPath} (DOS: {DosPath})", hostPath, dosAutoexecPath);
        }
    }

    /// <summary>
    /// Writes data to a file through DosFileManager by copying to memory first.
    /// </summary>
    private void WriteDataToFile(ushort fileHandle, byte[] data) {
        if (data.Length == 0) {
            return;
        }

        // Find a free area in memory to temporarily hold the data
        // Use the area just after the PSP (CS:0200 is typical for COM file load area, but we use higher)
        ushort pspSegment = _int21Handler.ProcessManager.PspTracker.GetCurrentPspSegment();
        SegmentedAddress tempBuffer = new SegmentedAddress(pspSegment, 0x0200);
        uint tempBufferAddress = MemoryUtils.ToPhysicalAddress(tempBuffer.Segment, tempBuffer.Offset);

        // Write data to memory
        for (int i = 0; i < data.Length; i++) {
            _memory.UInt8[tempBufferAddress + (uint)i] = data[i];
        }

        // Write from memory to file via DosFileManager
        DosFileOperationResult writeResult = _int21Handler.FileManager.WriteToFileOrDevice(
            fileHandle, 
            (ushort)data.Length, 
            tempBufferAddress);

        if (writeResult.IsError) {
            throw new InvalidOperationException("Failed to write data to file");
        }
    }

    /// <summary>
    /// Generates and writes a tiny COM program that invokes the shell via callback instruction.
    /// Following DOSBox's approach: uses MemoryAsmWriter to write callback + DOS terminate.
    /// The stub is written to CS:0x100 in the COMMAND.COM PSP area.
    /// The shell's terminate vector is set to point to this stub, so when child programs
    /// terminate via INT 22h, execution resumes at the callback for continued batch processing.
    /// </summary>
    private byte[] GenerateShellInvocationStub() {
        // Get the current PSP segment (COMMAND.COM)
        ushort pspSegment = _int21Handler.ProcessManager.PspTracker.GetCurrentPspSegment();
        SegmentedAddress stubAddress = new SegmentedAddress(pspSegment, 0x0100);

        // Use MemoryAsmWriter to write the callback stub directly to memory
        MemoryAsmWriter writer = new MemoryAsmWriter(_memory, stubAddress, _callbackHandler);

        // Write callback instruction - MemoryAsmWriter allocates callback number and registers it
        // This uses the correct encoding: FE 38 XX XX (5 bytes: FE 38 + 2-byte callback number)
        writer.RegisterAndWriteCallback(ShellCallbackHandler);

        // After callback returns, continue to DOS terminate
        // Write DOS terminate: mov ah, 0x4C; int 0x21
        writer.WriteUInt8(0xB4);  // mov ah, imm8
        writer.WriteUInt8(0x4C);  // 0x4C (terminate with return code)
        writer.WriteInt(0x21);    // int 0x21

        // Set CPU to start at CS:0x100
        _state.CS = pspSegment;
        _state.IP = 0x0100;

        // CRITICAL: Set the shell PSP's terminate vector (INT 22h) to point to the callback stub.
        // This ensures that when child programs terminate, execution resumes at the callback stub
        // for continued batch file processing, rather than exiting COMMAND.COM.
        DosProgramSegmentPrefix shellPsp = _int21Handler.ProcessManager.PspTracker.GetCurrentPsp();
        shellPsp.TerminateAddress = MemoryUtils.To32BitAddress(stubAddress.Segment, stubAddress.Offset);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Shell callback stub written to {Address}", stubAddress);
            _loggerService.Information("Shell PSP terminate vector set to {Address} for batch resume", stubAddress);
        }

        // Return empty array since the stub is now in memory
        // (the return value is only used for checksumming)
        return Array.Empty<byte>();
    }

    /// <summary>
    /// Callback handler invoked by the CPU when it executes the callback instruction.
    /// This runs the shell's command processing loop to handle AUTOEXEC.BAT and all batch files.
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
            // Execute the shell's main loop which handles AUTOEXEC.BAT and processes all batch files
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
