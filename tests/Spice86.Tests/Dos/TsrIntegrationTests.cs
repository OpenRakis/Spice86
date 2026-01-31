namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Interfaces;

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
        RunTsrTest("tsr_return_code.com", expectedExitCode: 0);
    }

    [Fact]
    public void TerminateAndStayResident_WithLargeParagraphCount_HandlesGracefully() {
        RunTsrTest("tsr_large_paragraphs.com", expectedExitCode: 0);
    }

    [Fact]
    public void TsrInterruptVectorTest() {
        RunTsrTest("tsr_interrupt_vector.com", expectedExitCode: 0);
    }

    private static void RunTsrTest(string testFileName, byte expectedExitCode) {
        string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", "DosTsrIntegration");
        string tempDir = Path.Join(Path.GetTempPath(), $"dos_tsr_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        string source = Path.Join(resourceDir, testFileName);
        string target = Path.Join(tempDir, testFileName);
        File.Copy(source, target, overwrite: true);

        try {
            Spice86DependencyInjection spice86 = new Spice86Creator(
                binName: target,
                enablePit: false,
                recordData: false,
                maxCycles: 200000L,
                installInterruptVectors: true,
                enableA20Gate: true
            ).Create();

            spice86.ProgramExecutor.Run();

            spice86.Machine.CpuState.AX.Should().Be(expectedExitCode);
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    private static void TryDeleteDirectory(string directoryPath) {
        if (!Directory.Exists(directoryPath)) {
            return;
        }
        Directory.Delete(directoryPath, true);
    }
}
