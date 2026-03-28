namespace Spice86.Tests.Bios;

using FluentAssertions;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

using System;
using System.IO;

using Xunit;

/// <summary>
/// Integration tests for VGA BIOS INT 10h scroll functions.
/// Verifies that scroll-up clears the full width of the vacated row.
/// </summary>
public class VgaScrollIntegrationTests : IDisposable {
    private readonly string _tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());

    public VgaScrollIntegrationTests() {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() {
        Directory.Delete(_tempDir, true);
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
        IMemory memory = RunComProgram("scroll_up_clears_full_row.com");

        // Assert – copied rows
        ReadVideoCell(memory, 0, 0).Should().Be(('B', (byte)0x1F),
            "row 0 col 0 should have 'B' scrolled up from row 1");
        ReadVideoCell(memory, 0, 79).Should().Be(('B', (byte)0x1F),
            "row 0 col 79 (rightmost) should have 'B' scrolled up from row 1");

        ReadVideoCell(memory, 1, 0).Should().Be(('C', (byte)0x1F),
            "row 1 col 0 should have 'C' scrolled up from row 2");
        ReadVideoCell(memory, 1, 79).Should().Be(('C', (byte)0x1F),
            "row 1 col 79 (rightmost) should have 'C' scrolled up from row 2");

        // Assert – cleared row (every column, not just the first half)
        ReadVideoCell(memory, 2, 0).Should().Be((' ', (byte)0x07),
            "row 2 col 0 should be cleared to space+0x07");
        ReadVideoCell(memory, 2, 40).Should().Be((' ', (byte)0x07),
            "row 2 col 40 (middle) should be cleared to space+0x07");
        ReadVideoCell(memory, 2, 79).Should().Be((' ', (byte)0x07),
            "row 2 col 79 (rightmost) should be cleared to space+0x07");
    }

    private IMemory RunComProgram(string resourceName) {
        string comPath = Path.Join(_tempDir, resourceName);
        File.Copy(
            Path.GetFullPath(Path.Join("Resources", "VgaTests", resourceName)),
            comPath);

        Spice86DependencyInjection spice86 = new Spice86Creator(
            binName: comPath,
            enablePit: true,
            maxCycles: 300000,
            installInterruptVectors: true,
            cDrive: _tempDir).Create();

        spice86.ProgramExecutor.Run();
        IMemory memory = spice86.Machine.Memory;
        spice86.Dispose();
        return memory;
    }

    private static (char Character, byte Attribute) ReadVideoCell(IMemory memory, int row, int col) {
        uint address = MemoryUtils.ToPhysicalAddress(0xB800, (ushort)((row * 80 + col) * 2));
        byte character = memory.UInt8[address];
        byte attribute = memory.UInt8[address + 1];
        return ((char)character, attribute);
    }
}
