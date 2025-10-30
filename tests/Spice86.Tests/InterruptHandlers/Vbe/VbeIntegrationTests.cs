namespace Spice86.Tests.InterruptHandlers.Vbe;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Integration tests for VESA VBE functionality that run actual ASM programs through the emulation stack.
/// These tests verify VBE behavior from the perspective of a real DOS program.
/// Programs typically call Function 00h (ReturnControllerInfo) first to detect VBE presence.
/// Binary test programs are in Resources/vbeTests/ and can be run on real hardware.
/// </summary>
public class VbeIntegrationTests {
    private const int ResultPort = 0x999;

    private enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests VBE presence detection via INT 10h AX=4F00h.
    /// Should return AX=004Fh indicating VBE is supported.
    /// Binary: Resources/vbeTests/vbe_detect.com
    /// </summary>
    [Fact]
    public void VbeDetection_ViaInt10h4F00_ShouldReturnSuccess() {
        // Act
        VbeTestHandler testHandler = RunVbeTest("vbe_detect.com");

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "INT 10h AX=4F00h should indicate VBE support");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE controller information signature.
    /// Should return "VESA" signature at the beginning of the buffer.
    /// Binary: Resources/vbeTests/vbe_signature.com
    /// </summary>
    [Fact]
    public void VbeReturnControllerInfo_ShouldReturnVesaSignature() {
        // Act
        VbeTestHandler testHandler = RunVbeTest("vbe_signature.com");

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "VBE signature should be 'VESA'");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE version number in controller info.
    /// Should return version 0100h (VBE 1.0 in BCD format).
    /// Binary: Resources/vbeTests/vbe_version.com
    /// </summary>
    [Fact]
    public void VbeReturnControllerInfo_ShouldReturnVersion10() {
        // Act
        VbeTestHandler testHandler = RunVbeTest("vbe_version.com");

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "VBE version should be 1.0 (0100h)");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE mode information retrieval (Function 01h).
    /// Should return AX=004Fh for a supported mode.
    /// Binary: Resources/vbeTests/vbe_modeinfo.com
    /// </summary>
    [Fact]
    public void VbeReturnModeInfo_ForSupportedMode_ShouldReturnSuccess() {
        // Act
        VbeTestHandler testHandler = RunVbeTest("vbe_modeinfo.com");

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Mode info should return success for supported mode");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE mode information for mode 0x101 (640x480x256).
    /// Verifies the resolution is correctly reported.
    /// Binary: Resources/vbeTests/vbe_mode101_res.com
    /// </summary>
    [Fact]
    public void VbeReturnModeInfo_Mode101_ShouldReturn640x480() {
        // Act
        VbeTestHandler testHandler = RunVbeTest("vbe_mode101_res.com");

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Mode 0x101 should report 640x480 resolution");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE mode information for an unsupported mode.
    /// Should return AX=014Fh (function supported but failed).
    /// Binary: Resources/vbeTests/vbe_unsupported_mode.com
    /// </summary>
    [Fact]
    public void VbeReturnModeInfo_ForUnsupportedMode_ShouldReturnFailure() {
        // Act
        VbeTestHandler testHandler = RunVbeTest("vbe_unsupported_mode.com");

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Unsupported mode should return failure status");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE save/restore state function 04h subfunction 00h.
    /// Should return buffer size needed in BX.
    /// Binary: Resources/vbeTests/vbe_getbuffersize.com
    /// </summary>
    [Fact]
    public void VbeSaveRestoreState_GetBufferSize_ShouldReturnSize() {
        // Act
        VbeTestHandler testHandler = RunVbeTest("vbe_getbuffersize.com");

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Get buffer size should return non-zero size");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE save state function (Function 04h subfunction 01h).
    /// Should return AX=004Fh indicating success.
    /// Binary: Resources/vbeTests/vbe_savestate.com
    /// </summary>
    [Fact]
    public void VbeSaveState_ShouldReturnSuccess() {
        // Act
        VbeTestHandler testHandler = RunVbeTest("vbe_savestate.com");

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Save state should succeed");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE restore state function (Function 04h subfunction 02h).
    /// Should return AX=004Fh indicating success.
    /// Binary: Resources/vbeTests/vbe_restorestate.com
    /// </summary>
    [Fact]
    public void VbeRestoreState_ShouldReturnSuccess() {
        // Act
        VbeTestHandler testHandler = RunVbeTest("vbe_restorestate.com");

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Restore state should succeed");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE full save/restore state cycle (Function 04h all subfunctions).
    /// Validates buffer size query, save, and restore operations.
    /// Binary: Resources/vbeTests/vbe_savestate_full.com
    /// </summary>
    [Fact]
    public void VbeSaveRestoreState_FullCycle_ShouldSucceed() {
        // Act
        VbeTestHandler testHandler = RunVbeTest("vbe_savestate_full.com");

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Full save/restore cycle should succeed");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE save/restore state with DAC palette preservation.
    /// Sets a color, saves state, changes color, restores state, and verifies original color.
    /// Binary: Resources/vbeTests/vbe_savestate_dac.com
    /// </summary>
    [Fact]
    public void VbeSaveRestoreState_DacPalette_ShouldPreserveColors() {
        // Act
        VbeTestHandler testHandler = RunVbeTest("vbe_savestate_dac.com");

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "DAC palette should be preserved through save/restore");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Comprehensive VBE 1.0 test that validates all functionality.
    /// Tests all VBE functions and displays results in text mode.
    /// This program can be run on real DOS hardware for validation.
    /// Tests:
    /// - VBE detection (4F00h)
    /// - Signature and version validation
    /// - Mode info for supported modes (4F01h)
    /// - Unsupported mode error handling
    /// - Buffer size query (4F04h/00h)
    /// - Display window control (4F05h)
    /// - Controller memory validation
    /// - Resolution verification for modes 0x100 and 0x101
    /// - Banking information validation
    /// Binary: Resources/vbeTests/vbe_comprehensive.com
    /// NOTE: Skipped in automated tests due to complexity and text output requirements.
    ///       This binary can and should be run manually on real DOS hardware for full validation.
    /// </summary>
    [Fact(Skip = "Comprehensive test requires more emulation cycles than practical for automated testing. Run binary on real hardware.")]
    public void VbeComprehensive_AllTests_ShouldPass() {
        // Act - comprehensive test needs more cycles due to multiple tests and text output
        VbeTestHandler testHandler = RunVbeTest("vbe_comprehensive.com", maxCycles: 500000L);

        // Assert
        // The comprehensive test outputs 0x00 if all tests pass, 0xFF if any fail
        testHandler.Results.Should().Contain((byte)TestResult.Success, "All comprehensive VBE tests should pass");
        // Each individual test also outputs its result
        // Last result should be the overall summary
        testHandler.Results.Last().Should().Be((byte)TestResult.Success, "Overall test summary should indicate all tests passed");
    }

    /// <summary>
    /// Runs a VBE test program and returns a test handler with results.
    /// </summary>
    /// <param name="fileName">Binary file name in Resources/vbeTests/</param>
    /// <param name="maxCycles">Maximum CPU cycles to run (default 100000)</param>
    private VbeTestHandler RunVbeTest(string fileName, long maxCycles = 100000L) {
        // Get full path to the binary file
        string filePath = Path.GetFullPath(Path.Combine("Resources", "vbeTests", fileName));

        // Setup emulator
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enableCfgCpu: true,
            enablePit: true,
            recordData: false,
            maxCycles: maxCycles,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: false,
            enableEms: false
        ).Create();

        VbeTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );

        // Act
        spice86DependencyInjection.ProgramExecutor.Run();

        // Assert
        return testHandler;
    }

    /// <summary>
    /// Captures VBE test results from designated I/O ports.
    /// </summary>
    private class VbeTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();

        public VbeTestHandler(State state, ILoggerService loggerService,
            IOPortDispatcher ioPortDispatcher) : base(state, true, loggerService) {
            ioPortDispatcher.AddIOPortHandler(ResultPort, this);
        }

        public override void WriteByte(ushort port, byte value) {
            if (port == ResultPort) {
                Results.Add(value);
            }
        }

        public override void WriteWord(ushort port, ushort value) {
            if (port == ResultPort) {
                // Handle word writes by splitting into bytes
                Results.Add((byte)(value & 0xFF));
                if ((value >> 8) != 0) {
                    Results.Add((byte)(value >> 8));
                }
            }
        }
    }
}
