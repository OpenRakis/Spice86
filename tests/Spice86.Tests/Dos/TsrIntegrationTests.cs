namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using System;
using System.IO;

using Xunit;

public class TsrIntegrationTests {
    [Fact]
    public void TerminateAndStayResident_WithDynamicMemoryAllocationAndPspCreation_Succeeds() {
        RunTsrTest("tsr22h.com", expectedExitCode: 0);
    }

    [Fact]
    public void TerminateAndStayResident_BasicTermination_Succeeds() {
        RunTsrTest("tsr_basic.com", expectedExitCode: 0);
    }

    [Fact]
    public void TerminateAndStayResident_WithZeroParagraphs_AcceptsValue() {
        RunTsrTest("tsr_zero_paragraphs.com", expectedExitCode: 0);
    }

    [Fact]
    public void TerminateAndStayResident_WithValidParagraphs_KeepsRequestedSize() {
        RunTsrTest("tsr_valid_paragraphs.com", expectedExitCode: 0);
    }

    [Fact]
    public void TerminateAndStayResident_WithReturnCode_PassesCodeCorrectly() {
        RunTsrTest("tsr_return_code.com", expectedExitCode: 66);
    }

    [Fact]
    public void TerminateAndStayResident_WithLargeParagraphCount_HandlesGracefully() {
        RunTsrTest("tsr_large_paragraphs.com", expectedExitCode: 0);
    }

    [Fact]
    public void TsrInterruptVectorTest() {
        RunTsrTest("tsr_interrupt_vector.com", expectedExitCode: 0);
    }

    [Fact]
    public void TerminateAndStayResident_BatchContinuesAfterTsr_NextCommandExecutes() {
        BatchTestHelpers.WithTempDirectory("tsr_batch", tempDir => {
            // Arrange: copy a TSR COM from resources and create a video writer COM
            string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", "DosTsrTests");
            File.Copy(Path.Join(resourceDir, "tsr_basic.com"), Path.Join(tempDir, "TSR_BASIC.COM"));
            BatchTestHelpers.CreateBinaryFile(tempDir, "WRITER.COM", BatchTestHelpers.BuildVideoWriterCom('Z', 0));

            // Act & Assert: batch should continue to WRITER.COM after TSR terminates
            BatchTestHelpers.RunAndAssertVideoCellFromScript(tempDir, "TSR_BASIC.COM\r\nWRITER.COM\r\n", 'Z');
        });
    }

    /// <summary>
    /// Reproduces the Maupiti Island bug: the game reads the parent (COMMAND.COM) PSP[0x02]
    /// ("first segment beyond parent's allocation") and uses it directly as the TSR paragraph
    /// count (DX). In Spice86 the fake COMMAND.COM PSP had CurrentSize set to
    /// LastFreeSegment (0x9FFF), so DX became 0x9FFF — exhausting all conventional memory.
    /// After the fix, COMMAND.COM PSP[0x02] = CommandComSegment + PspSizeInParagraphs = 0x70,
    /// so DX = 0x70, and nearly all of conventional memory remains free afterwards.
    /// </summary>
    [Fact]
    public void TerminateAndStayResident_UsingParentPspCurrentSize_LeavesConventionalMemoryFree() {
        // Arrange
        const ushort MinExpectedFreeParagraphs = 0x8000; // at least 512 KB must remain free
        Spice86DependencyInjection spice86 = CreateSpice86ForTsrTest("tsr_parent_psp_size.com");

        // Act
        spice86.ProgramExecutor.Run();

        // Assert
        Dos? dos = spice86.McpServices.Dos;
        if (dos is null) {
            throw new InvalidOperationException("DOS subsystem was not initialised");
        }
        DosMemoryControlBlock largestFree = dos.MemoryManager.FindLargestFree();
        largestFree.IsFree.Should().BeTrue(
            "after a TSR that uses parent PSP[0x02] as DX, conventional memory must have a large free block");
        largestFree.Size.Should().BeGreaterThan(MinExpectedFreeParagraphs,
            "after TSR the bulk of conventional memory must still be free;" +
            " if only 2 paragraphs (the env block) are free the bug is still present");
    }

    private static Spice86DependencyInjection CreateSpice86ForTsrTest(string testFileName) {
        string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", "DosTsrTests");
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_tsr_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string target = Path.Join(tempDir, testFileName);
        File.Copy(Path.Join(resourceDir, testFileName), target, overwrite: true);
        return new Spice86Creator(
            binName: target,
            enablePit: false,
            maxCycles: 200000L,
            installInterruptVectors: true,
            enableA20Gate: true
        ).Create();
    }

    private static void RunTsrTest(string testFileName, byte expectedExitCode) {
        // Arrange
        Spice86DependencyInjection spice86 = CreateSpice86ForTsrTest(testFileName);

        // Act
        spice86.ProgramExecutor.Run();

        // Assert
        spice86.Machine.CpuState.AX.Should().Be(expectedExitCode);
    }
}
