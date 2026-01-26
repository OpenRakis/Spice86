namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Utils;

using System;
using System.IO;

using Xunit;

/// <summary>
/// Integration tests for DOS INT 21h, AH=4Ah (Resize Memory Block) functionality.
/// Tests verify that memory reallocation works correctly with various padding sizes.
/// Monitors writes to video memory and throws if fail character ('F') is detected.
/// </summary>
public class DosReallocIntegrationTests {
    private const byte FailCharacter = (byte)'F';

    [Theory]
    [InlineData("realloc_test_pad0.com")]
    [InlineData("realloc_test_pad1.com")]
    [InlineData("realloc_test_pad13.com")]
    [InlineData("realloc_test_pad15.com")]
    [InlineData("realloc_test_pad16.com")]
    public void DosRealloc_ShouldSucceedWithVariousPaddingSizes(string fileName) {
        RunReallocTest(fileName);
    }

    [Fact]
    public void DosRealloc_WithZeroPadding_ShouldSucceed() {
        RunReallocTest("realloc_test_pad0.com");
    }

    [Fact]
    public void DosRealloc_WithSubParagraphPadding_ShouldSucceed() {
        RunReallocTest("realloc_test_pad1.com");
    }

    [Fact]
    public void DosRealloc_With13BytePadding_ShouldSucceed() {
        RunReallocTest("realloc_test_pad13.com");
    }

    [Fact]
    public void DosRealloc_With15BytePadding_ShouldSucceed() {
        RunReallocTest("realloc_test_pad15.com");
    }

    [Fact]
    public void DosRealloc_WithExactParagraphPadding_ShouldSucceed() {
        RunReallocTest("realloc_test_pad16.com");
    }

    /// <summary>
    /// Runs a realloc test program and throws if the fail character is written to video memory.
    /// Uses a MEMORY_WRITE breakpoint at the video memory area (0xB800:0000).
    /// Test programs write 'P' for success or 'F' for failure.
    /// </summary>
    /// <param name="fileName">The .COM file to execute</param>
    /// <exception cref="InvalidOperationException">Thrown if fail character is detected in video memory</exception>
    private static void RunReallocTest(string fileName) {
        string resourceDir = Path.Join(AppContext.BaseDirectory, "Resources", "DosReallocTest");
        string programPath = Path.Join(resourceDir, fileName);

        if (!File.Exists(programPath)) {
            throw new FileNotFoundException($"Test file not found: {programPath}");
        }

        Spice86DependencyInjection spice86 = new Spice86Creator(
            binName: programPath,
            enablePit: false,
            recordData: false,
            maxCycles: 50000,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: false,
            enableEms: false
        ).Create();

        // Monitor video memory writes at 0xB800:0000 for fail character
        uint videoBase = MemoryUtils.ToPhysicalAddress(0xB800, 0);
        spice86.Machine.EmulatorBreakpointsManager.ToggleBreakPoint(
            new Core.Emulator.VM.Breakpoint.AddressBreakPoint(BreakPointType.MEMORY_WRITE,
                videoBase,
                _ => {
                    // video memory should reflect that the test was a success
                    if (spice86.Machine.CpuState.AL == FailCharacter) {
                        throw new InvalidOperationException("DOS INT 21h AH=4Ah (realloc) failed: 'F' written to video memory");
                    }
                },
                true),
            true);

        spice86.Machine.EmulatorBreakpointsManager.ToggleBreakPoint(
           new Core.Emulator.VM.Breakpoint.AddressBreakPoint(BreakPointType.MACHINE_STOP,
                0,  // Address is ignored for MACHINE_STOP
                _ => {
                    // The test programs call INT 21h AH=4Ah, then checks CF
                    // When the program has exited, CF reflects whether the realloc succeeded
                    if (spice86.Machine.CpuState.AL == FailCharacter) {
                        throw new InvalidOperationException("DOS INT 21h AH=4Ah (realloc) failed signaled error with carry flaag");
                    }
                },
            true),
            true);

        spice86.ProgramExecutor.Run();
    }
}
