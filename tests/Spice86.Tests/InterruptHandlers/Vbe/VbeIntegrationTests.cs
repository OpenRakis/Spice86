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
    /// Runs a VBE test program and returns a test handler with results.
    /// </summary>
    /// <param name="fileName">Binary file name in Resources/vbeTests/</param>
    private VbeTestHandler RunVbeTest(string fileName) {
        // Get full path to the binary file
        string filePath = Path.GetFullPath(Path.Combine("Resources", "vbeTests", fileName));

        // Setup emulator
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enableCfgCpu: true,
            enablePit: true,
            recordData: false,
            maxCycles: 100000L,
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
    }
}
