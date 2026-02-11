namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using Xunit;

public class DosFcbManagerTests : IDisposable {
    private const uint StringAddr = 0x1000;
    private const uint FcbAddr = 0x2000;
    private const uint DtaAddr = 0x3000;

    private readonly string _mountPoint;
    private readonly DosTestFixture _fixture;

    public DosFcbManagerTests() {
        _mountPoint = Path.Join(Path.GetTempPath(), $"Spice86_FCB_Tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_mountPoint);
        _fixture = new DosTestFixture(_mountPoint);
    }

    public void Dispose() {
        if (Directory.Exists(_mountPoint)) {
            Directory.Delete(_mountPoint, recursive: true);
        }
    }

    private static void WriteSpacePaddedField(IMemory memory, uint address, string value, int fieldSize) {
        byte[] buffer = new byte[fieldSize];
        for (int i = 0; i < fieldSize; i++) {
            buffer[i] = (byte)' ';
        }
        for (int i = 0; i < value.Length && i < fieldSize; i++) {
            buffer[i] = (byte)value[i];
        }
        for (int i = 0; i < fieldSize; i++) {
            memory.UInt8[address + (uint)i] = buffer[i];
        }
    }

    private DosFileControlBlock CreateFcb(string fileName, string extension, byte driveNumber = 0) {
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.DriveNumber = driveNumber;
        fcb.FileName = fileName.PadRight(8);
        fcb.FileExtension = extension.PadRight(3);
        return fcb;
    }

    private string CreateTestFile(string fileName, string content = "test") {
        string fullPath = Path.Join(_mountPoint, fileName);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private string CreateTestFile(string fileName, byte[] content) {
        string fullPath = Path.Join(_mountPoint, fileName);
        File.WriteAllBytes(fullPath, content);
        return fullPath;
    }

    private void CleanupFilePatterns(params string[] patterns) {
        foreach (string pattern in patterns) {
            foreach (string file in Directory.GetFiles(_mountPoint, pattern)) {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void ParseFilename_SimpleFilename_NoWildcards() {
        // Arrange
        _fixture.Memory.SetZeroTerminatedString(StringAddr, "TEST.TXT", 128);

        // Act
        FcbParseResult result = _fixture.DosFcbManager.ParseFilename(StringAddr, FcbAddr, FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        bytesAdvanced.Should().Be(8);
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.FileName.Should().Be("TEST    ");
        fcb.FileExtension.Should().Be("TXT");
        fcb.DriveNumber.Should().Be(0);
    }

    [Fact]
    public void ParseFilename_WithDrive_ValidDrive() {
        // Arrange
        _fixture.Memory.SetZeroTerminatedString(StringAddr, "C:FILE.DAT", 128);

        // Act
        FcbParseResult result = _fixture.DosFcbManager.ParseFilename(StringAddr, FcbAddr, 0, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        bytesAdvanced.Should().Be(10);
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.FileName.Should().Be("FILE    ");
        fcb.FileExtension.Should().Be("DAT");
        fcb.DriveNumber.Should().Be(3); // C: = 3
    }

    [Fact]
    public void ParseFilename_InvalidDrive_ContinuesParsing() {
        // Arrange
        _fixture.Memory.SetZeroTerminatedString(StringAddr, "Z:TEST.TXT", 128);

        // Act - Undocumented behavior: should keep parsing even if drive specification is invalid
        FcbParseResult result = _fixture.DosFcbManager.ParseFilename(StringAddr, FcbAddr, 0, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.InvalidDrive);
        bytesAdvanced.Should().Be(10);
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.FileName.Should().Be("TEST    ");
        fcb.FileExtension.Should().Be("TXT");
        fcb.DriveNumber.Should().Be(26); // Z: = 26
    }

    [Fact]
    public void ParseFilename_Wildcards_Asterisk() {
        // Arrange
        _fixture.Memory.SetZeroTerminatedString(StringAddr, "*.TXT", 128);

        // Act
        FcbParseResult result = _fixture.DosFcbManager.ParseFilename(StringAddr, FcbAddr, FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.WildcardsPresent);
        bytesAdvanced.Should().Be(5);
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.FileName.Should().Be("????????");
        fcb.FileExtension.Should().Be("TXT");
    }

    [Fact]
    public void ParseFilename_Wildcards_QuestionMark() {
        // Arrange
        _fixture.Memory.SetZeroTerminatedString(StringAddr, "TEST?.TX?", 128);

        // Act
        FcbParseResult result = _fixture.DosFcbManager.ParseFilename(StringAddr, FcbAddr, FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.WildcardsPresent);
        bytesAdvanced.Should().Be(9);
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.FileName.Should().Be("TEST?   ");
        fcb.FileExtension.Should().Be("TX?");
    }

    [Fact]
    public void ParseFilename_SkipLeadingSeparators() {
        // Arrange
        _fixture.Memory.SetZeroTerminatedString(StringAddr, "  :;,=+  TEST.TXT", 128);

        // Act
        FcbParseResult result = _fixture.DosFcbManager.ParseFilename(StringAddr, FcbAddr, FcbParseControl.SkipLeadingSeparators | FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.FileName.Should().Be("TEST    ");
        fcb.FileExtension.Should().Be("TXT");
    }

    [Fact]
    public void ParseFilename_WhitespaceAlwaysSkipped() {
        // Arrange - Undocumented behavior: whitespace is always skipped regardless of flags
        _fixture.Memory.SetZeroTerminatedString(StringAddr, "   \t  TEST.TXT", 128);

        // Act
        FcbParseResult result = _fixture.DosFcbManager.ParseFilename(StringAddr, FcbAddr, 0, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.FileName.Should().Be("TEST    ");
        fcb.FileExtension.Should().Be("TXT");
    }

    [Fact]
    public void ParseFilename_DotAndDotDot() {
        // Arrange
        const uint fcbAddr1 = 0x2000;
        const uint fcbAddr2 = 0x2100;

        // Test "."
        const uint stringAddr1 = 0x1000;
        _fixture.Memory.SetZeroTerminatedString(stringAddr1, ".", 128);
        FcbParseResult result1 = _fixture.DosFcbManager.ParseFilename(stringAddr1, fcbAddr1, FcbParseControl.LeaveDriveUnchanged, out uint bytes1);

        // Test ".."
        const uint stringAddr2 = 0x1100;
        _fixture.Memory.SetZeroTerminatedString(stringAddr2, "..", 128);
        FcbParseResult result2 = _fixture.DosFcbManager.ParseFilename(stringAddr2, fcbAddr2, FcbParseControl.LeaveDriveUnchanged, out uint bytes2);

        // Assert
        result1.Should().Be(FcbParseResult.NoWildcards);
        bytes1.Should().Be(1);
        DosFileControlBlock fcb1 = new DosFileControlBlock(_fixture.Memory, fcbAddr1);
        fcb1.FileName.Should().Be(".       ");

        result2.Should().Be(FcbParseResult.NoWildcards);
        bytes2.Should().Be(2);
        DosFileControlBlock fcb2 = new DosFileControlBlock(_fixture.Memory, fcbAddr2);
        fcb2.FileName.Should().Be("..      ");
    }

    [Fact]
    public void ParseFilename_NoExtension() {
        // Arrange
        _fixture.Memory.SetZeroTerminatedString(StringAddr, "NOEXT", 128);

        // Act
        FcbParseResult result = _fixture.DosFcbManager.ParseFilename(StringAddr, FcbAddr, FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        bytesAdvanced.Should().Be(5);
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.FileName.Should().Be("NOEXT   ");
        fcb.FileExtension.Should().Be("   ");
    }

    [Fact]
    public void ParseFilename_UppercaseConversion() {
        // Arrange
        _fixture.Memory.SetZeroTerminatedString(StringAddr, "lowercase.ext", 128);

        // Act
        FcbParseResult result = _fixture.DosFcbManager.ParseFilename(StringAddr, FcbAddr, FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        // Note: FCB filename is max 8 characters, "lowercase" gets truncated to "LOWERCAS"
        fcb.FileName.Should().Be("LOWERCAS");
        fcb.FileExtension.Should().Be("EXT");
    }

    [Fact]
    public void ParseFilename_ParseControlFlags() {
        // Arrange
        _fixture.Memory.SetZeroTerminatedString(StringAddr, "TEST.TXT", 128);

        // Act - PARSE_BLNK_FNAME: should blank filename field
        FcbParseResult result = _fixture.DosFcbManager.ParseFilename(StringAddr, FcbAddr, FcbParseControl.BlankFilename, out _);

        // Assert - filename field should be blanked before parsing
        result.Should().Be(FcbParseResult.NoWildcards);
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.FileName.Should().Be("TEST    ");
    }

    [Fact]
    public void CreateFile_ValidName_ReturnsSuccess() {
        // Arrange
        DosFileControlBlock fcb = CreateFcb("NEWFILE", "TXT");

        try {
            // Act
            FcbStatus status = _fixture.DosFcbManager.CreateFile(FcbAddr);

            // Assert
            status.Should().Be(FcbStatus.Success);
            fcb.SftNumber.Should().NotBe(0);
            fcb.RecordSize.Should().Be(DosFileControlBlock.DefaultRecordSize);
        } finally {
            // Cleanup
            _fixture.DosFcbManager.CloseFile(FcbAddr);
            _fixture.DosFileManager.RemoveFile("NEWFILE.TXT");
        }
    }

    [Fact]
    public void OpenFile_ExistingFile_ReturnsSuccess() {
        // Arrange
        CreateTestFile("TESTOPEN.TXT", "Test content");
        DosFileControlBlock fcb = CreateFcb("TESTOPEN", "TXT");

        try {
            // Act
            FcbStatus status = _fixture.DosFcbManager.OpenFile(FcbAddr);

            // Assert
            status.Should().Be(FcbStatus.Success);
            fcb.SftNumber.Should().NotBe(0);
            fcb.RecordSize.Should().Be(DosFileControlBlock.DefaultRecordSize);
        } finally {
            // Cleanup
            _fixture.DosFcbManager.CloseFile(FcbAddr);
        }
    }

    [Fact]
    public void OpenFile_ExistingFile_PopulatesFileMetadata() {
        // Test that FCB open populates FileSize, Date, and Time fields

        // Arrange
        const string testContent = "Test file content for metadata test";
        CreateTestFile("METADATA.TXT", testContent);
        DosFileControlBlock fcb = CreateFcb("METADATA", "TXT");

        // Zero out the metadata fields before open
        fcb.FileSize = 0;
        fcb.Date = 0;
        fcb.Time = 0;

        try {
            // Act
            FcbStatus status = _fixture.DosFcbManager.OpenFile(FcbAddr);

            // Assert
            status.Should().Be(FcbStatus.Success);

            // FileSize should be populated with actual file size
            fcb.FileSize.Should().Be((uint)testContent.Length, "FileSize should match actual file size");

            // Date and Time should be non-zero (populated with file's last write time)
            fcb.Date.Should().NotBe(0, "Date should be populated from file last write time");
            fcb.Time.Should().NotBe(0, "Time should be populated from file last write time");

            // Verify Date/Time are in reasonable range (DOS format starts from 1980)
            // Date: bits 15-9 = year-1980, 8-5 = month, 4-0 = day
            ushort year = (ushort)((fcb.Date >> 9) + 1980);
            year.Should().BeInRange((ushort)1980, (ushort)2100);

            // Time: bits 15-11 = hour, 10-5 = minute, 4-0 = seconds/2
            ushort hour = (ushort)(fcb.Time >> 11);
            hour.Should().BeLessThanOrEqualTo((ushort)23);
        } finally {
            // Cleanup
            _fixture.DosFcbManager.CloseFile(FcbAddr);
        }
    }

    [Fact]
    public void CloseFile_OpenedFile_ReturnsSuccess() {
        // Arrange
        CreateFcb("TESTCLS", "TXT");
        _fixture.DosFcbManager.CreateFile(FcbAddr);

        try {
            // Act
            FcbStatus status = _fixture.DosFcbManager.CloseFile(FcbAddr);

            // Assert
            status.Should().Be(FcbStatus.Success);
        } finally {
            // Cleanup
            _fixture.DosFileManager.RemoveFile("TESTCLS.TXT");
        }
    }

    [Fact]
    public void ReadWriteFile_SequentialRecords_WorksCorrectly() {
        // Arrange
        CreateFcb("RWTST", "DAT");
        _fixture.DosFcbManager.CreateFile(FcbAddr);

        try {
            // Write test data
            byte[] testData = new byte[128];
            for (int i = 0; i < 128; i++) {
                testData[i] = (byte)('A' + (i % 26));
            }
            for (int i = 0; i < 128; i++) {
                _fixture.Memory.UInt8[DtaAddr + (uint)i] = testData[i];
            }

            FcbStatus writeStatus = _fixture.DosFcbManager.SequentialWrite(FcbAddr, DtaAddr);
            writeStatus.Should().Be(FcbStatus.Success);

            // Close and reopen for reading
            _fixture.DosFcbManager.CloseFile(FcbAddr);
            _fixture.DosFcbManager.OpenFile(FcbAddr);

            // Read data back
            Array.Clear(testData, 0, testData.Length);
            for (int i = 0; i < 128; i++) {
                _fixture.Memory.UInt8[DtaAddr + (uint)i] = 0;
            }

            FcbStatus readStatus = _fixture.DosFcbManager.SequentialRead(FcbAddr, DtaAddr);
            readStatus.Should().Be(FcbStatus.Success);

            byte[] readData = new byte[128];
            for (int i = 0; i < 128; i++) {
                readData[i] = _fixture.Memory.UInt8[DtaAddr + (uint)i];
            }
            for (int i = 0; i < 128; i++) {
                readData[i].Should().Be((byte)('A' + (i % 26)));
            }
        } finally {
            // Cleanup
            _fixture.DosFcbManager.CloseFile(FcbAddr);
            _fixture.DosFileManager.RemoveFile("RWTST.DAT");
        }
    }

    [Fact]
    public void RenameFile_SimpleWildcardExtension_RenamesAllMatches() {
        // Test 1 from fcb_ren.c:
        // fn1 = "*", fe1 = "in", fn2 = "*", fe2 = "out"
        // create "one.in", "two.in", "three.in", "four.in", "five.in", "none.ctl"
        // expect "one.out", "two.out", "three.out", "four.out", "five.out", "none.ctl"

        // Arrange
        CleanupFilePatterns("*.in", "*.out", "*.ctl");

        string[] sourceFiles = { "one.in", "two.in", "three.in", "four.in", "five.in", "none.ctl" };
        foreach (string file in sourceFiles) {
            CreateTestFile(file);
        }

        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "????????";
        fcb.FileExtension = "IN ";

        // New name FCB starts at offset+16 from FCB base
        // Within that FCB: drive at +0, name at +1, ext at +9
        // Absolute offsets from FCB base: name at 0x11 (17), ext at 0x19 (25)
        WriteSpacePaddedField(_fixture.Memory, FcbAddr + 17, "????????", 8);
        WriteSpacePaddedField(_fixture.Memory, FcbAddr + 25, "OUT", 3);

        // Act
        FcbStatus status = _fixture.DosFcbManager.RenameFile(FcbAddr);

        // Assert
        status.Should().Be(FcbStatus.Success);
        // DOS renames files in uppercase
        File.Exists(Path.Join(_mountPoint, "ONE.OUT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "TWO.OUT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "THREE.OUT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "FOUR.OUT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "FIVE.OUT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "none.ctl")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "one.in")).Should().BeFalse();
    }

    [Fact]
    public void RenameFile_PrefixWildcard_RenamesMatchingPrefix() {
        // Test 2 from fcb_ren.c:
        // fn1 = "a*", fe1 = "*", fn2 = "b*", fe2 = "out"
        // create "aone.in", "atwo.in", "athree.in", "afour.in", "afive.in", "xnone.ctl"
        // expect "bone.out", "btwo.out", "bthree.out", "bfour.out", "bfive.out", "xnone.ctl"

        // Arrange
        CleanupFilePatterns("a*.in", "b*.out", "x*.ctl");

        string[] sourceFiles = { "aone.in", "atwo.in", "athree.in", "afour.in", "afive.in", "xnone.ctl" };
        foreach (string file in sourceFiles) {
            CreateTestFile(file);
        }

        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "A???????";
        fcb.FileExtension = "???";

        WriteSpacePaddedField(_fixture.Memory, FcbAddr + 17, "B???????", 8);
        WriteSpacePaddedField(_fixture.Memory, FcbAddr + 25, "OUT", 3);

        // Act
        FcbStatus status = _fixture.DosFcbManager.RenameFile(FcbAddr);

        // Assert
        status.Should().Be(FcbStatus.Success);
        // DOS renames files in uppercase
        File.Exists(Path.Join(_mountPoint, "BONE.OUT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "BTWO.OUT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "BTHREE.OUT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "BFOUR.OUT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "BFIVE.OUT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "xnone.ctl")).Should().BeTrue();
    }

    [Fact]
    public void RenameFile_ComplexWildcardPattern_HandlesCorrectly() {
        // Test 3 from fcb_ren.c:
        // fn1 = "abc0??", fe1 = "*", fn2 = "???6*", fe2 = "*"
        // create "abc001.txt", "abc002.txt", "abc003.txt", "abc004.txt", "abc005.txt", "abc010.txt", "xbc007.txt"
        // expect "abc601.txt", "abc602.txt", "abc603.txt", "abc604.txt", "abc605.txt", "abc610.txt", "xbc007.txt"

        // Arrange
        CleanupFilePatterns("abc*.txt", "xbc*.txt");

        string[] sourceFiles = { "abc001.txt", "abc002.txt", "abc003.txt", "abc004.txt", "abc005.txt", "abc010.txt", "xbc007.txt" };
        foreach (string file in sourceFiles) {
            CreateTestFile(file);
        }

        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "ABC0????";
        fcb.FileExtension = "???";

        WriteSpacePaddedField(_fixture.Memory, FcbAddr + 17, "???6????", 8);
        WriteSpacePaddedField(_fixture.Memory, FcbAddr + 25, "???", 3);

        // Act
        FcbStatus status = _fixture.DosFcbManager.RenameFile(FcbAddr);

        // Assert
        status.Should().Be(FcbStatus.Success);
        // DOS renames files in uppercase
        File.Exists(Path.Join(_mountPoint, "ABC601.TXT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "ABC602.TXT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "ABC603.TXT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "ABC604.TXT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "ABC605.TXT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "ABC610.TXT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "xbc007.txt")).Should().BeTrue();
    }

    [Fact]
    public void RenameFile_ShortenExtension_TruncatesCorrectly() {
        // Test 4 from fcb_ren.c:
        // fn1 = "abc*", fe1 = "htm", fn2 = "*", fe2 = "??"
        // create "abc001.htm", "abc002.htm", "abc003.htm", "abc004.htm", "abc005.htm", "abc010.htm", "xbc007.htm"
        // expect "abc001.ht", "abc002.ht", "abc003.ht", "abc004.ht", "abc005.ht", "abc010.ht", "xbc007.htm"

        // Arrange
        CleanupFilePatterns("abc*.htm", "abc*.ht", "xbc*.htm");

        string[] sourceFiles = { "abc001.htm", "abc002.htm", "abc003.htm", "abc004.htm", "abc005.htm", "abc010.htm", "xbc007.htm" };
        foreach (string file in sourceFiles) {
            CreateTestFile(file);
        }

        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "ABC?????";
        fcb.FileExtension = "HTM";

        WriteSpacePaddedField(_fixture.Memory, FcbAddr + 17, "????????", 8);
        WriteSpacePaddedField(_fixture.Memory, FcbAddr + 25, "?? ", 3);

        // Act
        FcbStatus status = _fixture.DosFcbManager.RenameFile(FcbAddr);

        // Assert
        status.Should().Be(FcbStatus.Success);
        // DOS renames files in uppercase
        File.Exists(Path.Join(_mountPoint, "ABC001.HT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "ABC002.HT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "ABC003.HT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "ABC004.HT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "ABC005.HT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "ABC010.HT")).Should().BeTrue();
        File.Exists(Path.Join(_mountPoint, "xbc007.htm")).Should().BeTrue();
    }

    [Fact]
    public void SetRandomRecord_CalculatesCorrectly() {
        // Arrange
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);

        // Set current block and record
        fcb.CurrentBlock = 5;
        fcb.CurrentRecord = 42;
        fcb.RecordSize = 128;

        // Act
        _fixture.DosFcbManager.SetRandomRecord(FcbAddr);

        // Assert - random record = (currentBlock * 128) + currentRecord
        const uint expectedRandom = (5u * 128) + 42;
        fcb.RandomRecord.Should().Be(expectedRandom);
    }

    [Fact]
    public void GetFcb_StandardFcb_ReturnsCorrectly() {
        // Test standard (non-extended) FCB
        CreateFcb("TESTFILE", "TXT");

        // Act
        DosFileControlBlock result = _fixture.DosFcbManager.GetFcb(FcbAddr, out byte attr);

        // Assert
        result.BaseAddress.Should().Be(FcbAddr);
        attr.Should().Be(0);
        result.FileName.Should().Be("TESTFILE");
    }

    [Fact]
    public void GetFcb_ExtendedFcb_ReturnsAttributeAndEmbeddedFcb() {
        // Test extended FCB with attribute support
        DosExtendedFileControlBlock xfcb = new DosExtendedFileControlBlock(_fixture.Memory, FcbAddr);

        // Set extended FCB marker and attribute
        xfcb.Flag = 0xFF;
        xfcb.Attribute = 0x20; // Archive attribute

        // Set FCB fields (xfcb inherits from DosFileControlBlock)
        xfcb.DriveNumber = 0;
        xfcb.FileName = "EXTTEST ";
        xfcb.FileExtension = "DAT";

        // Act
        DosFileControlBlock result = _fixture.DosFcbManager.GetFcb(FcbAddr, out byte attr);

        // Assert
        attr.Should().Be(0x20);
        result.BaseAddress.Should().Be(FcbAddr + DosExtendedFileControlBlock.HeaderSize); // Points to embedded FCB
        result.FileName.Should().Be("EXTTEST ");
        result.FileExtension.Should().Be("DAT");
    }

    [Fact]
    public void RandomBlockRead_MultipleRecords_AdvancesDtaForEachRecord() {
        // Arrange: Create test file with multiple distinct records
        // Write 3 records: "AAAA", "BBBB", "CCCC" (4 bytes each)
        byte[] fileData = new byte[] {
            0x41, 0x41, 0x41, 0x41,  // Record 0: AAAA
            0x42, 0x42, 0x42, 0x42,  // Record 1: BBBB  
            0x43, 0x43, 0x43, 0x43   // Record 2: CCCC
        };
        CreateTestFile("MULTIREC.DAT", fileData);

        // Setup FCB
        DosFileControlBlock fcb = CreateFcb("MULTIREC", "DAT");

        // Open file
        FcbStatus openResult = _fixture.DosFcbManager.OpenFile(FcbAddr);
        openResult.Should().Be(FcbStatus.Success);

        // Set RecordSize AFTER OpenFile because OpenFile always resets RecordSize to 128
        fcb.RecordSize = 4;  // 4-byte records
        fcb.RandomRecord = 0; // Start at record 0

        ushort recordCount = 3;

        // Act: Read 3 records
        FcbStatus readResult = _fixture.DosFcbManager.RandomBlockRead(FcbAddr, DtaAddr, ref recordCount);

        // Assert: Should have read all 3 records
        readResult.Should().Be(FcbStatus.Success);
        recordCount.Should().Be(3);

        // Verify each record was written to consecutive DTA locations
        // Record 0 at DTA+0: AAAA
        _fixture.Memory.UInt8[DtaAddr + 0].Should().Be(0x41);
        _fixture.Memory.UInt8[DtaAddr + 1].Should().Be(0x41);
        _fixture.Memory.UInt8[DtaAddr + 2].Should().Be(0x41);
        _fixture.Memory.UInt8[DtaAddr + 3].Should().Be(0x41);

        // Record 1 at DTA+4: BBBB (DTA advanced by record size)
        _fixture.Memory.UInt8[DtaAddr + 4].Should().Be(0x42);
        _fixture.Memory.UInt8[DtaAddr + 5].Should().Be(0x42);
        _fixture.Memory.UInt8[DtaAddr + 6].Should().Be(0x42);
        _fixture.Memory.UInt8[DtaAddr + 7].Should().Be(0x42);

        // Record 2 at DTA+8: CCCC (DTA advanced again)
        _fixture.Memory.UInt8[DtaAddr + 8].Should().Be(0x43);
        _fixture.Memory.UInt8[DtaAddr + 9].Should().Be(0x43);
        _fixture.Memory.UInt8[DtaAddr + 10].Should().Be(0x43);
        _fixture.Memory.UInt8[DtaAddr + 11].Should().Be(0x43);

        // Cleanup
        _fixture.DosFcbManager.CloseFile(FcbAddr);
    }

    [Fact]
    public void RandomBlockWrite_ZeroRecords_TruncatesFileExplicitly() {
        // Arrange: Create file with initial content
        string testFile = Path.Join(_mountPoint, "TRUNCATE.DAT");

        // Write 100 bytes initially
        byte[] initialData = new byte[100];
        for (int i = 0; i < 100; i++) {
            initialData[i] = (byte)(i & 0xFF);
        }
        CreateTestFile("TRUNCATE.DAT", initialData);

        // Setup FCB
        DosFileControlBlock fcb = CreateFcb("TRUNCATE", "DAT");

        // Open file
        FcbStatus openResult = _fixture.DosFcbManager.OpenFile(FcbAddr);
        openResult.Should().Be(FcbStatus.Success);

        // Set RecordSize and RandomRecord AFTER OpenFile because OpenFile always resets RecordSize to 128
        fcb.RecordSize = 10;  // 10-byte records
        fcb.RandomRecord = 5;  // Truncate to record 5 = 50 bytes

        ushort recordCount = 0; // CX=0 means truncate

        // Act: Write 0 records (truncate operation)
        FcbStatus writeResult = _fixture.DosFcbManager.RandomBlockWrite(FcbAddr, DtaAddr, ref recordCount);

        // Assert: Operation should succeed
        writeResult.Should().Be(FcbStatus.Success);
        recordCount.Should().Be(0); // Should remain 0

        // Verify file size in FCB was updated
        fcb.FileSize.Should().Be(50); // 5 records * 10 bytes

        // Close and verify actual file size on disk
        _fixture.DosFcbManager.CloseFile(FcbAddr);
        FileInfo fileInfo = new FileInfo(testFile);
        fileInfo.Length.Should().Be(50); // File physically truncated to 50 bytes
    }

    [Fact]
    public void GetFileSize_UsesCeilingDivision() {
        // Arrange: Create file with size that isn't evenly divisible by record size
        // Write 1000 bytes
        byte[] fileData = new byte[1000];
        CreateTestFile("SIZETEST.DAT", fileData);

        // Setup FCB with 128-byte record size
        DosFileControlBlock fcb = CreateFcb("SIZETEST", "DAT");
        fcb.RecordSize = 128;

        // Act: Get file size
        FcbStatus result = _fixture.DosFcbManager.GetFileSize(FcbAddr);

        // Assert: Should succeed
        result.Should().Be(FcbStatus.Success);

        // Ceiling division: 1000 / 128 = 7, then 1000 % 128 != 0, so 7+1 = 8
        fcb.RandomRecord.Should().Be(8, "Ceiling division: 1000/128=7, 1000%%128!=0 so 7+1=8");
    }

    [Fact]
    public void GetFileSize_RecordSizeZero_UsesDefault128() {
        // Arrange
        // Write 256 bytes (exactly 2 default records)
        byte[] fileData = new byte[256];
        CreateTestFile("DEFAULT.DAT", fileData);

        // Setup FCB with RecordSize = 0 (should use default)
        DosFileControlBlock fcb = CreateFcb("DEFAULT", "DAT");
        fcb.RecordSize = 0; // Zero means use default

        // Act
        FcbStatus result = _fixture.DosFcbManager.GetFileSize(FcbAddr);

        // Assert
        result.Should().Be(FcbStatus.Success);
        // 256 / 128 (default) = 2 records
        fcb.RandomRecord.Should().Be(2, "256 bytes / 128 (default) = 2 records");
    }

    [Fact]
    public void OpenFile_NonExistentFile_ReturnsError() {
        // Arrange
        CreateFcb("NOTFOUND", "DAT");

        // Act
        FcbStatus result = _fixture.DosFcbManager.OpenFile(FcbAddr);

        // Assert: Should fail
        result.Should().Be(FcbStatus.Error);
    }

    [Fact]
    public void GetFileSize_NonExistentFile_ReturnsError() {
        // Arrange
        DosFileControlBlock fcb = CreateFcb("MISSING", "TXT");
        fcb.RecordSize = 512;

        // Act
        FcbStatus result = _fixture.DosFcbManager.GetFileSize(FcbAddr);

        // Assert
        result.Should().Be(FcbStatus.Error);
    }
}