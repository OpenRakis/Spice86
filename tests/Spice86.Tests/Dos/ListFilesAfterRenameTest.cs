namespace Spice86.Tests.Dos;

using Xunit;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.Memory.Indexable;
using FluentAssertions;

public class ListFilesAfterRenameTest : IDisposable {
    private readonly string _mountPoint;
    
    public ListFilesAfterRenameTest() {
        _mountPoint = Path.Combine(Path.GetTempPath(), $"Spice86Test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_mountPoint);
    }

    public void Dispose() {
        if (Directory.Exists(_mountPoint)) {
            Directory.Delete(_mountPoint, true);
        }
    }

    [Fact]
    public void Debug_ListAfterRename() {
        // Create 3 simple test files
        File.WriteAllText(Path.Combine(_mountPoint, "one.in"), "test");
        File.WriteAllText(Path.Combine(_mountPoint, "two.in"), "test");
        File.WriteAllText(Path.Combine(_mountPoint, "three.in"), "test");
        
        // Setup DOS environment
        DosTestFixture fixture = new(_mountPoint);
        
        uint fcbAddr = 0x2000;
        DosFileControlBlock fcb = new DosFileControlBlock(fixture.Memory, fcbAddr);
        fcb.DriveNumber = 0;
        fcb.FileName = "????????";
        fcb.FileExtension = "IN ";

        // Rename *.in to *.out
        WriteSpacePaddedField(fixture.Memory, fcbAddr + 12, "????????", 8);
        WriteSpacePaddedField(fixture.Memory, fcbAddr + 20, "OUT", 3);

        // Act
        FcbStatus status = fixture.DosFcbManager.RenameFile(fcbAddr);

        // List all files that exist
        string[] allFiles = Directory.GetFiles(_mountPoint);
        string fileList = string.Join(", ", allFiles.Select(Path.GetFileName));
        
        // Output for debugging
        Console.WriteLine($"Status: {status}");
        Console.WriteLine($"Files found: {fileList}");
        Console.WriteLine($"File count: {allFiles.Length}");
        
        // Basic assertion
        status.Should().Be(FcbStatus.Success, $"Rename should succeed. Files found: {fileList}");
        allFiles.Length.Should().Be(3, $"Should have 3 files. Found: {fileList}");
    }
    
    private static void WriteSpacePaddedField(IIndexable memory, uint address, string value, int fieldSize) {
        string padded = value.PadRight(fieldSize).Substring(0, fieldSize);
        for (int i = 0; i < fieldSize; i++) {
            memory.UInt8[address + (uint)i] = (byte)padded[i];
        }
    }
}
