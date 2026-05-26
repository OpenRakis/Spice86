namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Batch;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System;

internal sealed class CommandShell {
    private const byte CommandComInterruptNumber = 0x2E;
    private const string CommandComFileName = "COMMAND.COM";
    internal const string CommandComPath = "Z:\\COMMAND.COM";
    internal const string ShellPathValue = "Z:\\";
    internal const string StartupCommandTail = "/INIT AUTOEXEC.BAT";

    private readonly DosBatchExecutionEngine _batchExecutionEngine;
    private readonly byte[] _commandComProgramBytes;
    private readonly DosDriveManager _driveManager;
    private bool _launchInteractiveShellAfterStartup;
    private bool _shellSessionStarted;
    private bool _startupSessionStarted;

    internal CommandShell(DosBatchExecutionEngine batchExecutionEngine, DosDriveManager driveManager) {
        _batchExecutionEngine = batchExecutionEngine;
        _driveManager = driveManager;
        _commandComProgramBytes = BuildCommandComProgramBytes();
    }

    internal void ResetStartupSession() {
        _launchInteractiveShellAfterStartup = false;
        _shellSessionStarted = false;
        _startupSessionStarted = false;
    }

    internal void ConfigureStartupSession(string requestedProgramDosPath, string commandTail,
        bool shouldTerminateAfterStartupProgram) {
        EnsureCommandComInMemoryDrive();
        _launchInteractiveShellAfterStartup = !shouldTerminateAfterStartupProgram;
        _batchExecutionEngine.ConfigureStartupSession(requestedProgramDosPath, commandTail,
            shouldTerminateAfterStartupProgram);
        _startupSessionStarted = false;
    }

    internal bool ShouldEnterInteractiveShellAfterStartup() {
        return _launchInteractiveShellAfterStartup;
    }

    internal bool TryStartStartupSession(out LaunchRequest launchRequest) {
        _startupSessionStarted = true;
        return _batchExecutionEngine.TryStartNonInteractiveSession(out launchRequest);
    }

    internal bool TryContinueStartupSession(ushort lastChildReturnCode, out LaunchRequest launchRequest) {
        if (!_startupSessionStarted) {
            return TryStartStartupSession(out launchRequest);
        }

        return _batchExecutionEngine.TryContinueNonInteractiveSession(lastChildReturnCode, out launchRequest);
    }

    internal bool ApplyRedirectionForLaunch(LaunchRequest launchRequest) {
        return _batchExecutionEngine.ApplyRedirectionForLaunch(launchRequest);
    }

    internal void RestoreStandardHandlesAfterLaunch() {
        _batchExecutionEngine.RestoreStandardHandlesAfterLaunch();
    }

    internal bool TryEnterShellSession(ushort lastChildReturnCode, out LaunchRequest launchRequest) {
        if (!_shellSessionStarted) {
            _shellSessionStarted = true;
            if (_startupSessionStarted) {
                return _batchExecutionEngine.ContinueSession(lastChildReturnCode, out launchRequest);
            }

            return _batchExecutionEngine.StartSession(out launchRequest);
        }

        return _batchExecutionEngine.ContinueSession(lastChildReturnCode, out launchRequest);
    }

    internal byte[] GetProgramBytes() {
        return _commandComProgramBytes;
    }

    internal bool IsCommandComProgram(string programName) {
        return string.Equals(programName, CommandComFileName, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureCommandComInMemoryDrive() {
        if (!_driveManager.TryGetMemoryDrive('Z', out MemoryDrive? zDrive)) {
            return;
        }

        zDrive.AddFile(CommandComFileName, _commandComProgramBytes);
    }

    private static byte[] BuildCommandComProgramBytes() {
        return [
            0xCD, CommandComInterruptNumber,
            0xB8, 0x00, 0x4C,
            0xCD, 0x21
        ];
    }
}