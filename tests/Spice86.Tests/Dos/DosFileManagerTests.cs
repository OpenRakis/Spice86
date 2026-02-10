namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;

using Xunit;

public class DosFileManagerTests {
    private static readonly string MountPoint = Path.GetFullPath(Path.Combine("Resources", "MountPoint"));

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
        DosTestFixture fixture = new(MountPoint);

        // Act
        fixture.DosFileManager.SetCurrentDir(dosPath);

        // Assert
        DosFileOperationResult result = fixture.DosFileManager.GetCurrentDir(0x0, out string currentDir);
        result.Should().BeEquivalentTo(DosFileOperationResult.NoValue());
        currentDir.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void CanOpenFileBeginningWithC() {
        // Arrange
        DosTestFixture fixture = new(@$"{MountPoint}\foo\bar");

        // Act
        DosFileOperationResult result = fixture.DosFileManager.OpenFileOrDevice("C.txt", FileAccessMode.ReadOnly);

        // Assert
        result.Should().BeEquivalentTo(DosFileOperationResult.Value16(3));
        fixture.DosFileManager.OpenFiles.ElementAtOrDefault(3)?.Name.Should().Be("C.txt");
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
        DosTestFixture fixture = new(MountPoint);

        // Act
        fixture.DosFileManager.SetCurrentDir(dosPath);

        // Assert
        DosFileOperationResult result = fixture.DosFileManager.GetCurrentDir(0x0, out string currentDir);
        result.Should().BeEquivalentTo(DosFileOperationResult.NoValue());
        currentDir.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void OpenFile_ComputesDeviceInfoAndSupportsRelativeSeek() {
        DosTestFixture fixture = new(MountPoint);
        ushort? handle = null;

        const string fileName = "seektest.bin";

        try {
            DosFileOperationResult openResult = fixture.DosFileManager.OpenFileOrDevice(fileName, FileAccessMode.ReadOnly);
            openResult.IsError.Should().BeFalse();
            openResult.Value.Should().NotBeNull();
            if (openResult.Value == null) {
                throw new InvalidOperationException("OpenFileOrDevice returned null");
            }
            handle = (ushort)openResult.Value.Value;

            VirtualFileBase? fileBase = fixture.DosFileManager.OpenFiles[handle.Value];
            fileBase.Should().BeOfType<DosFile>();
            if (fileBase is not DosFile dosFile) {
                throw new InvalidOperationException("Expected DosFile but got different type");
            }
            dosFile.DeviceInformation.Should().Be(0x0802);
            dosFile.CanSeek.Should().BeTrue();

            dosFile.Seek(0x200, SeekOrigin.Begin);
            dosFile.Position.Should().Be(0x200);

            DosFileOperationResult seekResult =
                fixture.DosFileManager.MoveFilePointerUsingHandle(SeekOrigin.Current, handle.Value, -0x1BA);
            seekResult.IsError.Should().BeFalse();
            seekResult.Value.Should().Be(0x46);
            dosFile.Position.Should().Be(0x46);
        } finally {
            if (handle is not null) {
                fixture.DosFileManager.CloseFileOrDevice(handle.Value);
            }
        }
    }
}


