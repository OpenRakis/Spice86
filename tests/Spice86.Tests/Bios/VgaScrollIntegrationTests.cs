namespace Spice86.Tests.Bios;

using FluentAssertions;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;
using Spice86.Tests.Utility;

using Xunit;

/// <summary>
/// Integration tests for VGA BIOS INT 10h scroll functions.
/// Verifies that scroll-up clears the full width of the vacated row.
/// </summary>
public class VgaScrollIntegrationTests : IDisposable {
    private readonly TempFile _tempFile = new("VgaScrollIntegrationTests");

    public void Dispose() {
        _tempFile.Dispose();
    }

    /// <summary>
    /// Tests that INT 10h AH=06h (scroll up) clears every column of the
    /// newly blanked row, not just the first half.
    ///
    /// The COM program fills rows 0-2 with 'A', 'B', 'C' (attr 0x1F)
    /// via INT 10h AH=09h, then scrolls the window up 1 line via AH=06h
    /// with blank attribute 0x07.
    /// </summary>
    [Fact]
    public void ScrollUp_ShouldClearEntireBottomRow() {
        // Arrange
        VideoMemorySnapshot videoMemory = RunComProgram("scroll_up_clears_full_row.com");

        // Assert – copied rows
        videoMemory.Row0Col0.Should().Be(('B', (byte)0x1F),
            "row 0 col 0 should have 'B' scrolled up from row 1");
        videoMemory.Row0Col79.Should().Be(('B', (byte)0x1F),
            "row 0 col 79 (rightmost) should have 'B' scrolled up from row 1");

        videoMemory.Row1Col0.Should().Be(('C', (byte)0x1F),
            "row 1 col 0 should have 'C' scrolled up from row 2");
        videoMemory.Row1Col79.Should().Be(('C', (byte)0x1F),
            "row 1 col 79 (rightmost) should have 'C' scrolled up from row 2");

        // Assert – cleared row (every column, not just the first half)
        videoMemory.Row2Col0.Should().Be((' ', (byte)0x07),
            "row 2 col 0 should be cleared to space+0x07");
        videoMemory.Row2Col40.Should().Be((' ', (byte)0x07),
            "row 2 col 40 (middle) should be cleared to space+0x07");
        videoMemory.Row2Col79.Should().Be((' ', (byte)0x07),
            "row 2 col 79 (rightmost) should be cleared to space+0x07");
    }

    private VideoMemorySnapshot RunComProgram(string resourceName) {
        string comPath = Path.Join(_tempFile.Path, resourceName);
        File.Copy(
            Path.Join(AppContext.BaseDirectory, "Resources", "VgaTests", resourceName),
            comPath);

        using Spice86Creator creator = new Spice86Creator(
            binName: comPath,
            enablePit: true,
            maxCycles: 300000,
            installInterruptVectors: true,
            cDrive: _tempFile.Path);
        using Spice86DependencyInjection spice86 = creator.Create();

        spice86.ProgramExecutor.Run();
        IMemory memory = spice86.Machine.Memory;
        return new VideoMemorySnapshot(
            ReadVideoCell(memory, 0, 0),
            ReadVideoCell(memory, 0, 79),
            ReadVideoCell(memory, 1, 0),
            ReadVideoCell(memory, 1, 79),
            ReadVideoCell(memory, 2, 0),
            ReadVideoCell(memory, 2, 40),
            ReadVideoCell(memory, 2, 79));
    }

    private static (char Character, byte Attribute) ReadVideoCell(IMemory memory, int row, int col) {
        uint address = MemoryUtils.ToPhysicalAddress(0xB800, (ushort)((row * 80 + col) * 2));
        byte character = memory.UInt8[address];
        byte attribute = memory.UInt8[address + 1];
        return ((char)character, attribute);
    }

    private sealed record VideoMemorySnapshot(
        (char Character, byte Attribute) Row0Col0,
        (char Character, byte Attribute) Row0Col79,
        (char Character, byte Attribute) Row1Col0,
        (char Character, byte Attribute) Row1Col79,
        (char Character, byte Attribute) Row2Col0,
        (char Character, byte Attribute) Row2Col40,
        (char Character, byte Attribute) Row2Col79);
}
