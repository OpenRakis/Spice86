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
    public void FindFirstMatchingFile_VolumeLabel_CdRomDrive_UsesMountedMediaLabel() {
        // Arrange
        using TempFile tempFile = new("Spice86_FM_Tests");
        string cdRoot = Path.Join(tempFile.Path, "MYDISC");
        Directory.CreateDirectory(cdRoot);
        using DosTestFixture fixture = new(tempFile.Path);
        fixture.Dos.MountFolderAsCdRom('D', cdRoot).Should().BeTrue();

        // Act: volume-label searches on a mounted CD drive should use the mounted media label.
        DosFileOperationResult result = fixture.DosFileManager.FindFirstMatchingFile(
            @"D:\*.*", (ushort)DosFileAttributes.VolumeId);

        // Assert
        result.IsError.Should().BeFalse();
        DosDiskTransferArea dta = fixture.DosFileManager.DiskTransferArea;
        dta.FileAttributes.Should().Be((byte)DosFileAttributes.VolumeId);
        dta.FileName.Should().Be("MYDISC",
            "DOS-visible CD volume label queries should reflect the mounted CD media label");
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

    [Fact]
    public void OpenFile_TrailingDot_OpensExtensionlessFile() {
        // Regression: The Summoning calls INT 21h AH=3D with "V." to open a host file
        // literally named "V" (no extension). DOS semantics: "FILE." == "FILE" with empty
        // extension. FreeDOS truename strips a single trailing dot.

        // Arrange
        string mountPoint = CreateTempMountWithExtensionlessFile("V");
        try {
            using DosTestFixture fixture = new(mountPoint);

            // Act
            DosFileOperationResult result = fixture.DosFileManager.OpenFileOrDevice("V.", FileAccessMode.ReadOnly);

            // Assert
            result.IsError.Should().BeFalse(
                "a DOS trailing-dot filename must resolve to the same on-disk file as the bare name");
            CloseIfOpen(fixture, result);
        } finally {
            Directory.Delete(mountPoint, recursive: true);
        }
    }

    [Fact]
    public void OpenFile_BareNameMatchesExtensionlessFile() {
        // Regression guard for the reverse direction: opening "V" must continue to work
        // when the host file is literally "V" (no extension).

        // Arrange
        string mountPoint = CreateTempMountWithExtensionlessFile("V");
        try {
            using DosTestFixture fixture = new(mountPoint);

            // Act
            DosFileOperationResult result = fixture.DosFileManager.OpenFileOrDevice("V", FileAccessMode.ReadOnly);

            // Assert
            result.IsError.Should().BeFalse("a bare DOS name must open an extension-less host file");
            CloseIfOpen(fixture, result);
        } finally {
            Directory.Delete(mountPoint, recursive: true);
        }
    }

    [Fact]
    public void OpenFile_TrailingSpaces_OpensSpacePaddedFile() {
        // Regression: Alone in the Dark's TATOU.COM (launched by GO.BAT) passes an FCB-style
        // space-padded ASCIIZ buffer ("Info.cc1            ") to INT 21h AH=3D. DOS semantics:
        // trailing whitespace on a file name is silently tolerated (FreeDOS 8.3 entries are
        // space-padded so trailing spaces match the padding). The canonicalized name must
        // resolve to the same host file as the bare name.

        // Arrange
        string mountPoint = CreateTempMountWithExtensionlessFile("INFO.CC1");
        try {
            using DosTestFixture fixture = new(mountPoint);

            // Act
            DosFileOperationResult result = fixture.DosFileManager.OpenFileOrDevice("Info.cc1            ", FileAccessMode.ReadOnly);

            // Assert
            result.IsError.Should().BeFalse(
                "a DOS filename with trailing spaces must resolve to the same on-disk file as the unpadded name");
            CloseIfOpen(fixture, result);
        } finally {
            Directory.Delete(mountPoint, recursive: true);
        }
    }

    [Fact]
    public void OpenFile_DoubleTrailingDotIsRejected() {
        // FreeDOS rejects multiple trailing dots (PNE_DOT). Ensure we surface a DOS error
        // rather than silently matching or crashing.

        // Arrange
        string mountPoint = CreateTempMountWithExtensionlessFile("V");
        try {
            using DosTestFixture fixture = new(mountPoint);

            // Act
            DosFileOperationResult result = fixture.DosFileManager.OpenFileOrDevice("V..", FileAccessMode.ReadOnly);

            // Assert
            result.IsError.Should().BeTrue("multiple trailing dots are ill-formed in DOS file names");
        } finally {
            Directory.Delete(mountPoint, recursive: true);
        }
    }

    private static string CreateTempMountWithExtensionlessFile(string fileName) {
        string mountPoint = Path.Join(Path.GetTempPath(), $"Spice86_FM_Tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(mountPoint);
        File.WriteAllText(Path.Join(mountPoint, fileName), "payload");
        return mountPoint;
    }

    private static void CloseIfOpen(DosTestFixture fixture, DosFileOperationResult result) {
        if (result.Value is uint handle) {
            fixture.DosFileManager.CloseFileOrDevice((ushort)handle);
        }
    }
}