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
    public void ParseFilename_Dot_ParsesCorrectly() {
        // Arrange
        _fixture.Memory.SetZeroTerminatedString(StringAddr, ".", 128);

        // Act
        FcbParseResult result = _fixture.DosFcbManager.ParseFilename(StringAddr, FcbAddr, FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        bytesAdvanced.Should().Be(1);
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.FileName.Should().Be(".       ");
    }

    [Fact]
    public void ParseFilename_DotDot_ParsesCorrectly() {
        // Arrange
        _fixture.Memory.SetZeroTerminatedString(StringAddr, "..", 128);

        // Act
        FcbParseResult result = _fixture.DosFcbManager.ParseFilename(StringAddr, FcbAddr, FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        bytesAdvanced.Should().Be(2);
        DosFileControlBlock fcb = new DosFileControlBlock(_fixture.Memory, FcbAddr);
        fcb.FileName.Should().Be("..      ");
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

        // Act
        FcbStatus status = _fixture.DosFcbManager.CreateFile(FcbAddr);

        // Assert
        status.Should().Be(FcbStatus.Success);
        fcb.SftNumber.Should().NotBe(0);
        fcb.RecordSize.Should().Be(DosFileControlBlock.DefaultRecordSize);

        // Cleanup
        _fixture.DosFcbManager.CloseFile(FcbAddr);
    }

    [Fact]
    public void OpenFile_ExistingFile_ReturnsSuccess() {
        // Arrange
        CreateTestFile("TESTOPEN.TXT", "Test content");
        DosFileControlBlock fcb = CreateFcb("TESTOPEN", "TXT");

        // Act
        FcbStatus status = _fixture.DosFcbManager.OpenFile(FcbAddr);

        // Assert
        status.Should().Be(FcbStatus.Success);
        fcb.SftNumber.Should().NotBe(0);
        fcb.RecordSize.Should().Be(DosFileControlBlock.DefaultRecordSize);

        // Cleanup
        _fixture.DosFcbManager.CloseFile(FcbAddr);
    }

    [Fact]
    public void OpenFile_ExistingFile_PopulatesFileMetadata() {
        // Arrange
        const string testContent = "Test file content for metadata test";
        CreateTestFile("METADATA.TXT", testContent);
        DosFileControlBlock fcb = CreateFcb("METADATA", "TXT");
        fcb.FileSize = 0;
        fcb.Date = 0;
        fcb.Time = 0;

        // Act
        FcbStatus status = _fixture.DosFcbManager.OpenFile(FcbAddr);

        // Assert
        status.Should().Be(FcbStatus.Success);
        fcb.FileSize.Should().Be((uint)testContent.Length, "FileSize should match actual file size");
        fcb.Date.Should().NotBe(0, "Date should be populated from file last write time");
        fcb.Time.Should().NotBe(0, "Time should be populated from file last write time");

        ushort year = (ushort)((fcb.Date >> 9) + 1980);
        year.Should().BeInRange((ushort)1980, (ushort)2100);

        ushort hour = (ushort)(fcb.Time >> 11);
        hour.Should().BeLessThanOrEqualTo((ushort)23);

        // Cleanup
        _fixture.DosFcbManager.CloseFile(FcbAddr);
    }

    [Fact]
    public void CloseFile_OpenedFile_ReturnsSuccess() {
        // Arrange
        CreateFcb("TESTCLS", "TXT");
        _fixture.DosFcbManager.CreateFile(FcbAddr);

        // Act
        FcbStatus status = _fixture.DosFcbManager.CloseFile(FcbAddr);

        // Assert
        status.Should().Be(FcbStatus.Success);
    }

    [Fact]
    public void SequentialWriteAndRead_RoundTrip_DataPreserved() {
        // Arrange
        CreateFcb("RWTST", "DAT");
        _fixture.DosFcbManager.CreateFile(FcbAddr);
        byte[] testData = new byte[128];
        for (int i = 0; i < 128; i++) {
            testData[i] = (byte)('A' + (i % 26));
            _fixture.Memory.UInt8[DtaAddr + (uint)i] = testData[i];
        }

        // Act
        FcbStatus writeStatus = _fixture.DosFcbManager.SequentialWrite(FcbAddr, DtaAddr);
        _fixture.DosFcbManager.CloseFile(FcbAddr);
        _fixture.DosFcbManager.OpenFile(FcbAddr);
        for (int i = 0; i < 128; i++) {
            _fixture.Memory.UInt8[DtaAddr + (uint)i] = 0;
        }
        FcbStatus readStatus = _fixture.DosFcbManager.SequentialRead(FcbAddr, DtaAddr);

        // Assert
        writeStatus.Should().Be(FcbStatus.Success);
        readStatus.Should().Be(FcbStatus.Success);
        for (int i = 0; i < 128; i++) {
            _fixture.Memory.UInt8[DtaAddr + (uint)i].Should().Be((byte)('A' + (i % 26)));
        }

        // Cleanup
        _fixture.DosFcbManager.CloseFile(FcbAddr);
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
        // Arrange
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
        // Arrange
        DosExtendedFileControlBlock xfcb = new DosExtendedFileControlBlock(_fixture.Memory, FcbAddr);
        xfcb.Flag = 0xFF;
        xfcb.Attribute = 0x20;
        xfcb.DriveNumber = 0;
        xfcb.FileName = "EXTTEST ";
        xfcb.FileExtension = "DAT";

        // Act
        DosFileControlBlock result = _fixture.DosFcbManager.GetFcb(FcbAddr, out byte attr);

        // Assert
        attr.Should().Be(0x20);
        result.BaseAddress.Should().Be(FcbAddr + DosExtendedFileControlBlock.HeaderSize);
        result.FileName.Should().Be("EXTTEST ");
        result.FileExtension.Should().Be("DAT");
    }

    [Fact]
    public void RandomBlockRead_MultipleRecords_AdvancesDtaForEachRecord() {
        // Arrange
        byte[] fileData = { 0x41, 0x41, 0x41, 0x41, 0x42, 0x42, 0x42, 0x42, 0x43, 0x43, 0x43, 0x43 };
        CreateTestFile("MULTIREC.DAT", fileData);
        DosFileControlBlock fcb = CreateFcb("MULTIREC", "DAT");
        _fixture.DosFcbManager.OpenFile(FcbAddr);
        fcb.RecordSize = 4;
        fcb.RandomRecord = 0;
        ushort recordCount = 3;

        // Act
        FcbStatus readResult = _fixture.DosFcbManager.RandomBlockRead(FcbAddr, DtaAddr, ref recordCount);

        // Assert
        readResult.Should().Be(FcbStatus.Success);
        recordCount.Should().Be(3);
        _fixture.Memory.UInt8[DtaAddr + 0].Should().Be(0x41);
        _fixture.Memory.UInt8[DtaAddr + 4].Should().Be(0x42);
        _fixture.Memory.UInt8[DtaAddr + 8].Should().Be(0x43);

        // Cleanup
        _fixture.DosFcbManager.CloseFile(FcbAddr);
    }

    [Fact]
    public void RandomBlockWrite_ZeroRecords_TruncatesFile() {
        // Arrange
        string testFile = Path.Join(_mountPoint, "TRUNCATE.DAT");
        byte[] initialData = new byte[100];
        for (int i = 0; i < 100; i++) {
            initialData[i] = (byte)(i & 0xFF);
        }
        CreateTestFile("TRUNCATE.DAT", initialData);
        DosFileControlBlock fcb = CreateFcb("TRUNCATE", "DAT");
        _fixture.DosFcbManager.OpenFile(FcbAddr);
        fcb.RecordSize = 10;
        fcb.RandomRecord = 5;
        ushort recordCount = 0;

        // Act
        FcbStatus writeResult = _fixture.DosFcbManager.RandomBlockWrite(FcbAddr, DtaAddr, ref recordCount);

        // Assert
        writeResult.Should().Be(FcbStatus.Success);
        recordCount.Should().Be(0);
        fcb.FileSize.Should().Be(50);
        _fixture.DosFcbManager.CloseFile(FcbAddr);
        new FileInfo(testFile).Length.Should().Be(50);
    }

    [Fact]
    public void GetFileSize_UsesCeilingDivision() {
        // Arrange
        byte[] fileData = new byte[1000];
        CreateTestFile("SIZETEST.DAT", fileData);
        DosFileControlBlock fcb = CreateFcb("SIZETEST", "DAT");
        fcb.RecordSize = 128;

        // Act
        FcbStatus result = _fixture.DosFcbManager.GetFileSize(FcbAddr);

        // Assert
        result.Should().Be(FcbStatus.Success);
        fcb.RandomRecord.Should().Be(8);
    }

    [Fact]
    public void GetFileSize_RecordSizeZero_UsesDefault128() {
        // Arrange
        byte[] fileData = new byte[256];
        CreateTestFile("DEFAULT.DAT", fileData);
        DosFileControlBlock fcb = CreateFcb("DEFAULT", "DAT");
        fcb.RecordSize = 0;

        // Act
        FcbStatus result = _fixture.DosFcbManager.GetFileSize(FcbAddr);

        // Assert
        result.Should().Be(FcbStatus.Success);
        fcb.RandomRecord.Should().Be(2);
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