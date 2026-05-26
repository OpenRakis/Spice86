namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.OperatingSystem.Batch;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System;
using System.Diagnostics;
using System.IO;

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
        TraceShell($"SHELL: ConfigureStartupSession program='{requestedProgramDosPath}' tail='{commandTail}' terminateAfterStartup={shouldTerminateAfterStartupProgram}");
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
        bool hasLaunchRequest = _batchExecutionEngine.TryStartNonInteractiveSession(out launchRequest);
        TraceShell($"SHELL: TryStartStartupSession hasLaunchRequest={hasLaunchRequest} request={DescribeLaunchRequest(launchRequest)}");
        return hasLaunchRequest;
    }

    internal bool TryContinueStartupSession(ushort lastChildReturnCode, out LaunchRequest launchRequest) {
        TraceShell($"SHELL: TryContinueStartupSession lastChildReturnCode=0x{lastChildReturnCode:X4} startupStarted={_startupSessionStarted}");
        if (!_startupSessionStarted) {
            return TryStartStartupSession(out launchRequest);
        }

        bool hasLaunchRequest = _batchExecutionEngine.TryContinueNonInteractiveSession(lastChildReturnCode, out launchRequest);
        TraceShell($"SHELL: TryContinueStartupSession hasLaunchRequest={hasLaunchRequest} request={DescribeLaunchRequest(launchRequest)}");
        return hasLaunchRequest;
    }

    internal bool ApplyRedirectionForLaunch(LaunchRequest launchRequest) {
        TraceShell($"SHELL: ApplyRedirectionForLaunch request={DescribeLaunchRequest(launchRequest)}");
        bool result = _batchExecutionEngine.ApplyRedirectionForLaunch(launchRequest);
        TraceShell($"SHELL: ApplyRedirectionForLaunch result={result} request={DescribeLaunchRequest(launchRequest)}");
        return result;
    }

    internal void RestoreStandardHandlesAfterLaunch() {
        TraceShell("SHELL: RestoreStandardHandlesAfterLaunch");
        _batchExecutionEngine.RestoreStandardHandlesAfterLaunch();
    }

    internal bool TryEnterShellSession(ushort lastChildReturnCode, out LaunchRequest launchRequest) {
        TraceShell($"SHELL: TryEnterShellSession lastChildReturnCode=0x{lastChildReturnCode:X4} shellStarted={_shellSessionStarted}");
        if (!_shellSessionStarted) {
            _shellSessionStarted = true;
            bool hasLaunchRequest;
            if (_startupSessionStarted) {
                hasLaunchRequest = _batchExecutionEngine.ContinueSession(lastChildReturnCode, out launchRequest);
                TraceShell($"SHELL: TryEnterShellSession continue-after-startup hasLaunchRequest={hasLaunchRequest} request={DescribeLaunchRequest(launchRequest)}");
            } else {
                hasLaunchRequest = _batchExecutionEngine.StartSession(out launchRequest);
                TraceShell($"SHELL: TryEnterShellSession start hasLaunchRequest={hasLaunchRequest} request={DescribeLaunchRequest(launchRequest)}");
            }
            return hasLaunchRequest;
        }

        bool hasLaunchRequest2 = _batchExecutionEngine.ContinueSession(lastChildReturnCode, out launchRequest);
        TraceShell($"SHELL: TryEnterShellSession continue hasLaunchRequest={hasLaunchRequest2} request={DescribeLaunchRequest(launchRequest)}");
        return hasLaunchRequest2;
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

    private static string DescribeLaunchRequest(LaunchRequest launchRequest) {
        if (launchRequest is ProgramLaunchRequest programLaunchRequest) {
            return $"Program('{programLaunchRequest.ProgramName}', tail='{programLaunchRequest.CommandTail}', in='{programLaunchRequest.Redirection.InputPath}', out='{programLaunchRequest.Redirection.OutputPath}', err='{programLaunchRequest.Redirection.ErrorPath}')";
        }

        if (launchRequest is InternalProgramLaunchRequest internalProgramLaunchRequest) {
            return $"Internal({internalProgramLaunchRequest.ComProgramBytes.Length} bytes, in='{internalProgramLaunchRequest.Redirection.InputPath}', out='{internalProgramLaunchRequest.Redirection.OutputPath}', err='{internalProgramLaunchRequest.Redirection.ErrorPath}')";
        }

        return launchRequest.GetType().Name;
    }

    private static void TraceShell(string message) {
        Debug.WriteLine(message);
        Console.WriteLine(message);
    }
}