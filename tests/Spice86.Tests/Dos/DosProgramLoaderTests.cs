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

    /// <summary>
    /// A program outside the C: drive folder has no DOS path. Resolving its file name on C: instead would
    /// start whichever program of that name the C: folder holds, not the one the user selected.
    /// This is an initialization-only test, not an execution test.
    /// </summary>
    [Fact]
    public void ProgramOutsideOfCDriveIsNotReplacedByAProgramOfTheSameNameOnC() {
        string cDrive = Path.GetFullPath($"c_drive_{Guid.NewGuid():N}");
        string otherFolder = Path.GetFullPath($"outside_c_drive_{Guid.NewGuid():N}");
        Directory.CreateDirectory(cDrive);
        Directory.CreateDirectory(otherFolder);
        try {
            // Arrange
            File.WriteAllBytes(Path.Join(cDrive, "HALT.COM"), [HltOpcode]);
            string programPath = Path.Join(otherFolder, "HALT.COM");
            File.WriteAllBytes(programPath, [HltOpcode]);

            // Act
            using Spice86Creator creator = new(
                binName: programPath,
                enablePit: false,
                maxCycles: 1000L,
                installInterruptVectors: true,
                cDrive: cDrive);
            using Spice86DependencyInjection spice86 = creator.Create();

            // Assert
            spice86.Machine.CpuState.IsRunning.Should().BeFalse("the selected program is not on the C: drive");
        } finally {
            Directory.Delete(cDrive, true);
            Directory.Delete(otherFolder, true);
        }
    }
}
