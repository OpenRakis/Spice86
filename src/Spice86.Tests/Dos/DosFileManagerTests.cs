namespace Spice86.Tests.Dos;

using Spice86.Core.Emulator.OperatingSystem;

using Xunit;

using Moq;
using FluentAssertions;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.CLI;
using Spice86.Shared.Interfaces;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Structures;

public class DosFileManagerTests {
    private static readonly string MountPoint = Path.GetFullPath(@"Resources\MountPoint");

    [Theory]
    [InlineData(@"\FoO", "FOO")]
    [InlineData(@"/FOo", "FOO")]
    [InlineData(@"/fOO/", "FOO")]
    [InlineData(@"/Foo\", "FOO")]
    [InlineData(@"\FoO\", "FOO")]
    [InlineData(@"C:\FoO\BAR\", @"FOO\BAR")]
    [InlineData(@"C:\", "")]
    public void AbsolutePaths(string dosPath, string expected) {
        // Arrange
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);

        // Act
        dosFileManager.SetCurrentDir(dosPath);

        // Assert
        DosFileOperationResult result = dosFileManager.GetCurrentDir(0x0, out string currentDir);
        result.Should().BeEquivalentTo(DosFileOperationResult.NoValue());
        currentDir.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void CanOpenFileBeginningWithC() {
        // Arrange
        DosFileManager dosFileManager = ArrangeDosFileManager(@$"{MountPoint}\foo\bar");

        // Act
        var result = dosFileManager.OpenFile("C.txt", 1);

        // Assert
        result.Should().BeEquivalentTo(DosFileOperationResult.Value16(0));
        dosFileManager.OpenFiles.ElementAtOrDefault(0)?.Name.Should().Be("C.txt");
    }

    [Theory]
    [InlineData(@"foo", "FOO")]
    [InlineData(@"foo/", "FOO")]
    [InlineData(@"foo\", "FOO")]
    [InlineData(@".\FOO", "FOO")]
    [InlineData(@"C:FOO", "FOO")]
    [InlineData(@"C:FOO\", "FOO")]
    [InlineData(@"C:FOO/", "FOO")]
    [InlineData(@"C:foo\bar", @"FOO\BAR")]
    [InlineData(@"../foo/BAR", @"FOO\BAR")]
    [InlineData(@"..\foo\BAR", @"FOO\BAR")]
    [InlineData(@"./FOO/BAR", @"FOO\BAR")]
    public void RelativePaths(string dosPath, string expected) {
        // Arrange
        DosFileManager dosFileManager = ArrangeDosFileManager(MountPoint);

        // Act
        dosFileManager.SetCurrentDir(dosPath);

        // Assert
        DosFileOperationResult result = dosFileManager.GetCurrentDir(0x0, out string currentDir);
        result.Should().BeEquivalentTo(DosFileOperationResult.NoValue());
        currentDir.Should().BeEquivalentTo(expected);
    }

    private static DosFileManager ArrangeDosFileManager(string mountPoint) {
        Configuration configuration = new Configuration() {
            DumpDataOnExit = false,
            CDrive = mountPoint
        };
        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Mock<ILoggerService> loggerServiceMock = new Mock<ILoggerService>();
        Mock<IVirtualDevice> chracterDeviceMock = new Mock<IVirtualDevice>();
        List<IVirtualDevice> dosDevicesMock = new List<IVirtualDevice>() { chracterDeviceMock.Object };
        return new DosFileManager(new Memory(ram, configuration.A20Gate), configuration.CDrive, configuration.Exe, loggerServiceMock.Object, dosDevicesMock);
    }
}
