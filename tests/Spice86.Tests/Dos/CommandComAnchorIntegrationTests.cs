namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using Xunit;

using static BatchTestHelpers;

public sealed class CommandComAnchorIntegrationTests {
    [Fact]
    public void ShellBootstrap_UsesZDriveCommandComAnchor() {
        WithTempFile("shell_command_anchor", tempDir => {
            Configuration configuration = CreateShellConfiguration(tempDir, string.Empty);
            using Spice86DependencyInjection spice86 = new(configuration);

            bool hasZDrive = spice86.Machine.Dos.DosDriveManager.TryGetMemoryDrive('Z', out MemoryDrive? zDrive);

            hasZDrive.Should().BeTrue();
            if (!hasZDrive || zDrive == null) {
                return;
            }

            zDrive.FileExists("COMMAND.COM").Should().BeTrue();
            spice86.Machine.Dos.ProcessManager.GetEnvironmentVariable("COMSPEC").Should().Be("Z:\\COMMAND.COM");
            spice86.Machine.Dos.ProcessManager.GetEnvironmentVariable("PATH").Should().Be("Z:\\");
        });
    }
}