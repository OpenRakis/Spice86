namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.Boot;
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
    private readonly FloppyBootService _floppyBootService;

    public DosProgramLoader(Configuration configuration, IMemory memory,
        State state, DosInt21Handler int21Handler,
        ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _configuration = configuration;
        _processManager = int21Handler.ProcessManager;
        _fileManager = int21Handler.FileManager;
        _floppyBootService = new FloppyBootService(memory, state, loggerService);
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
            if (!_processManager.BatchExecutionEngine.ApplyRedirectionForLaunch(launchRequest)) {
                _state.IsRunning = false;
                return File.ReadAllBytes(file);
            }

            DosExecResult result = LoadLaunchRequest(launchRequest, paramBlock);

            if (!result.Success) {
                _processManager.BatchExecutionEngine.RestoreStandardHandlesAfterLaunch();
                _state.IsRunning = false;
                return File.ReadAllBytes(file);
            }

            string? launchedHostPath = GetHostPathForLaunchedProgram(launchRequest);
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
        if (launchRequest is BootFloppyLaunchRequest bootFloppy) {
            return ExecuteFloppyBoot(bootFloppy);
        }

        if (launchRequest is not ProgramLaunchRequest programLaunchRequest) {
            return DosExecResult.Fail(DosErrorCode.InvalidDrive);
        }

        return _processManager.LoadInitialProgram(programLaunchRequest.ProgramName, paramBlock,
            programLaunchRequest.CommandTail, paramBlock.EnvironmentSegment);
    }

    private DosExecResult ExecuteFloppyBoot(BootFloppyLaunchRequest request) {
        char upper = char.ToUpperInvariant(request.DriveLetter);
        if (upper != 'A' && upper != 'B') {
            return DosExecResult.Fail(DosErrorCode.InvalidDrive);
        }
        if (!_processManager.TryGetFloppyImageForBoot(upper, out byte[]? imageData, out string imagePath)) {
            return DosExecResult.Fail(DosErrorCode.InvalidDrive);
        }
        byte driveNumber = (byte)(upper - 'A');
        if (!_floppyBootService.TryBootFromFloppyImage(imageData, driveNumber, imagePath)) {
            return DosExecResult.Fail(DosErrorCode.InvalidDrive);
        }
        return DosExecResult.SuccessExecute(_state.CS, _state.IP, _state.SS, _state.SP);
    }

    protected virtual string? GetHostPathForLaunchedProgram(LaunchRequest launchRequest) {
        if (launchRequest is not ProgramLaunchRequest programLaunchRequest) {
            return null;
        }

        return _fileManager.GetFullHostExecutablePathFromDos(programLaunchRequest.ProgramName);
    }
}
