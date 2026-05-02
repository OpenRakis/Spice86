namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Batch;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using System.IO;

internal class DosProgramLoader : DosFileLoader {
    private readonly Configuration _configuration;
    protected readonly DosProcessManager _processManager;
    protected readonly DosFileManager _fileManager;

    public DosProgramLoader(Configuration configuration, IMemory memory,
        State state, DosInt21Handler int21Handler,
        ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _configuration = configuration;
        _processManager = int21Handler.ProcessManager;
        _fileManager = int21Handler.FileManager;
    }

    public override byte[] LoadFile(string file, string? arguments) {
        // Ensure root COMMAND.COM PSP exists before loading any programs
        _processManager.CreateRootCommandComPsp();

        // Determine C drive base path
        string? cDrive = _configuration.CDrive;

        if (string.IsNullOrWhiteSpace(cDrive)) {
            cDrive = Path.GetDirectoryName(file) ?? "C:\\";
        }

        // Convert host file path to DOS path relative to C drive
        string absoluteDosPath;

        if (file.Length >= cDrive.Length) {
            absoluteDosPath = $"C:{file[cDrive.Length..]}";
        } else {
            string fileName = Path.GetFileName(file);
            absoluteDosPath = $"C:{fileName}";
        }

        string commandTail = arguments ?? string.Empty;
        _processManager.BatchExecutionEngine.ConfigureHostStartupProgram(absoluteDosPath, commandTail);

        DosExecParameterBlock paramBlock = new(new ByteArrayReaderWriter(new byte[DosExecParameterBlock.Size]), 0);
        if (_processManager.BatchExecutionEngine.TryStart(out LaunchRequest launchRequest)) {
            if (!_processManager.BatchExecutionEngine.TryApplyRedirectionForLaunch(launchRequest)) {
                _state.IsRunning = false;
                return File.ReadAllBytes(file);
            }

            DosExecResult result = LoadLaunchRequest(launchRequest, paramBlock);

            if (!result.Success) {
                _processManager.BatchExecutionEngine.RestoreStandardHandlesAfterLaunch();
                _state.IsRunning = false;
                return File.ReadAllBytes(file);
            }

            string? launchedHostPath = TryGetHostPathForLaunchedProgram(launchRequest);
            if (!string.IsNullOrWhiteSpace(launchedHostPath) && File.Exists(launchedHostPath)) {
                return File.ReadAllBytes(launchedHostPath);
            }
        } else {
            _state.IsRunning = false;
        }

        return File.ReadAllBytes(file);
    }

    protected virtual DosExecResult LoadLaunchRequest(LaunchRequest launchRequest,
        DosExecParameterBlock paramBlock) {
        if (launchRequest is not ProgramLaunchRequest programLaunchRequest) {
            return DosExecResult.Fail(DosErrorCode.InvalidDrive);
        }

        return _processManager.LoadExternalProgram(programLaunchRequest.ProgramName, paramBlock,
            programLaunchRequest.CommandTail, paramBlock.EnvironmentSegment);
    }

    protected virtual string? TryGetHostPathForLaunchedProgram(LaunchRequest launchRequest) {
        if (launchRequest is not ProgramLaunchRequest programLaunchRequest) {
            return null;
        }

        return _fileManager.TryGetFullHostExecutablePathFromDos(programLaunchRequest.ProgramName);
    }
}
