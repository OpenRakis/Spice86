namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;
using System.IO;
using System.Text;

/// <summary>
/// Loader that loads a minimal COMMAND.COM implementation and generates AUTOEXEC.BAT
/// for executing the target program. This replaces the callback-based shell approach
/// with authentic DOS program execution flow.
/// </summary>
internal class CommandComLoader : DosFileLoader {
    private readonly Configuration _configuration;
    private readonly DosInt21Handler _int21;
    private readonly MinimalCommandCom _commandCom;

    public CommandComLoader(Configuration configuration, IMemory memory,
        State state, DosInt21Handler int21Handler,
        ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _configuration = configuration;
        _int21 = int21Handler;
        _commandCom = new MinimalCommandCom(memory, loggerService);
    }

    public override byte[] LoadFile(string file, string? arguments) {
        // 1. Create root COMMAND.COM PSP
        _int21.ProcessManager.CreateRootCommandComPsp();

        // 2. Determine DOS path for the target program
        string? cDrive = _configuration.CDrive;
        if (string.IsNullOrWhiteSpace(cDrive)) {
            cDrive = Path.GetDirectoryName(file) ?? "C:\\";
        }

        string dosPath;
        if (file.Length >= cDrive.Length) {
            dosPath = $"C:{file[cDrive.Length..]}";
        } else {
            string fileName = Path.GetFileName(file);
            dosPath = $"C:\\{fileName}";
        }

        // 3. Generate AUTOEXEC.BAT with program execution + EXIT
        CreateAutoexecBat(dosPath, arguments);

        // 4. Load minimal COMMAND.COM binary at segment 0x60
        ushort commandComSegment = DosProcessManager.CommandComSegment;
        byte[] commandComBinary = _commandCom.GenerateCommandComBinary(commandComSegment);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("Loaded minimal COMMAND.COM ({Size} bytes) at segment {Segment:X4}",
                commandComBinary.Length, commandComSegment);
        }

        // 5. CPU is already set up by CreateRootCommandComPsp to point to 0x60:0x100
        // COMMAND.COM will start executing and process AUTOEXEC.BAT

        return commandComBinary;
    }

    /// <summary>
    /// Creates AUTOEXEC.BAT that runs the program and then exits.
    /// </summary>
    private void CreateAutoexecBat(string dosPath, string? arguments) {
        string dosAutoexecPath = "C:\\AUTOEXEC.BAT";

        // Build AUTOEXEC.BAT content:
        // - @ECHO OFF to suppress command echoing
        // - program.exe [args] to execute the program
        // - EXIT to terminate the shell
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
            _loggerService.Information("AUTOEXEC.BAT created at {HostPath}", hostPath);
            _loggerService.Information("Content: {Content}", content.ToString().Replace("\r\n", " | "));
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
}
