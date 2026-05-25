namespace Spice86.Tests.Dos;

using FluentAssertions;

using System.IO;

using Xunit;

using static BatchTestHelpers;

public sealed class CommandShellIntegrationTests {
    private static readonly ushort[] ExitCommandKeys = [0x1265, 0x2D78, 0x1769, 0x1474, 0x1C0D];

    [Fact]
    public void StartWithoutExecutable_ShowsPromptAndProcessesExit() {
        WithTempFile("shell_no_exe", tempDir => {
            // Arrange
            (char[] prompt, long cycles) = RunShellSessionAndCaptureVideoCellsAndCycles(tempDir, string.Empty, 4,
                ExitCommandKeys);

            // Assert
            prompt.Should().Equal(['C', ':', '\\', '>']);
            cycles.Should().BeGreaterThan(32);
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
}