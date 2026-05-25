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

        string cDrive = ResolveStartupDriveBasePath(file);
        string requestedProgramDosPath = ResolveStartupProgramDosPath(file, cDrive);
        string commandTail = arguments ?? string.Empty;
        bool shouldTerminateAfterStartupProgram = !string.IsNullOrWhiteSpace(requestedProgramDosPath) &&
            !_configuration.ShellBootstrap;
        _processManager.BatchExecutionEngine.ConfigureStartupSession(requestedProgramDosPath, commandTail,
            shouldTerminateAfterStartupProgram);

        if (string.IsNullOrWhiteSpace(requestedProgramDosPath)) {
            DosExecResult commandComLoadResult = _processManager.LoadInitialCommandComShell();
            if (!commandComLoadResult.Success) {
                _state.AX = (ushort)commandComLoadResult.ErrorCode;
                _state.IsRunning = false;
                return Array.Empty<byte>();
            }

            return InternalBatchProgramBuilder.BuildCommandComProgramBytes();
        }

        return LoadStartupShellSession();
    }

    private byte[] LoadStartupShellSession() {
        DosExecParameterBlock paramBlock = new(new ByteArrayReaderWriter(new byte[DosExecParameterBlock.Size]), 0);
        _processManager.MarkRootShellSessionStarted();
        bool hasLaunchRequest = _processManager.BatchExecutionEngine.StartSession(out LaunchRequest launchRequest);
        while (hasLaunchRequest) {
            if (!_processManager.BatchExecutionEngine.ApplyRedirectionForLaunch(launchRequest)) {
                _processManager.LastChildReturnCode = BuildCriticalErrorReturnCode(DosErrorCode.PathNotFound);
                hasLaunchRequest = _processManager.BatchExecutionEngine.ContinueSession(_processManager.LastChildReturnCode, out launchRequest);
                continue;
            }

            DosExecResult result = LoadLaunchRequest(launchRequest, paramBlock);
            if (result.Success) {
                string? launchedHostPath = GetHostPathForLaunchedProgram(launchRequest);
                if (!string.IsNullOrWhiteSpace(launchedHostPath) && File.Exists(launchedHostPath)) {
                    return File.ReadAllBytes(launchedHostPath);
                }

                return Array.Empty<byte>();
            }

            _processManager.BatchExecutionEngine.RestoreStandardHandlesAfterLaunch();
            _processManager.LastChildReturnCode = BuildCriticalErrorReturnCode(result.ErrorCode);
            hasLaunchRequest = _processManager.BatchExecutionEngine.ContinueSession(_processManager.LastChildReturnCode, out launchRequest);
        }

        _state.AX = (ushort)(_processManager.LastChildReturnCode & 0x00FF);
        _state.IsRunning = false;
        return Array.Empty<byte>();
    }

    private string ResolveStartupDriveBasePath(string file) {
        string? configuredDrive = _configuration.CDrive;
        if (!string.IsNullOrWhiteSpace(configuredDrive)) {
            return configuredDrive;
        }

        if (!string.IsNullOrWhiteSpace(file)) {
            string? fileDirectory = Path.GetDirectoryName(file);
            if (!string.IsNullOrWhiteSpace(fileDirectory)) {
                return fileDirectory;
            }
        }

        return Environment.CurrentDirectory;
    }

    private static string ResolveStartupProgramDosPath(string file, string cDrive) {
        if (string.IsNullOrWhiteSpace(file)) {
            return string.Empty;
        }

        if (file.Length >= cDrive.Length) {
            return $"C:{file[cDrive.Length..]}";
        }

        string fileName = Path.GetFileName(file);
        return $"C:{fileName}";
    }

    private static ushort BuildCriticalErrorReturnCode(DosErrorCode errorCode) {
        return (ushort)(((ushort)DosTerminationType.CriticalError << 8) | (byte)errorCode);
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
        if (!_processManager.TryGetMountedImageForBoot(upper, out byte[]? imageData, out string imagePath)) {
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
