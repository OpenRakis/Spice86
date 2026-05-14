namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Tests.Utility;

using Xunit;

public class DosFileManagerTests {
    private static readonly string MountPoint = Path.Join(AppContext.BaseDirectory, "Resources", "MountPoint");

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
        using DosTestFixture fixture = new(MountPoint);

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
        using DosTestFixture fixture = new(@$"{MountPoint}\foo\bar");

        // Act
        DosFileOperationResult result = fixture.DosFileManager.OpenFileOrDevice("C.txt", FileAccessMode.ReadOnly);

        // Assert
        result.Should().BeEquivalentTo(DosFileOperationResult.Value16(6));
        fixture.DosFileManager.OpenFiles.Last()?.Name.Should().Be("C.txt");
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
        using DosTestFixture fixture = new(MountPoint);

        // Act
        fixture.DosFileManager.SetCurrentDir(dosPath);

        // Assert
        DosFileOperationResult result = fixture.DosFileManager.GetCurrentDir(0x0, out string currentDir);
        result.Should().BeEquivalentTo(DosFileOperationResult.NoValue());
        currentDir.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void OpenFile_ComputesDeviceInfoAndSupportsRelativeSeek() {
        using DosTestFixture fixture = new(MountPoint);
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

    [Fact]
    public void FindFirstMatchingFile_ExtendedFormat_WritesAsciizFilenameAtOffset0x1E() {
        // Arrange
        using TempFile tempFile = new("Spice86_FM_Tests");
        File.WriteAllText(Path.Join(tempFile.Path, "HELLO.TXT"), "content");
        using DosTestFixture fixture = new(tempFile.Path);

        // Act: Extended find (isFcbSearch: false is default)
        DosFileOperationResult result = fixture.DosFileManager.FindFirstMatchingFile("HELLO.TXT", 0);

        // Assert
        result.IsError.Should().BeFalse();
        DosDiskTransferArea dta = fixture.DosFileManager.DiskTransferArea;
        dta.FileName.Should().Be("HELLO.TXT", "Extended FindFirst should write ASCIIZ 8.3 filename at offset 0x1E");
        dta.FileSize.Should().Be(7);

        // Exhaust the search and ensure it is cleaned up.
        DosFileOperationResult nextResult = fixture.DosFileManager.FindNextMatchingFile();
        nextResult.IsError.Should().BeTrue();
        dta.SearchId.Should().Be(0u, "FindNext exhaustion should clear active search state");
    }

    [Fact]
    public void FcbFindFirstMatchingFile_WritesSpacePaddedNameAtOffset0x01() {
        // Arrange
        using TempFile tempFile = new("Spice86_FM_Tests");
        File.WriteAllText(Path.Join(tempFile.Path, "HELLO.TXT"), "content");
        using DosTestFixture fixture = new(tempFile.Path);

        // Act
        DosFileOperationResult result = fixture.DosFileManager.FcbFindFirstMatchingFile("HELLO.TXT", 0);

        // Assert
        result.IsError.Should().BeFalse();
        uint dtaAddr = fixture.DosFileManager.GetDiskTransferAreaPhysicalAddress();
        DosFileControlBlock dtaFcb = new DosFileControlBlock(fixture.Memory, dtaAddr);
        dtaFcb.FileName.Should().Be("HELLO   ", "FCB FindFirst should write 8-char space-padded name at offset 0x01");
        dtaFcb.FileExtension.Should().Be("TXT", "FCB FindFirst should write 3-char space-padded extension at offset 0x09");
        dtaFcb.FileSize.Should().Be(7);
    }

    [Fact]
    public void FindFirstMatchingFile_VolumeLabel_ExtendedFormat_Returns83FormattedLabel() {
        // Arrange
        using TempFile tempFile = new("Spice86_FM_Tests");
        using DosTestFixture fixture = new(tempFile.Path);
        fixture.Dos.DosDriveManager.CurrentDrive.Label = "MYVOLUMEID";

        // Act: Extended search for volume label
        DosFileOperationResult result = fixture.DosFileManager.FindFirstMatchingFile(
            @"C:\*.*", (ushort)DosFileAttributes.VolumeId);

        // Assert
        result.IsError.Should().BeFalse();
        DosDiskTransferArea dta = fixture.DosFileManager.DiskTransferArea;
        dta.FileAttributes.Should().Be((byte)DosFileAttributes.VolumeId);
        // PackName formats "MYVOLUMEID " as "MYVOLUME.ID" (8.3 with dot)
        dta.FileName.Should().Be("MYVOLUME.ID",
            "Extended FindFirst should format volume label in 8.3 with dot via PackName");
    }

    [Fact]
    public void FindFirstMatchingFile_VolumeLabel_ShortLabel_NoDot() {
        // Arrange
        using TempFile tempFile = new("Spice86_FM_Tests");
        using DosTestFixture fixture = new(tempFile.Path);
        fixture.Dos.DosDriveManager.CurrentDrive.Label = "MYLABEL";

        // Act: Extended search for volume label
        DosFileOperationResult result = fixture.DosFileManager.FindFirstMatchingFile(
            @"C:\*.*", (ushort)DosFileAttributes.VolumeId);

        // Assert
        result.IsError.Should().BeFalse();
        DosDiskTransferArea dta = fixture.DosFileManager.DiskTransferArea;
        // "MYLABEL" is 7 chars, fits in 8-byte name portion, extension is all spaces
        // PackName: no dot since extension is all spaces
        dta.FileName.Should().Be("MYLABEL",
            "Short volume labels should have no dot in 8.3 format");
    }

    [Fact]
    public void FindNextMatchingFile_VolumeLabel_ReturnsNoMoreFiles() {
        // Arrange: Volume label searches return exactly one result (the drive label).
        // FindNext should immediately return "no more files".
        using TempFile tempFile = new("Spice86_FM_Tests");
        using DosTestFixture fixture = new(tempFile.Path);

        // First call to establish the search
        DosFileOperationResult firstResult = fixture.DosFileManager.FindFirstMatchingFile(
            @"C:\*.*", (ushort)DosFileAttributes.VolumeId);
        firstResult.IsError.Should().BeFalse();

        // Act
        DosFileOperationResult nextResult = fixture.DosFileManager.FindNextMatchingFile();

        // Assert
        nextResult.IsError.Should().BeTrue("Volume label search should have only one result");
    }
}