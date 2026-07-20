namespace Spice86.Tests.Bios;

using FluentAssertions;
using Microsoft.Extensions.Logging;

using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;

using Xunit;

/// <summary>
/// Integration tests for RTC/CMOS and time services that run real assembly code
/// through the emulation stack. These tests verify behavior as a real DOS program
/// would experience it, including CMOS port access, BIOS INT 1A, and DOS INT 21H.
/// </summary>
public class RtcIntegrationTests {
    enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests direct CMOS/RTC port access (ports 0x70 and 0x71).
    /// Verifies that time and date registers return valid BCD values.
    /// </summary>
    [Fact]
    public void CmosDirectPortAccess_ShouldReturnValidBcdValues() {
        // This test runs cmos_ports.asm which directly accesses CMOS registers
        // and validates that they contain proper BCD-encoded time/date values
        TestIoPortHandler testHandler = RunRtcTest("cmos_ports.com");

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "CMOS registers should return valid BCD time/date values");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);

        // All 7 tests should have passed (last test writes 0x07 to details port)
        testHandler.Details.Should().Contain(0x07, "All 7 tests should have completed");
    }

    /// <summary>
    /// Tests BIOS INT 1A time services (functions 00h-05h).
    /// Includes system clock counter and RTC time/date operations.
    /// </summary>
    [Fact]
    public void BiosInt1A_TimeServices_ShouldWork() {
        // This test runs bios_int1a.asm which exercises all INT 1A functions:
        // - 00h: Get System Clock Counter
        // - 01h: Set System Clock Counter
        // - 02h: Read RTC Time
        // - 03h: Set RTC Time (stub in emulator)
        // - 04h: Read RTC Date
        // - 05h: Set RTC Date (stub in emulator)
        TestIoPortHandler testHandler = RunRtcTest("bios_int1a.com");

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "BIOS INT 1A functions should execute successfully");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);

        // All 6 tests should have passed (last test writes 0x06 to details port)
        testHandler.Details.Should().Contain(0x06, "All 6 tests should have completed");
    }

    /// <summary>
    /// Tests DOS INT 21H date and time services (functions 2Ah-2Dh).
    /// Includes both get/set operations and validation of error handling.
    /// </summary>
    [Fact]
    public void DosInt21H_DateTimeServices_ShouldWork() {
        // This test runs dos_int21h.asm which exercises all DOS date/time functions:
        // - 2Ah: Get DOS Date
        // - 2Bh: Set DOS Date (with validation tests)
        // - 2Ch: Get DOS Time
        // - 2Dh: Set DOS Time (with validation tests)
        TestIoPortHandler testHandler = RunRtcTest("dos_int21h.com");

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "DOS INT 21H date/time functions should execute successfully");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);

        // All 11 tests should have passed (last test writes 0x0B to details port)
        testHandler.Details.Should().Contain(0x0B, "All 11 tests should have completed");
    }

    /// <summary>
    /// Tests BIOS INT 15h, AH=83h - Event Wait Interval function.
    /// Verifies setting, detecting active wait, and canceling wait operations.
    /// </summary>
    [Fact]
    public void BiosInt15h_WaitFunction_ShouldWork() {
        // This test runs bios_int15h_83h.asm which exercises INT 15h, AH=83h:
        // - Set a wait event (AL=00h)
        // - Detect already-active wait (should return error AH=80h)
        // - Cancel a wait event (AL=01h)
        // - Set a new wait after canceling (should succeed)
        // - Cancel the second wait
        TestIoPortHandler testHandler = RunRtcTest("bios_int15h_83h.com");

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "BIOS INT 15h, AH=83h WAIT function should execute successfully");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);

        // All 5 tests should have passed (last test writes 0x05 to details port)
        testHandler.Details.Should().Contain(0x05, "All 5 tests should have completed");
    }

    /// <summary>
    /// Tests BIOS INT 15h, AH=83h wait setup and INT 70h RTC configuration.
    /// Verifies that the wait function properly configures the RTC periodic interrupt.
    /// </summary>
    [Fact]
    public void BiosInt15h_83h_ShouldConfigureRtcProperly() {
        // This test runs bios_int70_wait.asm which verifies INT 15h, AH=83h:
        // - Sets up a wait with user flag address and timeout
        // - Enables RTC periodic interrupt (bit 6 of Status Register B)
        // - Stores wait timeout in BIOS data area
        // - Canceling the wait disables the periodic interrupt
        // - Wait flag is properly managed in BIOS data area
        TestIoPortHandler testHandler = RunRtcTest("bios_int70_wait.com", maxCycles: 500000L);

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "BIOS INT 15h, AH=83h should configure RTC periodic interrupt correctly");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);

        // All 7 tests should have passed (last test writes 0x07 to details port)
        testHandler.Details.Should().Contain(0x07, "All 7 tests should have completed");
    }

    /// <summary>
    /// Runs an RTC test program and returns a test handler with results.
    /// </summary>
    private TestIoPortHandler RunRtcTest(string comFileName, long maxCycles = 100000L) {
        string resourcePath = Path.Join(AppContext.BaseDirectory, "Resources", "RtcTests", comFileName);
        string cDrive = Path.GetDirectoryName(resourcePath) ?? AppContext.BaseDirectory;

        using Spice86Creator creator = new Spice86Creator(
            binName: resourcePath,
            enablePit: true,
            maxCycles: maxCycles,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: false,
            enableEms: false,
            cDrive: cDrive
        );
        using Spice86DependencyInjection spice86DependencyInjection = creator.Create();

        TestIoPortHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

}
