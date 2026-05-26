namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86;
using Spice86.Core.Emulator.OperatingSystem.Batch;

using System.IO;

using Xunit;

using static BatchTestHelpers;

public sealed class CommandShellIntegrationTests {
    private static readonly ushort[] ExitCommandKeys = [0x1265, 0x2D78, 0x1769, 0x1474, 0x1C0D];
    private static readonly ushort[] TypedProgramThenExitKeys = [0x1177, 0x1C0D, 0x1265, 0x2D78, 0x1769, 0x1474, 0x1C0D];
    private static readonly ushort[] TypedProgramWithRedirectionThenExitKeys = [0x1177, 0x343E, 0x2D78, 0x1C0D, 0x1265, 0x2D78, 0x1769, 0x1474, 0x1C0D];

    [Fact]
    public void StartWithoutExecutable_ShowsPromptAndProcessesExit() {
        WithTempFile("shell_no_exe", tempDir => {
            // Arrange
            (char[] prompt, long cycles) = RunShellSessionAndCaptureVideoCellsAndCycles(tempDir, string.Empty, 4,
                ExitCommandKeys);

            // Assert
            prompt.Should().Equal(['C', ':', '\\', '>']);
            cycles.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void StartWithExecutable_ReturnsToPromptAfterProgramExit() {
        WithTempFile("shell_target_exit", tempDir => {
            // Arrange
            string executablePath = CreateBinaryFile(tempDir, "EXITNOW.COM", BuildExitCodeCom(0));
            char[] prompt = RunShellSessionAndCaptureVideoCells(tempDir, executablePath, 4, ExitCommandKeys);

            // Assert
            prompt.Should().Equal(['C', ':', '\\', '>']);
        });
    }

    [Fact]
    public void InteractiveShell_TypedProgramLaunchesAndThenProcessesExit() {
        WithTempFile("shell_interactive_launch", tempDir => {
            string outputPath = Path.Join(tempDir, "X");
            CreateBinaryFile(tempDir, "W.COM", BuildStdoutWriterCom("W"));

            RunShellSessionAndCaptureVideoCells(tempDir, string.Empty, 1, TypedProgramWithRedirectionThenExitKeys);

            File.ReadAllText(outputPath).Should().Be("W");
        });
    }

    [Fact]
    public void InteractiveShell_TypedProgramExecutesWithoutRedirection() {
        WithTempFile("shell_interactive_video_probe", tempDir => {
            CreateBinaryFile(tempDir, "W.COM", BuildVideoWriterCom('W', 160));

            char[] cells = RunShellSessionAndCaptureVideoCells(tempDir, string.Empty, 81, TypedProgramThenExitKeys);

            cells[80].Should().Be('W');
        });
    }

    [Fact]
    public void BatchEngine_BareCommandResolvesFromCurrentDirectoryAtDriveRoot() {
        WithTempFile("shell_command_resolution", tempDir => {
            CreateBinaryFile(tempDir, "W.COM", BuildStdoutWriterCom("W"));

            using Spice86DependencyInjection spice86 = new(CreateShellConfiguration(tempDir, string.Empty));
            spice86.Machine.Dos.ProcessManager.CreateRootCommandComPsp();

            bool launched = spice86.Machine.Dos.ProcessManager.BatchExecutionEngine
                .TryExecuteCommandLine("w", out LaunchRequest launchRequest);

            launched.Should().BeTrue();
            launchRequest.Should().BeOfType<ProgramLaunchRequest>();
            ProgramLaunchRequest programLaunchRequest = (ProgramLaunchRequest)launchRequest;
            programLaunchRequest.ProgramName.Should().Be("C:\\w.COM");
        });
    }

    [Fact]
    public void BatchEngine_StartSession_ReturnsLaunchRequestForTypedProgram() {
        WithTempFile("shell_start_session", tempDir => {
            CreateBinaryFile(tempDir, "W.COM", BuildStdoutWriterCom("W"));

            using Spice86DependencyInjection spice86 = new(CreateShellConfiguration(tempDir, string.Empty));
            spice86.Machine.Dos.ProcessManager.CreateRootCommandComPsp();

            bool queued = spice86.Machine.BiosKeyboardInt9Handler.BiosKeyboardBuffer.EnqueueKeyCode(0x1177);
            queued.Should().BeTrue();
            queued = spice86.Machine.BiosKeyboardInt9Handler.BiosKeyboardBuffer.EnqueueKeyCode(0x343E);
            queued.Should().BeTrue();
            queued = spice86.Machine.BiosKeyboardInt9Handler.BiosKeyboardBuffer.EnqueueKeyCode(0x2D78);
            queued.Should().BeTrue();
            queued = spice86.Machine.BiosKeyboardInt9Handler.BiosKeyboardBuffer.EnqueueKeyCode(0x1C0D);
            queued.Should().BeTrue();

            bool launched = spice86.Machine.Dos.ProcessManager.BatchExecutionEngine.StartSession(out LaunchRequest launchRequest);

            launched.Should().BeTrue();
            launchRequest.Should().BeOfType<ProgramLaunchRequest>();
            ProgramLaunchRequest programLaunchRequest = (ProgramLaunchRequest)launchRequest;
            programLaunchRequest.ProgramName.Should().Be("C:\\w.COM");
            programLaunchRequest.Redirection.OutputPath.Should().Be("x");
        });
    }

}