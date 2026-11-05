namespace Spice86.Tests.Dos;

using FluentAssertions;

using Xunit;

/// <summary>
/// Loading the startup program from the host folder mounted as C:.
/// </summary>
public class DosProgramLoaderTests {
    private const byte HltOpcode = 0xF4;
    private const ushort ComProgramEntryOffset = 0x0100;

    /// <summary>
    /// The command line takes the C: drive folder as typed, often relative to the working directory, while
    /// the program path is made absolute, so the loader must not assume both paths are spelled the same way.
    /// This is an initialization-only test, not an execution test.
    /// </summary>
    [Fact]
    public void ProgramIsLoadedWhenCDriveIsRelativeToWorkingDirectory() {
        string relativeCDrive = $"relative_c_drive_{Guid.NewGuid():N}";
        Directory.CreateDirectory(relativeCDrive);
        try {
            // Arrange
            string programPath = Path.GetFullPath(Path.Join(relativeCDrive, "HALT.COM"));
            File.WriteAllBytes(programPath, [HltOpcode]);

            // Act
            using Spice86Creator creator = new(
                binName: programPath,
                enablePit: false,
                maxCycles: 1000L,
                installInterruptVectors: true,
                cDrive: relativeCDrive);
            using Spice86DependencyInjection spice86 = creator.Create();

            // Assert
            spice86.Machine.CpuState.IsRunning.Should().BeTrue("the program on the relative C: drive must be found and loaded");
            spice86.Machine.CpuState.IP.Should().Be(ComProgramEntryOffset, "a COM program starts at offset 0x100 of its PSP");
        } finally {
            Directory.Delete(relativeCDrive, true);
        }
    }
}
