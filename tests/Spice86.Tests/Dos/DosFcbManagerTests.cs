namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using Xunit;

public class DosFcbManagerTests : IDisposable {
    private readonly string _mountPoint;

    public DosFcbManagerTests() {
        _mountPoint = Path.Join(Path.GetTempPath(), $"Spice86_FCB_Tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_mountPoint);
    }

    public void Dispose() {
        if (Directory.Exists(_mountPoint)) {
            Directory.Delete(_mountPoint, recursive: true);
        }
    }

    /// <summary>
    /// Writes a space-padded string to memory (FCB fields are space-padded, not null-terminated).
    /// </summary>
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



    [Fact]
    public void ParseFilename_SimpleFilename_NoWildcards() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint stringAddr = 0x1000;
        const uint fcbAddr = 0x2000;
        fixture.Memory.SetZeroTerminatedString(stringAddr, "TEST.TXT", 128);

        // Act
        FcbParseResult result = fixture.DosFcbManager.ParseFilename(stringAddr, fcbAddr, FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        bytesAdvanced.Should().Be(8);
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.FileName.Should().Be("TEST    ");
        fcb.FileExtension.Should().Be("TXT");
        fcb.DriveNumber.Should().Be(0);
    }

    [Fact]
    public void ParseFilename_WithDrive_ValidDrive() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint stringAddr = 0x1000;
        const uint fcbAddr = 0x2000;
        fixture.Memory.SetZeroTerminatedString(stringAddr, "C:FILE.DAT", 128);

        // Act
        FcbParseResult result = fixture.DosFcbManager.ParseFilename(stringAddr, fcbAddr, 0, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        bytesAdvanced.Should().Be(10);
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.FileName.Should().Be("FILE    ");
        fcb.FileExtension.Should().Be("DAT");
        fcb.DriveNumber.Should().Be(3); // C: = 3
    }

    [Fact]
    public void ParseFilename_InvalidDrive_ContinuesParsing() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint stringAddr = 0x1000;
        const uint fcbAddr = 0x2000;
        fixture.Memory.SetZeroTerminatedString(stringAddr, "Z:TEST.TXT", 128);

        // Act - Undocumented behavior: should keep parsing even if drive specification is invalid
        FcbParseResult result = fixture.DosFcbManager.ParseFilename(stringAddr, fcbAddr, 0, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.InvalidDrive);
        bytesAdvanced.Should().Be(10);
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.FileName.Should().Be("TEST    ");
        fcb.FileExtension.Should().Be("TXT");
        fcb.DriveNumber.Should().Be(26); // Z: = 26
    }

    [Fact]
    public void ParseFilename_Wildcards_Asterisk() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint stringAddr = 0x1000;
        const uint fcbAddr = 0x2000;
        fixture.Memory.SetZeroTerminatedString(stringAddr, "*.TXT", 128);

        // Act
        FcbParseResult result = fixture.DosFcbManager.ParseFilename(stringAddr, fcbAddr, FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.WildcardsPresent);
        bytesAdvanced.Should().Be(5);
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.FileName.Should().Be("????????");
        fcb.FileExtension.Should().Be("TXT");
    }

    [Fact]
    public void ParseFilename_Wildcards_QuestionMark() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint stringAddr = 0x1000;
        const uint fcbAddr = 0x2000;
        fixture.Memory.SetZeroTerminatedString(stringAddr, "TEST?.TX?", 128);

        // Act
        FcbParseResult result = fixture.DosFcbManager.ParseFilename(stringAddr, fcbAddr, FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.WildcardsPresent);
        bytesAdvanced.Should().Be(9);
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.FileName.Should().Be("TEST?   ");
        fcb.FileExtension.Should().Be("TX?");
    }

    [Fact]
    public void ParseFilename_SkipLeadingSeparators() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint stringAddr = 0x1000;
        const uint fcbAddr = 0x2000;
        fixture.Memory.SetZeroTerminatedString(stringAddr, "  :;,=+  TEST.TXT", 128);

        // Act
        FcbParseResult result = fixture.DosFcbManager.ParseFilename(stringAddr, fcbAddr, FcbParseControl.SkipLeadingSeparators | FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.FileName.Should().Be("TEST    ");
        fcb.FileExtension.Should().Be("TXT");
    }

    [Fact]
    public void ParseFilename_WhitespaceAlwaysSkipped() {
        // Arrange - Undocumented behavior: whitespace is always skipped regardless of flags
        DosTestFixture fixture = new(_mountPoint);
        const uint stringAddr = 0x1000;
        const uint fcbAddr = 0x2000;
        fixture.Memory.SetZeroTerminatedString(stringAddr, "   \t  TEST.TXT", 128);

        // Act
        FcbParseResult result = fixture.DosFcbManager.ParseFilename(stringAddr, fcbAddr, 0, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.FileName.Should().Be("TEST    ");
        fcb.FileExtension.Should().Be("TXT");
    }

    [Fact]
    public void ParseFilename_DotAndDotDot() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint fcbAddr1 = 0x2000;
        const uint fcbAddr2 = 0x2100;

        // Test "."
        const uint stringAddr1 = 0x1000;
        fixture.Memory.SetZeroTerminatedString(stringAddr1, ".", 128);
        FcbParseResult result1 = fixture.DosFcbManager.ParseFilename(stringAddr1, fcbAddr1, FcbParseControl.LeaveDriveUnchanged, out uint bytes1);

        // Test ".."
        const uint stringAddr2 = 0x1100;
        fixture.Memory.SetZeroTerminatedString(stringAddr2, "..", 128);
        FcbParseResult result2 = fixture.DosFcbManager.ParseFilename(stringAddr2, fcbAddr2, FcbParseControl.LeaveDriveUnchanged, out uint bytes2);

        // Assert
        result1.Should().Be(FcbParseResult.NoWildcards);
        bytes1.Should().Be(1);
        DosFileControlBlock fcb1 = new DosFileControlBlock(fixture.Memory, fcbAddr1);
        fcb1.FileName.Should().Be(".       ");

        result2.Should().Be(FcbParseResult.NoWildcards);
        bytes2.Should().Be(2);
        DosFileControlBlock fcb2 = new DosFileControlBlock(fixture.Memory, fcbAddr2);
        fcb2.FileName.Should().Be("..      ");
    }

    [Fact]
    public void ParseFilename_NoExtension() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint stringAddr = 0x1000;
        const uint fcbAddr = 0x2000;
        fixture.Memory.SetZeroTerminatedString(stringAddr, "NOEXT", 128);

        // Act
        FcbParseResult result = fixture.DosFcbManager.ParseFilename(stringAddr, fcbAddr, FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        bytesAdvanced.Should().Be(5);
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.FileName.Should().Be("NOEXT   ");
        fcb.FileExtension.Should().Be("   ");
    }

    [Fact]
    public void ParseFilename_UppercaseConversion() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint stringAddr = 0x1000;
        const uint fcbAddr = 0x2000;
        fixture.Memory.SetZeroTerminatedString(stringAddr, "lowercase.ext", 128);

        // Act
        FcbParseResult result = fixture.DosFcbManager.ParseFilename(stringAddr, fcbAddr, FcbParseControl.LeaveDriveUnchanged, out uint bytesAdvanced);

        // Assert
        result.Should().Be(FcbParseResult.NoWildcards);
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        // Note: FCB filename is max 8 characters, "lowercase" gets truncated to "LOWERCAS"
        fcb.FileName.Should().Be("LOWERCAS");
        fcb.FileExtension.Should().Be("EXT");
    }

    [Fact]
    public void ParseFilename_ParseControlFlags() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint stringAddr = 0x1000;
        const uint fcbAddr = 0x2000;
        fixture.Memory.SetZeroTerminatedString(stringAddr, "TEST.TXT", 128);

        // Act - PARSE_BLNK_FNAME: should blank filename field
        FcbParseResult result = fixture.DosFcbManager.ParseFilename(stringAddr, fcbAddr, FcbParseControl.BlankFilename, out _);

        // Assert - filename field should be blanked before parsing
        result.Should().Be(FcbParseResult.NoWildcards);
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.FileName.Should().Be("TEST    ");
    }





    [Fact]
    public void CreateFile_ValidName_ReturnsSuccess() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "NEWFILE ";
        fcb.FileExtension = "TXT";

        try {
            // Act
            FcbStatus status = fixture.DosFcbManager.CreateFile(fcbAddr);

            // Assert
            status.Should().Be(FcbStatus.Success);
            fcb.SftNumber.Should().NotBe(0);
            fcb.RecordSize.Should().Be(DosFileControlBlock.DefaultRecordSize);
        } finally {
            // Cleanup
            fixture.DosFcbManager.CloseFile(fcbAddr);
            fixture.DosFileManager.RemoveFile("NEWFILE.TXT");
        }
    }

    [Fact]
    public void OpenFile_ExistingFile_ReturnsSuccess() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        string testFile = Path.Join(_mountPoint, "TESTOPEN.TXT");
        File.WriteAllText(testFile, "Test content");

        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "TESTOPEN";
        fcb.FileExtension = "TXT";

        try {
            // Act
            FcbStatus status = fixture.DosFcbManager.OpenFile(fcbAddr);

            // Assert
            status.Should().Be(FcbStatus.Success);
            fcb.SftNumber.Should().NotBe(0);
            fcb.RecordSize.Should().Be(DosFileControlBlock.DefaultRecordSize);
        } finally {
            // Cleanup
            fixture.DosFcbManager.CloseFile(fcbAddr);
            File.Delete(testFile);
        }
    }

    [Fact]
    public void OpenFile_ExistingFile_PopulatesFileMetadata() {
        // Test that FCB open populates FileSize, Date, and Time fields

        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const string testContent = "Test file content for metadata test";
        string testFile = Path.Join(_mountPoint, "METADATA.TXT");
        File.WriteAllText(testFile, testContent);

        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "METADATA";
        fcb.FileExtension = "TXT";

        // Zero out the metadata fields before open
        fcb.FileSize = 0;
        fcb.Date = 0;
        fcb.Time = 0;

        try {
            // Act
            FcbStatus status = fixture.DosFcbManager.OpenFile(fcbAddr);

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
            fixture.DosFcbManager.CloseFile(fcbAddr);
            File.Delete(testFile);
        }
    }

    [Fact]
    public void CloseFile_OpenedFile_ReturnsSuccess() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "TESTCLS ";
        fcb.FileExtension = "TXT";
        fixture.DosFcbManager.CreateFile(fcbAddr);

        try {
            // Act
            FcbStatus status = fixture.DosFcbManager.CloseFile(fcbAddr);

            // Assert
            status.Should().Be(FcbStatus.Success);
        } finally {
            // Cleanup
            fixture.DosFileManager.RemoveFile("TESTCLS.TXT");
        }
    }

    [Fact]
    public void ReadWriteFile_SequentialRecords_WorksCorrectly() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint fcbAddr = 0x2000;
        const uint dtaAddr = 0x3000;

        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "RWTST   ";
        fcb.FileExtension = "DAT";
        fixture.DosFcbManager.CreateFile(fcbAddr);

        try {
            // Write test data
            byte[] testData = new byte[128];
            for (int i = 0; i < 128; i++) {
                testData[i] = (byte)('A' + (i % 26));
            }
            for (int i = 0; i < 128; i++) {
                fixture.Memory.UInt8[dtaAddr + (uint)i] = testData[i];
            }

            FcbStatus writeStatus = fixture.DosFcbManager.SequentialWrite(fcbAddr, dtaAddr);
            writeStatus.Should().Be(FcbStatus.Success);

            // Close and reopen for reading
            fixture.DosFcbManager.CloseFile(fcbAddr);
            fixture.DosFcbManager.OpenFile(fcbAddr);

            // Read data back
            Array.Clear(testData, 0, testData.Length);
            for (int i = 0; i < 128; i++) {
                fixture.Memory.UInt8[dtaAddr + (uint)i] = 0;
            }

            FcbStatus readStatus = fixture.DosFcbManager.SequentialRead(fcbAddr, dtaAddr);
            readStatus.Should().Be(FcbStatus.Success);

            byte[] readData = new byte[128];
            for (int i = 0; i < 128; i++) {
                readData[i] = fixture.Memory.UInt8[dtaAddr + (uint)i];
            }
            for (int i = 0; i < 128; i++) {
                readData[i].Should().Be((byte)('A' + (i % 26)));
            }
        } finally {
            // Cleanup
            fixture.DosFcbManager.CloseFile(fcbAddr);
            fixture.DosFileManager.RemoveFile("RWTST.DAT");
        }
    }

    [Fact]
    public void RenameFile_SimpleWildcardExtension_RenamesAllMatches() {
        // Test 1 from fcb_ren.c:
        // fn1 = "*", fe1 = "in", fn2 = "*", fe2 = "out"
        // create "one.in", "two.in", "three.in", "four.in", "five.in", "none.ctl"
        // expect "one.out", "two.out", "three.out", "four.out", "five.out", "none.ctl"

        // Arrange
        DosTestFixture fixture = new(_mountPoint);

        // Clean up any existing files from previous test runs
        foreach (string pattern in new[] { "*.in", "*.out", "*.ctl" }) {
            foreach (string file in Directory.GetFiles(_mountPoint, pattern)) {
                File.Delete(file);
            }
        }

        string[] sourceFiles = { "one.in", "two.in", "three.in", "four.in", "five.in", "none.ctl" };

        foreach (string fullPath in sourceFiles.Select(file => Path.Join(_mountPoint, file))) {
            File.WriteAllText(fullPath, "test");
        }

        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "????????";
        fcb.FileExtension = "IN ";

        // New name FCB starts at offset+16 from FCB base
        // Within that FCB: drive at +0, name at +1, ext at +9
        // Absolute offsets from FCB base: name at 0x11 (17), ext at 0x19 (25)
        WriteSpacePaddedField(fixture.Memory, fcbAddr + 17, "????????", 8);
        WriteSpacePaddedField(fixture.Memory, fcbAddr + 25, "OUT", 3);

        try {
            // Act
            FcbStatus status = fixture.DosFcbManager.RenameFile(fcbAddr);

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
        } finally {
            // Cleanup
            foreach (string file in new[] { "ONE.OUT", "TWO.OUT", "THREE.OUT", "FOUR.OUT", "FIVE.OUT", "none.ctl" }) {
                File.Delete(Path.Join(_mountPoint, file));
            }
        }
    }

    [Fact]
    public void RenameFile_PrefixWildcard_RenamesMatchingPrefix() {
        // Test 2 from fcb_ren.c:
        // fn1 = "a*", fe1 = "*", fn2 = "b*", fe2 = "out"
        // create "aone.in", "atwo.in", "athree.in", "afour.in", "afive.in", "xnone.ctl"
        // expect "bone.out", "btwo.out", "bthree.out", "bfour.out", "bfive.out", "xnone.ctl"

        // Arrange
        DosTestFixture fixture = new(_mountPoint);

        // Clean up any existing files from previous test runs
        foreach (string pattern in new[] { "a*.in", "b*.out", "x*.ctl" }) {
            foreach (string file in Directory.GetFiles(_mountPoint, pattern)) {
                File.Delete(file);
            }
        }

        string[] sourceFiles = { "aone.in", "atwo.in", "athree.in", "afour.in", "afive.in", "xnone.ctl" };

        foreach (string file in sourceFiles) {
            File.WriteAllText(Path.Join(_mountPoint, file), "test");
        }

        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "A???????";
        fcb.FileExtension = "???";

        WriteSpacePaddedField(fixture.Memory, fcbAddr + 17, "B???????", 8);
        WriteSpacePaddedField(fixture.Memory, fcbAddr + 25, "OUT", 3);

        try {
            // Act
            FcbStatus status = fixture.DosFcbManager.RenameFile(fcbAddr);

            // Assert
            status.Should().Be(FcbStatus.Success);
            // DOS renames files in uppercase
            File.Exists(Path.Join(_mountPoint, "BONE.OUT")).Should().BeTrue();
            File.Exists(Path.Join(_mountPoint, "BTWO.OUT")).Should().BeTrue();
            File.Exists(Path.Join(_mountPoint, "BTHREE.OUT")).Should().BeTrue();
            File.Exists(Path.Join(_mountPoint, "BFOUR.OUT")).Should().BeTrue();
            File.Exists(Path.Join(_mountPoint, "BFIVE.OUT")).Should().BeTrue();
            File.Exists(Path.Join(_mountPoint, "xnone.ctl")).Should().BeTrue();
        } finally {
            // Cleanup
            foreach (string file in new[] { "BONE.OUT", "BTWO.OUT", "BTHREE.OUT", "BFOUR.OUT", "BFIVE.OUT", "xnone.ctl" }) {
                File.Delete(Path.Join(_mountPoint, file));
            }
        }
    }

    [Fact]
    public void RenameFile_ComplexWildcardPattern_HandlesCorrectly() {
        // Test 3 from fcb_ren.c:
        // fn1 = "abc0??", fe1 = "*", fn2 = "???6*", fe2 = "*"
        // create "abc001.txt", "abc002.txt", "abc003.txt", "abc004.txt", "abc005.txt", "abc010.txt", "xbc007.txt"
        // expect "abc601.txt", "abc602.txt", "abc603.txt", "abc604.txt", "abc605.txt", "abc610.txt", "xbc007.txt"

        // Arrange
        DosTestFixture fixture = new(_mountPoint);

        // Clean up any existing files from previous test runs
        foreach (string pattern in new[] { "abc*.txt", "xbc*.txt" }) {
            foreach (string file in Directory.GetFiles(_mountPoint, pattern)) {
                File.Delete(file);
            }
        }

        string[] sourceFiles = { "abc001.txt", "abc002.txt", "abc003.txt", "abc004.txt", "abc005.txt", "abc010.txt", "xbc007.txt" };

        foreach (string file in sourceFiles) {
            File.WriteAllText(Path.Join(_mountPoint, file), "test");
        }

        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "ABC0????";
        fcb.FileExtension = "???";

        WriteSpacePaddedField(fixture.Memory, fcbAddr + 17, "???6????", 8);
        WriteSpacePaddedField(fixture.Memory, fcbAddr + 25, "???", 3);

        try {
            // Act
            FcbStatus status = fixture.DosFcbManager.RenameFile(fcbAddr);

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
        } finally {
            // Cleanup
            foreach (string file in new[] { "ABC601.TXT", "ABC602.TXT", "ABC603.TXT", "ABC604.TXT", "ABC605.TXT", "ABC610.TXT", "xbc007.txt" }) {
                File.Delete(Path.Join(_mountPoint, file));
            }
        }
    }

    [Fact]
    public void RenameFile_ShortenExtension_TruncatesCorrectly() {
        // Test 4 from fcb_ren.c:
        // fn1 = "abc*", fe1 = "htm", fn2 = "*", fe2 = "??"
        // create "abc001.htm", "abc002.htm", "abc003.htm", "abc004.htm", "abc005.htm", "abc010.htm", "xbc007.htm"
        // expect "abc001.ht", "abc002.ht", "abc003.ht", "abc004.ht", "abc005.ht", "abc010.ht", "xbc007.htm"

        // Arrange
        DosTestFixture fixture = new(_mountPoint);

        // Clean up any existing files from previous test runs
        foreach (string pattern in new[] { "abc*.htm", "abc*.ht", "xbc*.htm" }) {
            foreach (string file in Directory.GetFiles(_mountPoint, pattern)) {
                File.Delete(file);
            }
        }

        string[] sourceFiles = { "abc001.htm", "abc002.htm", "abc003.htm", "abc004.htm", "abc005.htm", "abc010.htm", "xbc007.htm" };

        foreach (string file in sourceFiles) {
            File.WriteAllText(Path.Join(_mountPoint, file), "test");
        }

        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "ABC?????";
        fcb.FileExtension = "HTM";

        WriteSpacePaddedField(fixture.Memory, fcbAddr + 17, "????????", 8);
        WriteSpacePaddedField(fixture.Memory, fcbAddr + 25, "?? ", 3);

        try {
            // Act
            FcbStatus status = fixture.DosFcbManager.RenameFile(fcbAddr);

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
        } finally {
            // Cleanup
            foreach (string file in new[] { "ABC001.HT", "ABC002.HT", "ABC003.HT", "ABC004.HT", "ABC005.HT", "ABC010.HT", "xbc007.htm" }) {
                File.Delete(Path.Join(_mountPoint, file));
            }
        }
    }





    [Fact]
    public void SetRandomRecord_CalculatesCorrectly() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);

        // Set current block and record
        fcb.CurrentBlock = 5;
        fcb.CurrentRecord = 42;
        fcb.RecordSize = 128;

        // Act
        fixture.DosFcbManager.SetRandomRecord(fcbAddr);

        // Assert - random record = (currentBlock * 128) + currentRecord
        const uint expectedRandom = (5u * 128) + 42;
        fcb.RandomRecord.Should().Be(expectedRandom);
    }

    [Fact]
    public void GetFcb_StandardFcb_ReturnsCorrectly() {
        // Test standard (non-extended) FCB
        DosTestFixture fixture = new(_mountPoint);
        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "TESTFILE";
        fcb.FileExtension = "TXT";

        // Act
        DosFileControlBlock result = fixture.DosFcbManager.GetFcb(fcbAddr, out byte attr);

        // Assert
        result.BaseAddress.Should().Be(fcbAddr);
        attr.Should().Be(0);
        result.FileName.Should().Be("TESTFILE");
    }

    [Fact]
    public void GetFcb_ExtendedFcb_ReturnsAttributeAndEmbeddedFcb() {
        // Test extended FCB with attribute support
        DosTestFixture fixture = new(_mountPoint);
        const uint xfcbAddr = 0x2000;
        DosExtendedFileControlBlock xfcb = new DosExtendedFileControlBlock(fixture.Memory, xfcbAddr);

        // Set extended FCB marker and attribute
        xfcb.Flag = 0xFF;
        xfcb.Attribute = 0x20; // Archive attribute

        // Set FCB fields (xfcb inherits from DosFileControlBlock)
        xfcb.DriveNumber = 0;
        xfcb.FileName = "EXTTEST ";
        xfcb.FileExtension = "DAT";

        // Act
        DosFileControlBlock result = fixture.DosFcbManager.GetFcb(xfcbAddr, out byte attr);

        // Assert
        attr.Should().Be(0x20);
        result.BaseAddress.Should().Be(xfcbAddr + DosExtendedFileControlBlock.HeaderSize); // Points to embedded FCB
        result.FileName.Should().Be("EXTTEST ");
        result.FileExtension.Should().Be("DAT");
    }

    /// <summary>
    /// Tests RandomBlockRead with multiple records to verify DTA address advances for each record.
    /// </summary>
    [Fact]
    public void RandomBlockRead_MultipleRecords_AdvancesDtaForEachRecord() {
        // Arrange: Create test file with multiple distinct records
        DosTestFixture fixture = new(_mountPoint);
        string testFile = Path.Join(_mountPoint, "MULTIREC.DAT");

        // Write 3 records: "AAAA", "BBBB", "CCCC" (4 bytes each)
        byte[] fileData = new byte[] {
            0x41, 0x41, 0x41, 0x41,  // Record 0: AAAA
            0x42, 0x42, 0x42, 0x42,  // Record 1: BBBB  
            0x43, 0x43, 0x43, 0x43   // Record 2: CCCC
        };
        File.WriteAllBytes(testFile, fileData);

        // Setup FCB
        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0; // Default drive
        fcb.FileName = "MULTIREC";
        fcb.FileExtension = "DAT";

        // Open file
        FcbStatus openResult = fixture.DosFcbManager.OpenFile(fcbAddr);
        openResult.Should().Be(FcbStatus.Success);

        // Set RecordSize AFTER OpenFile because OpenFile always resets RecordSize to 128
        fcb.RecordSize = 4;  // 4-byte records
        fcb.RandomRecord = 0; // Start at record 0

        // Setup DTA buffer
        const uint dtaAddress = 0x3000;
        ushort recordCount = 3;

        // Act: Read 3 records
        FcbStatus readResult = fixture.DosFcbManager.RandomBlockRead(fcbAddr, dtaAddress, ref recordCount);

        // Assert: Should have read all 3 records
        readResult.Should().Be(FcbStatus.Success);
        recordCount.Should().Be(3);

        // Verify each record was written to consecutive DTA locations
        // Record 0 at DTA+0: AAAA
        fixture.Memory.UInt8[dtaAddress + 0].Should().Be(0x41);
        fixture.Memory.UInt8[dtaAddress + 1].Should().Be(0x41);
        fixture.Memory.UInt8[dtaAddress + 2].Should().Be(0x41);
        fixture.Memory.UInt8[dtaAddress + 3].Should().Be(0x41);

        // Record 1 at DTA+4: BBBB (DTA advanced by record size)
        fixture.Memory.UInt8[dtaAddress + 4].Should().Be(0x42);
        fixture.Memory.UInt8[dtaAddress + 5].Should().Be(0x42);
        fixture.Memory.UInt8[dtaAddress + 6].Should().Be(0x42);
        fixture.Memory.UInt8[dtaAddress + 7].Should().Be(0x42);

        // Record 2 at DTA+8: CCCC (DTA advanced again)
        fixture.Memory.UInt8[dtaAddress + 8].Should().Be(0x43);
        fixture.Memory.UInt8[dtaAddress + 9].Should().Be(0x43);
        fixture.Memory.UInt8[dtaAddress + 10].Should().Be(0x43);
        fixture.Memory.UInt8[dtaAddress + 11].Should().Be(0x43);

        // Cleanup
        fixture.DosFcbManager.CloseFile(fcbAddr);
    }

    /// <summary>
    /// Tests RandomBlockWrite with CX=0 to verify file is truncated to the position specified by RandomRecord.
    /// </summary>
    [Fact]
    public void RandomBlockWrite_ZeroRecords_TruncatesFileExplicitly() {
        // Arrange: Create file with initial content
        DosTestFixture fixture = new(_mountPoint);
        string testFile = Path.Join(_mountPoint, "TRUNCATE.DAT");

        // Write 100 bytes initially
        byte[] initialData = new byte[100];
        for (int i = 0; i < 100; i++) {
            initialData[i] = (byte)(i & 0xFF);
        }
        File.WriteAllBytes(testFile, initialData);

        // Setup FCB
        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "TRUNCATE";
        fcb.FileExtension = "DAT";

        // Open file
        FcbStatus openResult = fixture.DosFcbManager.OpenFile(fcbAddr);
        openResult.Should().Be(FcbStatus.Success);

        // Set RecordSize and RandomRecord AFTER OpenFile because OpenFile always resets RecordSize to 128
        fcb.RecordSize = 10;  // 10-byte records
        fcb.RandomRecord = 5;  // Truncate to record 5 = 50 bytes

        const uint dtaAddress = 0x3000;
        ushort recordCount = 0; // CX=0 means truncate

        // Act: Write 0 records (truncate operation)
        FcbStatus writeResult = fixture.DosFcbManager.RandomBlockWrite(fcbAddr, dtaAddress, ref recordCount);

        // Assert: Operation should succeed
        writeResult.Should().Be(FcbStatus.Success);
        recordCount.Should().Be(0); // Should remain 0

        // Verify file size in FCB was updated
        fcb.FileSize.Should().Be(50); // 5 records * 10 bytes

        // Close and verify actual file size on disk
        fixture.DosFcbManager.CloseFile(fcbAddr);
        FileInfo fileInfo = new FileInfo(testFile);
        fileInfo.Length.Should().Be(50); // File physically truncated to 50 bytes
    }

    /// <summary>
    /// Tests GetFileSize with ceiling division.
    /// </summary>
    [Fact]
    public void GetFileSize_UsesCeilingDivision() {
        // Arrange: Create file with size that isn't evenly divisible by record size
        DosTestFixture fixture = new(_mountPoint);
        string testFile = Path.Join(_mountPoint, "SIZETEST.DAT");

        // Write 1000 bytes
        byte[] fileData = new byte[1000];
        File.WriteAllBytes(testFile, fileData);

        // Setup FCB with 128-byte record size
        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "SIZETEST";
        fcb.FileExtension = "DAT";
        fcb.RecordSize = 128;

        // Act: Get file size
        FcbStatus result = fixture.DosFcbManager.GetFileSize(fcbAddr);

        // Assert: Should succeed
        result.Should().Be(FcbStatus.Success);

        // Ceiling division: 1000 / 128 = 7, then 1000 % 128 != 0, so 7+1 = 8
        fcb.RandomRecord.Should().Be(8, "Ceiling division: 1000/128=7, 1000%%128!=0 so 7+1=8");
    }

    /// <summary>
    /// Tests GetFileSize with record size of zero, which should use default 128 bytes.
    /// </summary>
    [Fact]
    public void GetFileSize_RecordSizeZero_UsesDefault128() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        string testFile = Path.Join(_mountPoint, "DEFAULT.DAT");

        // Write 256 bytes (exactly 2 default records)
        byte[] fileData = new byte[256];
        File.WriteAllBytes(testFile, fileData);

        // Setup FCB with RecordSize = 0 (should use default)
        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "DEFAULT ";
        fcb.FileExtension = "DAT";
        fcb.RecordSize = 0; // Zero means use default

        // Act
        FcbStatus result = fixture.DosFcbManager.GetFileSize(fcbAddr);

        // Assert
        result.Should().Be(FcbStatus.Success);
        // 256 / 128 (default) = 2 records
        fcb.RandomRecord.Should().Be(2, "256 bytes / 128 (default) = 2 records");
    }

    /// <summary>
    /// Tests OpenFile with non-existent file returns error.
    /// Error path coverage.
    /// </summary>
    [Fact]
    public void OpenFile_NonExistentFile_ReturnsError() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "NOTFOUND";
        fcb.FileExtension = "DAT";

        // Act
        FcbStatus result = fixture.DosFcbManager.OpenFile(fcbAddr);

        // Assert: Should fail
        result.Should().Be(FcbStatus.Error);
    }

    /// <summary>
    /// Tests GetFileSize with non-existent file returns error.
    /// Error path coverage.
    /// </summary>
    [Fact]
    public void GetFileSize_NonExistentFile_ReturnsError() {
        // Arrange
        DosTestFixture fixture = new(_mountPoint);
        const uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "MISSING ";
        fcb.FileExtension = "TXT";
        fcb.RecordSize = 512;

        // Act
        FcbStatus result = fixture.DosFcbManager.GetFileSize(fcbAddr);

        // Assert
        result.Should().Be(FcbStatus.Error);
    }
}
