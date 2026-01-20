namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using System.Text;

using Xunit;

public class DosInt21HandlerTests {
    [Fact]
    public void DosFileManager_MoveFilePointer_ShouldTreatOffsetAsSignedValue() {
        // Arrange
        IMemory memory = CreateMemory();
        CreateFcbManager(memory, out _, out DosFileManager dosFileManager);
        
        RecordingVirtualFile recordingFile = new();
        const ushort fileHandle = 0x0003;
        dosFileManager.OpenFiles[fileHandle] = recordingFile;

        const SeekOrigin seekOrigin = SeekOrigin.Current;
        const int offset = -1; // 0xFFFFFFFF when CXDX registers hold 0xFFFF:0xFFFF

        // Act
        dosFileManager.MoveFilePointerUsingHandle(seekOrigin, fileHandle, offset);

        // Assert
        recordingFile.LastSeekOffset.Should().Be(-1);
        recordingFile.LastSeekOrigin.Should().Be(SeekOrigin.Current);
        
        CloseAllOpenFiles(dosFileManager);
    }

    private sealed class RecordingVirtualFile : VirtualFileBase {
        private long _length;

        public long LastSeekOffset { get; private set; }

        public SeekOrigin LastSeekOrigin { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position { get; set; }

        public override void Flush() {
        }

        public override int Read(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin) {
            LastSeekOffset = offset;
            LastSeekOrigin = origin;
            return origin switch {
                SeekOrigin.Begin => Position = offset,
                SeekOrigin.Current => Position += offset,
                SeekOrigin.End => Position = _length + offset,
                _ => Position
            };
        }

        public override void SetLength(long value) {
            _length = value;
        }

        public override void Write(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }
    }

    [Fact]
    public void FcbParseFilename_ShouldParseSimpleFilenameWithoutDrive() {
        // Arrange
        IMemory memory = CreateMemory();
        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        const string filename = "TEST.TXT";
        
        WriteString(memory, stringAddress, filename);
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager);
        driveManager.CurrentDrive = driveManager['C']; // C: drive

        // Act
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, 0x00, out uint bytesAdvanced);

        // Assert
        result.Should().Be(DosFcbManager.FcbSuccess, "parsing should succeed without wildcards");
        bytesAdvanced.Should().Be((uint)filename.Length, "should advance past the entire filename");
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber.Should().Be(0, "no drive specified, should use default");
        fcb.FileName.Should().Be("TEST    ", "filename should be space-padded to 8 chars");
        fcb.FileExtension.Should().Be("TXT", "extension should be TXT");
        
        CloseAllOpenFiles(dosFileManager);
    }

    [Fact]
    public void FcbParseFilename_ShouldParseFilenameWithDrive() {
        // Arrange
        IMemory memory = CreateMemory();
        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        const string filename = "A:FILE.DAT";
        
        WriteString(memory, stringAddress, filename);
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out _, out DosFileManager dosFileManager);

        // Act
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, 0x02, out uint bytesAdvanced);

        // Assert
        result.Should().Be(DosFcbManager.FcbSuccess);
        bytesAdvanced.Should().Be((uint)filename.Length);
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber.Should().Be(1, "drive A: should be set to 1");
        fcb.FileName.Should().Be("FILE    ");
        fcb.FileExtension.Should().Be("DAT");
        

        CloseAllOpenFiles(dosFileManager);
    }

    [Fact]
    public void FcbParseFilename_ShouldHandleLongExtension() {
        // Arrange
        IMemory memory = CreateMemory();
        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        const string filename = "FILE.LONGEXT";
        
        WriteString(memory, stringAddress, filename);
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out _, out DosFileManager dosFileManager);

        // Act
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, 0x00, out uint bytesAdvanced);

        // Assert
        result.Should().Be(DosFcbManager.FcbSuccess);
        bytesAdvanced.Should().Be((uint)filename.Length, "bytesAdvanced should advance past the entire extension");
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.FileName.Should().Be("FILE    ");
        fcb.FileExtension.Should().Be("LON", "extension should be truncated to 3 characters");
        
        CloseAllOpenFiles(dosFileManager);
    }

    [Fact]
    public void FcbParseFilename_ShouldDetectWildcards() {
        // Arrange
        IMemory memory = CreateMemory();
        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        const string filename = "TEST*.TXT";
        
        WriteString(memory, stringAddress, filename);
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out _, out DosFileManager dosFileManager);

        // Act
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, 0x00, out _);

        // Assert
        result.Should().Be(0x01, "should return 0x01 when wildcards are present");
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.FileName.Should().Be("TEST????", "* should be expanded to ???? wildcards");
        
        CloseAllOpenFiles(dosFileManager);
    }

    [Fact]
    public void FcbParseFilename_ShouldHandleInvalidDrive() {
        // Arrange
        IMemory memory = CreateMemory();
        const uint stringAddress = 0x1000;
        const uint fcbAddress = 0x2000;
        const string filename = "Z:TEST.TXT";
        
        WriteString(memory, stringAddress, filename);
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager);
        // Z: drive doesn't exist in default setup

        // Act
        byte result = fcbManager.ParseFilename(stringAddress, fcbAddress, 0x00, out _);

        // Assert
        result.Should().Be(DosFcbManager.FcbError, "should return error for invalid drive");
        
        CloseAllOpenFiles(dosFileManager);
    }

    [Fact]
    public void FcbOpenFile_ShouldOpenExistingFile() {
        // Arrange
        string tempDir = CreateTempDirectory();
        string testFile = Path.Join(tempDir, "TEST.TXT");
        File.WriteAllText(testFile, "Hello FCB");
        
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        
        // Setup FCB with filename
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0; // Use current drive
        fcb.FileName = "TEST    ";
        fcb.FileExtension = "TXT";
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Act
            byte result = fcbManager.OpenFile(fcbAddress);

            // Assert
            result.Should().Be(DosFcbManager.FcbSuccess, "file should open successfully");
            fcb.FileSize.Should().BeGreaterThan(0, "file size should be set");
            fcb.RecordSize.Should().Be(DosFileControlBlock.DefaultRecordSize, "should use default record size");
            fcb.CurrentBlock.Should().Be(0, "should start at block 0");
            fcb.CurrentRecord.Should().Be(0, "should start at record 0");
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbOpenFile_ShouldReturnErrorForNonexistentFile() {
        // Arrange
        string tempDir = CreateTempDirectory();
        
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "NOFILE  ";
        fcb.FileExtension = "TXT";
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Act
            byte result = fcbManager.OpenFile(fcbAddress);

            // Assert
            result.Should().Be(DosFcbManager.FcbError, "should return error when file doesn't exist");
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbFindFirst_ShouldFindMatchingFile() {
        // Arrange
        string tempDir = CreateTempDirectory();
        string testFile = Path.Join(tempDir, "TEST.TXT");
        File.WriteAllText(testFile, "Test content");
        
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        const uint dtaAddress = 0x3000;
        
        // Setup FCB with search pattern
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "TEST    ";
        fcb.FileExtension = "TXT";
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Act
            byte result = fcbManager.FindFirst(fcbAddress, dtaAddress);

            // Assert
            result.Should().Be(DosFcbManager.FcbSuccess, "should find the matching file");
            
            // Verify DTA is filled with file information
            DosFileControlBlock dtaFcb = new(memory, dtaAddress);
            dtaFcb.FileName.Trim().Should().Be("TEST", "DTA should contain the found filename");
            dtaFcb.FileExtension.Trim().Should().Be("TXT", "DTA should contain the found extension");
            dtaFcb.FileSize.Should().BeGreaterThan(0, "DTA should contain the file size");
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbFindFirst_ShouldReturnErrorWhenNoMatch() {
        // Arrange
        string tempDir = CreateTempDirectory();
        
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        const uint dtaAddress = 0x3000;
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "NOFILE  ";
        fcb.FileExtension = "TXT";
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Act
            byte result = fcbManager.FindFirst(fcbAddress, dtaAddress);

            // Assert
            result.Should().Be(DosFcbManager.FcbError, "should return error when no files match");
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbFindFirst_ShouldFindWildcardMatches() {
        // Arrange
        string tempDir = CreateTempDirectory();
        File.WriteAllText(Path.Join(tempDir, "TEST1.TXT"), "1");
        File.WriteAllText(Path.Join(tempDir, "TEST2.TXT"), "2");
        File.WriteAllText(Path.Join(tempDir, "OTHER.DAT"), "3");
        
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        const uint dtaAddress = 0x3000;
        
        // Setup FCB with wildcard pattern
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "TEST????";
        fcb.FileExtension = "TXT";
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Act
            byte result = fcbManager.FindFirst(fcbAddress, dtaAddress);

            // Assert
            result.Should().Be(DosFcbManager.FcbSuccess, "should find at least one matching file");
            
            DosFileControlBlock dtaFcb = new(memory, dtaAddress);
            string foundName = dtaFcb.FileName.Trim();
            foundName.Should().StartWith("TEST", "found file should match wildcard pattern");
            dtaFcb.FileExtension.Trim().Should().Be("TXT");
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbFindNext_ShouldFindMultipleMatches() {
        // Arrange
        string tempDir = CreateTempDirectory();
        File.WriteAllText(Path.Join(tempDir, "FILE1.TXT"), "1");
        File.WriteAllText(Path.Join(tempDir, "FILE2.TXT"), "2");
        File.WriteAllText(Path.Join(tempDir, "FILE3.TXT"), "3");
        
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        const uint dtaAddress = 0x3000;
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "FILE????";
        fcb.FileExtension = "TXT";
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Act - Find first
            byte firstResult = fcbManager.FindFirst(fcbAddress, dtaAddress);
            firstResult.Should().Be(DosFcbManager.FcbSuccess);
            
            DosFileControlBlock firstDta = new(memory, dtaAddress);
            string firstName = firstDta.FileName.Trim();
            
            // Act - Find next
            byte secondResult = fcbManager.FindNext(fcbAddress, dtaAddress);
            
            // Assert
            secondResult.Should().Be(DosFcbManager.FcbSuccess, "should find second matching file");
            
            DosFileControlBlock secondDta = new(memory, dtaAddress);
            string secondName = secondDta.FileName.Trim();
            
            secondName.Should().NotBe(firstName, "second file should be different from first");
            secondName.Should().StartWith("FILE", "second file should also match pattern");
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbFindNext_ShouldReturnErrorWhenNoMoreMatches() {
        // Arrange
        string tempDir = CreateTempDirectory();
        File.WriteAllText(Path.Join(tempDir, "SINGLE.TXT"), "only one");
        
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        const uint dtaAddress = 0x3000;
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "SINGLE  ";
        fcb.FileExtension = "TXT";
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Act
            byte firstResult = fcbManager.FindFirst(fcbAddress, dtaAddress);
            firstResult.Should().Be(DosFcbManager.FcbSuccess);
            
            byte secondResult = fcbManager.FindNext(fcbAddress, dtaAddress);

            // Assert
            secondResult.Should().Be(DosFcbManager.FcbError, "should return error when no more files match");
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbFindFirst_WithExtendedFcb_ShouldRespectAttributeFilter() {
        // Arrange
        string tempDir = CreateTempDirectory();
        string normalFile = Path.Join(tempDir, "NORMAL.TXT");
        string hiddenFile = Path.Join(tempDir, "HIDDEN.TXT");
        
        File.WriteAllText(normalFile, "normal");
        File.WriteAllText(hiddenFile, "hidden");
        File.SetAttributes(hiddenFile, FileAttributes.Hidden);
        
        IMemory memory = CreateMemory();
        const uint xfcbAddress = 0x2000;
        const uint dtaAddress = 0x3000;
        
        // Setup Extended FCB with Hidden attribute filter
        DosExtendedFileControlBlock xfcb = new(memory, xfcbAddress);
        xfcb.Flag = DosExtendedFileControlBlock.ExtendedFcbFlag;
        xfcb.Attribute = (byte)DosFileAttributes.Hidden;
        
        DosFileControlBlock fcb = xfcb.Fcb;
        fcb.DriveNumber = 0;
        fcb.FileName = "????????";
        fcb.FileExtension = "TXT";
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Act
            byte result = fcbManager.FindFirst(xfcbAddress, dtaAddress);

            // Assert
            result.Should().Be(DosFcbManager.FcbSuccess, "should find files including hidden ones");
            
            // Verify DTA contains extended FCB header
            byte dtaFlag = memory.UInt8[dtaAddress];
            dtaFlag.Should().Be(DosExtendedFileControlBlock.ExtendedFcbFlag, "DTA should have extended FCB format");
        } finally {
            CloseAllOpenFiles(dosFileManager);
            File.SetAttributes(hiddenFile, FileAttributes.Normal);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbCreateFile_ShouldCreateNewFile() {
        // Arrange
        string tempDir = CreateTempDirectory();
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "NEWFILE ";
        fcb.FileExtension = "DAT";
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Act
            byte result = fcbManager.CreateFile(fcbAddress);

            // Assert
            result.Should().Be(DosFcbManager.FcbSuccess, "creation should succeed");
            File.Exists(Path.Join(tempDir, "NEWFILE.DAT")).Should().BeTrue("file should be created on disk");
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbDeleteFile_ShouldDeleteMatchingFiles() {
        // Arrange
        string tempDir = CreateTempDirectory();
        File.WriteAllText(Path.Join(tempDir, "DELETE.ME"), "test content");
        
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "DELETE  ";
        fcb.FileExtension = "ME ";
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Act
            byte result = fcbManager.DeleteFile(fcbAddress);

            // Assert
            result.Should().Be(DosFcbManager.FcbSuccess, "deletion should succeed");
            File.Exists(Path.Join(tempDir, "DELETE.ME")).Should().BeFalse("file should be deleted");
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbDeleteFile_ShouldReturnErrorForDeviceName() {
        // Arrange
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "CON     ";
        fcb.FileExtension = "   ";
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out _, out DosFileManager dosFileManager);

        try {
            // Act
            byte result = fcbManager.DeleteFile(fcbAddress);

            // Assert
            result.Should().Be(DosFcbManager.FcbError, "cannot delete character device");
        } finally {
            CloseAllOpenFiles(dosFileManager);
        }
    }

    [Fact]
    public void FcbRenameFile_ShouldRenameFile() {
        // Arrange
        string tempDir = CreateTempDirectory();
        File.WriteAllText(Path.Join(tempDir, "OLD.TXT"), "test content");
        
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "OLD     ";
        fcb.FileExtension = "TXT";
        
        // Write new name at offset 0x11 (17 bytes into FCB)
        const uint newNameOffset = fcbAddress + 0x11;
        WriteString(memory, newNameOffset, "NEW     TXT");
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Act
            byte result = fcbManager.RenameFile(fcbAddress);

            // Assert
            result.Should().Be(DosFcbManager.FcbSuccess, "rename should succeed");
            File.Exists(Path.Join(tempDir, "OLD.TXT")).Should().BeFalse("old file should not exist");
            File.Exists(Path.Join(tempDir, "NEW.TXT")).Should().BeTrue("new file should exist");
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbSequentialReadWrite_ShouldReadAndWriteRecords() {
        // Arrange
        string tempDir = CreateTempDirectory();
        string testFile = Path.Join(tempDir, "SEQTEST.DAT");
        
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        const uint dtaAddress = 0x3000;
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "SEQTEST ";
        fcb.FileExtension = "DAT";
        fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Create file and write data
            byte createResult = fcbManager.CreateFile(fcbAddress);
            createResult.Should().Be(DosFcbManager.FcbSuccess);
            
            // Write test data to DTA
            for (int i = 0; i < 128; i++) {
                memory.UInt8[dtaAddress + (uint)i] = (byte)(i % 256);
            }
            
            // Write one record
            byte writeResult = fcbManager.SequentialWrite(fcbAddress, dtaAddress);
            writeResult.Should().Be(DosFcbManager.FcbSuccess, "write should succeed");
            
            // Close and reopen
            fcbManager.CloseFile(fcbAddress);
            byte openResult = fcbManager.OpenFile(fcbAddress);
            openResult.Should().Be(DosFcbManager.FcbSuccess);
            
            // Clear DTA
            for (int i = 0; i < 128; i++) {
                memory.UInt8[dtaAddress + (uint)i] = 0;
            }
            
            // Read record back
            byte readResult = fcbManager.SequentialRead(fcbAddress, dtaAddress);
            readResult.Should().Be(DosFcbManager.FcbSuccess, "read should succeed");
            
            // Verify data
            for (int i = 0; i < 128; i++) {
                memory.UInt8[dtaAddress + (uint)i].Should().Be((byte)(i % 256), $"byte at offset {i} should match");
            }
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbRandomReadWrite_ShouldReadAndWriteAtSpecificRecord() {
        // Arrange
        string tempDir = CreateTempDirectory();
        
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        const uint dtaAddress = 0x3000;
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "RANDOM  ";
        fcb.FileExtension = "DAT";
        fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Create file
            byte createResult = fcbManager.CreateFile(fcbAddress);
            createResult.Should().Be(DosFcbManager.FcbSuccess);
            
            // Write at record 5
            fcb.RandomRecord = 5;
            for (int i = 0; i < 128; i++) {
                memory.UInt8[dtaAddress + (uint)i] = (byte)0xAB;
            }
            
            byte writeResult = fcbManager.RandomWrite(fcbAddress, dtaAddress);
            writeResult.Should().Be(DosFcbManager.FcbSuccess, "random write should succeed");
            
            // Close and reopen
            fcbManager.CloseFile(fcbAddress);
            fcbManager.OpenFile(fcbAddress);
            
            // Clear DTA and read back record 5
            for (int i = 0; i < 128; i++) {
                memory.UInt8[dtaAddress + (uint)i] = 0;
            }
            
            fcb.RandomRecord = 5;
            byte readResult = fcbManager.RandomRead(fcbAddress, dtaAddress);
            readResult.Should().Be(DosFcbManager.FcbSuccess, "random read should succeed");
            
            // Verify data
            for (int i = 0; i < 128; i++) {
                memory.UInt8[dtaAddress + (uint)i].Should().Be(0xAB, $"byte at offset {i} should be 0xAB");
            }
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbGetFileSize_ShouldCalculateRecordCount() {
        // Arrange
        string tempDir = CreateTempDirectory();
        string testFile = Path.Join(tempDir, "SIZED.TXT");
        File.WriteAllBytes(testFile, new byte[256]); // 256 bytes = 2 records
        
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "SIZED   ";
        fcb.FileExtension = "TXT";
        fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Act
            byte result = fcbManager.GetFileSize(fcbAddress);

            // Assert
            result.Should().Be(DosFcbManager.FcbSuccess, "get file size should succeed");
            fcb.RandomRecord.Should().Be(2, "256 bytes / 128 bytes per record = 2 records");
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void FcbRandomBlockWrite_WithZeroRecordCount_ShouldTruncateFile() {
        // Arrange
        string tempDir = CreateTempDirectory();
        
        IMemory memory = CreateMemory();
        const uint fcbAddress = 0x2000;
        const uint dtaAddress = 0x3000;
        
        DosFileControlBlock fcb = new(memory, fcbAddress);
        fcb.DriveNumber = 0;
        fcb.FileName = "TRUNC   ";
        fcb.FileExtension = "DAT";
        fcb.RecordSize = DosFileControlBlock.DefaultRecordSize;
        
        DosFcbManager fcbManager = CreateFcbManager(memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, tempDir);
        driveManager.CurrentDrive = driveManager['C'];

        try {
            // Create file and write 3 records
            fcbManager.CreateFile(fcbAddress);
            
            for (int i = 0; i < 128; i++) {
                memory.UInt8[dtaAddress + (uint)i] = (byte)0xFF;
            }
            
            for (int record = 0; record < 3; record++) {
                fcbManager.SequentialWrite(fcbAddress, dtaAddress);
            }
            
            // Truncate at record 1 (keep only first record) by setting random record and calling RandomBlockWrite with 0 count
            fcb.RandomRecord = 1;
            ushort recordCount = 0;
            byte truncateResult = fcbManager.RandomBlockWrite(fcbAddress, dtaAddress, ref recordCount);
            truncateResult.Should().Be(DosFcbManager.FcbSuccess, "truncate should succeed");
            
            // Verify file size
            fcb.FileSize.Should().Be(128, "file should be truncated to 1 record = 128 bytes");
        } finally {
            CloseAllOpenFiles(dosFileManager);
            CleanupTempDirectory(tempDir);
        }
    }

    private static Memory CreateMemory() {
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        Ram ram = new(A20Gate.EndOfHighMemoryArea);
        A20Gate a20Gate = new();
        return new Memory(memoryBreakpoints, ram, a20Gate);
    }

    private static DosFcbManager CreateFcbManager(IMemory memory, out DosDriveManager driveManager, out DosFileManager dosFileManager) {
        string cDrivePath = Path.GetTempPath();
        return CreateFcbManager(memory, out driveManager, out dosFileManager, cDrivePath);
    }

    private static DosFcbManager CreateFcbManager(IMemory memory, out DosDriveManager driveManager, out DosFileManager dosFileManager, string rootPath) {
        ILoggerService logger = Substitute.For<ILoggerService>();
        string cDrivePath = rootPath;
        driveManager = new DosDriveManager(logger, cDrivePath, null);
        State state = new State(CpuModel.INTEL_80286);
        DosStringDecoder stringDecoder = new(memory, state);
        IList<IVirtualDevice> virtualDevices = new List<IVirtualDevice>();
        dosFileManager = new DosFileManager(memory, stringDecoder, driveManager, logger, virtualDevices);
        
        return new DosFcbManager(memory, dosFileManager, driveManager, logger);
    }

    /// <summary>
    /// Closes all open files in the DosFileManager to prevent file locking during cleanup.
    /// </summary>
    private static void CloseAllOpenFiles(DosFileManager dosFileManager) {
        for (int i = 0; i < dosFileManager.OpenFiles.Length; i++) {
            VirtualFileBase? file = dosFileManager.OpenFiles[i];
            file?.Close();
        }
    }

    private static void WriteString(IMemory memory, uint address, string text) {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        for (int i = 0; i < bytes.Length; i++) {
            memory.UInt8[address + (uint)i] = bytes[i];
        }
        memory.UInt8[address + (uint)bytes.Length] = 0; // null terminator
    }

    private static string CreateTempDirectory() {
        string tempPath = Path.Join(Path.GetTempPath(), "Spice86_FCB_Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempPath);
        return tempPath;
    }

    private static void CleanupTempDirectory(string path) {
        if (Directory.Exists(path)) {
            Directory.Delete(path, true);
        }
    }
}