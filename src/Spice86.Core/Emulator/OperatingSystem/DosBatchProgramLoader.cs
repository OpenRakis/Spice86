namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Batch;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

internal sealed class DosBatchProgramLoader : DosProgramLoader {
    public DosBatchProgramLoader(Configuration configuration, IMemory memory,
        State state, DosInt21Handler int21Handler,
        ILoggerService loggerService)
        : base(configuration, memory, state, int21Handler, loggerService) {
    }

    protected override DosExecResult LoadLaunchRequest(LaunchRequest launchRequest,
        DosExecParameterBlock paramBlock) {
        if (launchRequest is InternalProgramLaunchRequest internalProgramLaunchRequest) {
            return _processManager.LoadInternalProgram(internalProgramLaunchRequest.ComProgramBytes);
        }

        if (launchRequest is ProgramLaunchRequest programLaunchRequest) {
            return _processManager.LoadExternalProgram(programLaunchRequest.ProgramName, paramBlock,
                programLaunchRequest.CommandTail, paramBlock.EnvironmentSegment);
        }

        return DosExecResult.Fail(DosErrorCode.FormatInvalid);
    }

    protected override string? TryGetHostPathForLaunchedProgram(LaunchRequest launchRequest) {
        if (launchRequest is not ProgramLaunchRequest programLaunchRequest) {
            return null;
        }

        return _fileManager.TryGetFullHostExecutablePathFromDos(programLaunchRequest.ProgramName);
    }
}
