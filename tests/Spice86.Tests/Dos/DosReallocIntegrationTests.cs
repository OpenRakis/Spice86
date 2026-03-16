namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Shared.Emulator.VM.Breakpoint;

using System;
using System.IO;

using Xunit;

/// <summary>
/// Integration tests for DOS INT 21h, AH=4Ah (Resize Memory Block) functionality.
/// Tests verify that memory reallocation works correctly with various padding sizes.
/// </summary>
public class DosReallocIntegrationTests {
    [Theory]
    [InlineData("realloc_test_pad0.com")]
    [InlineData("realloc_test_pad1.com")]
    [InlineData("realloc_test_pad13.com")]
    [InlineData("realloc_test_pad15.com")]
    [InlineData("realloc_test_pad16.com")]
    public void DosRealloc_ShouldSucceedWithVariousPaddingSizes(string fileName) {
        bool reallocSucceeded = RunReallocTest(fileName);
        reallocSucceeded.Should().BeTrue($"DOS INT 21h AH=4Ah (realloc) should succeed (CF=0) for {fileName}");
    }

    [Fact]
    public void DosRealloc_WithZeroPadding_ShouldSucceed() {
        bool reallocSucceeded = RunReallocTest("realloc_test_pad0.com");
        reallocSucceeded.Should().BeTrue("DOS realloc with 0 byte padding should succeed (CF=0)");
    }

    [Fact]
    public void DosRealloc_WithSubParagraphPadding_ShouldSucceed() {
        bool reallocSucceeded = RunReallocTest("realloc_test_pad1.com");
        reallocSucceeded.Should().BeTrue("DOS realloc with 1 byte padding should succeed (CF=0)");
    }

    [Fact]
    public void DosRealloc_With13BytePadding_ShouldSucceed() {
        bool reallocSucceeded = RunReallocTest("realloc_test_pad13.com");
        reallocSucceeded.Should().BeTrue("DOS realloc with 13 byte padding should succeed (CF=0)");
    }

    [Fact]
    public void DosRealloc_With15BytePadding_ShouldSucceed() {
        bool reallocSucceeded = RunReallocTest("realloc_test_pad15.com");
        reallocSucceeded.Should().BeTrue("DOS realloc with 15 byte padding should succeed (CF=0)");
    }

    [Fact]
    public void DosRealloc_WithExactParagraphPadding_ShouldSucceed() {
        bool reallocSucceeded = RunReallocTest("realloc_test_pad16.com");
        reallocSucceeded.Should().BeTrue("DOS realloc with 16 byte (1 paragraph) padding should succeed (CF=0)");
    }

    /// <summary>
    /// Runs a realloc test program and captures the carry flag state when the machine stops.
    /// Uses a MACHINE_STOP breakpoint which is more reliable than guessing instruction addresses.
    /// </summary>
    /// <param name="fileName">The .COM file to execute</param>
    /// <returns>True if the realloc succeeded (CF=0), false otherwise</returns>
    private static bool RunReallocTest(string fileName) {
        string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", "DosReallocTest");
        string programPath = Path.Join(resourceDir, fileName);

        if (!File.Exists(programPath)) {
            throw new FileNotFoundException($"Test file not found: {programPath}");
        }

        Spice86DependencyInjection spice86 = new Spice86Creator(
            binName: programPath,
            enablePit: false,
            maxCycles: 50000,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: false,
            enableEms: false
        ).Create();

        bool reallocSucceeded = false;

        spice86.Machine.EmulatorBreakpointsManager.ToggleBreakPoint(
            new Core.Emulator.VM.Breakpoint.AddressBreakPoint(BreakPointType.MACHINE_STOP,
                0,  // Address is ignored for MACHINE_STOP
                _ => {
                    // The test programs call INT 21h AH=4Ah, then check CF and HLT
                    // At this point, CF reflects whether the realloc succeeded
                    reallocSucceeded = !spice86.Machine.CpuState.CarryFlag;
                },
                true),
            true);

        spice86.ProgramExecutor.Run();

        return reallocSucceeded;
    }
}
