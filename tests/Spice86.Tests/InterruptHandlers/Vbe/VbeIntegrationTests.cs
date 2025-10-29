namespace Spice86.Tests.InterruptHandlers.Vbe;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration tests for VESA VBE functionality that run machine code through the emulation stack.
/// These tests verify VBE behavior from the perspective of a real DOS program.
/// Programs typically call Function 00h (ReturnControllerInfo) first to detect VBE presence.
/// </summary>
public class VbeIntegrationTests {
    private const int ResultPort = 0x999;    // Port to write test results
    private const int DetailsPort = 0x998;   // Port to write test details/error messages

    private enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests VBE presence detection via INT 10h AX=4F00h.
    /// Programs call this function first to detect VBE support.
    /// Should return AX=004Fh indicating VBE is supported.
    /// </summary>
    [Fact]
    public void VbeDetection_ViaInt10h4F00_ShouldReturnSuccess() {
        // Arrange
        byte[] program = new byte[] {
            // Set up buffer pointer at ES:DI
            0xB8, 0x00, 0x20,       // mov ax, 0x2000 - Buffer segment
            0x8E, 0xC0,             // mov es, ax
            0xBF, 0x00, 0x00,       // mov di, 0x0000 - Buffer offset
            // Call INT 10h Function 4F00h (Return Controller Info)
            0xB8, 0x00, 0x4F,       // mov ax, 0x4F00
            0xCD, 0x10,             // int 10h
            // Check if AX = 004Fh (VBE supported and successful)
            0x3D, 0x4F, 0x00,       // cmp ax, 0x004F
            0x74, 0x04,             // je success
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Act
        VbeTestHandler testHandler = RunVbeTest(program);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "INT 10h AX=4F00h should indicate VBE support");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE controller information signature.
    /// Should return "VESA" signature at the beginning of the buffer.
    /// </summary>
    [Fact]
    public void VbeReturnControllerInfo_ShouldReturnVesaSignature() {
        // Arrange
        byte[] program = new byte[] {
            // Set up buffer pointer at ES:DI
            0xB8, 0x00, 0x20,       // mov ax, 0x2000 - Buffer segment
            0x8E, 0xC0,             // mov es, ax
            0xBF, 0x00, 0x00,       // mov di, 0x0000 - Buffer offset
            // Call INT 10h Function 4F00h
            0xB8, 0x00, 0x4F,       // mov ax, 0x4F00
            0xCD, 0x10,             // int 10h
            // Check signature - "VESA" = 56h 45h 53h 41h
            0x26, 0x80, 0x3D, 0x56, // cmp byte [es:di+0], 0x56 ('V')
            0x75, 0x18,             // jne failed
            0x26, 0x80, 0x7D, 0x01, 0x45, // cmp byte [es:di+1], 0x45 ('E')
            0x75, 0x11,             // jne failed
            0x26, 0x80, 0x7D, 0x02, 0x53, // cmp byte [es:di+2], 0x53 ('S')
            0x75, 0x0A,             // jne failed
            0x26, 0x80, 0x7D, 0x03, 0x41, // cmp byte [es:di+3], 0x41 ('A')
            0x75, 0x03,             // jne failed
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Act
        VbeTestHandler testHandler = RunVbeTest(program);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "VBE signature should be 'VESA'");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE version number in controller info.
    /// Should return version 0100h (VBE 1.0 in BCD format).
    /// </summary>
    [Fact]
    public void VbeReturnControllerInfo_ShouldReturnVersion10() {
        // Arrange
        byte[] program = new byte[] {
            // Set up buffer pointer
            0xB8, 0x00, 0x20,       // mov ax, 0x2000
            0x8E, 0xC0,             // mov es, ax
            0xBF, 0x00, 0x00,       // mov di, 0x0000
            // Call INT 10h Function 4F00h
            0xB8, 0x00, 0x4F,       // mov ax, 0x4F00
            0xCD, 0x10,             // int 10h
            // Check version at offset 04h - should be 0100h
            0x26, 0x8B, 0x45, 0x04, // mov ax, [es:di+04h]
            0x3D, 0x00, 0x01,       // cmp ax, 0x0100
            0x74, 0x04,             // je success
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Act
        VbeTestHandler testHandler = RunVbeTest(program);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "VBE version should be 1.0 (0100h)");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE mode information retrieval (Function 01h).
    /// Should return AX=004Fh for a supported mode.
    /// </summary>
    [Fact]
    public void VbeReturnModeInfo_ForSupportedMode_ShouldReturnSuccess() {
        // Arrange - Test mode 0x100 (640x400x256)
        byte[] program = new byte[] {
            // Set up buffer pointer
            0xB8, 0x00, 0x20,       // mov ax, 0x2000
            0x8E, 0xC0,             // mov es, ax
            0xBF, 0x00, 0x00,       // mov di, 0x0000
            // Call INT 10h Function 4F01h with mode 0x100
            0xB9, 0x00, 0x01,       // mov cx, 0x0100 - Mode 0x100
            0xB8, 0x01, 0x4F,       // mov ax, 0x4F01
            0xCD, 0x10,             // int 10h
            // Check if AX = 004Fh
            0x3D, 0x4F, 0x00,       // cmp ax, 0x004F
            0x74, 0x04,             // je success
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Act
        VbeTestHandler testHandler = RunVbeTest(program);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Mode info should return success for supported mode");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE mode information for mode 0x101 (640x480x256).
    /// Verifies the resolution is correctly reported.
    /// </summary>
    [Fact]
    public void VbeReturnModeInfo_Mode101_ShouldReturn640x480() {
        // Arrange
        byte[] program = new byte[] {
            // Set up buffer pointer
            0xB8, 0x00, 0x20,       // mov ax, 0x2000
            0x8E, 0xC0,             // mov es, ax
            0xBF, 0x00, 0x00,       // mov di, 0x0000
            // Call INT 10h Function 4F01h with mode 0x101
            0xB9, 0x01, 0x01,       // mov cx, 0x0101 - Mode 0x101
            0xB8, 0x01, 0x4F,       // mov ax, 0x4F01
            0xCD, 0x10,             // int 10h
            // Check width at offset 12h - should be 640 (0x0280)
            0x26, 0x8B, 0x45, 0x12, // mov ax, [es:di+12h]
            0x3D, 0x80, 0x02,       // cmp ax, 640
            0x75, 0x0D,             // jne failed
            // Check height at offset 14h - should be 480 (0x01E0)
            0x26, 0x8B, 0x45, 0x14, // mov ax, [es:di+14h]
            0x3D, 0xE0, 0x01,       // cmp ax, 480
            0x75, 0x03,             // jne failed
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Act
        VbeTestHandler testHandler = RunVbeTest(program);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Mode 0x101 should report 640x480 resolution");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE mode information for an unsupported mode.
    /// Should return AX=014Fh (function supported but failed).
    /// </summary>
    [Fact]
    public void VbeReturnModeInfo_ForUnsupportedMode_ShouldReturnFailure() {
        // Arrange - Test unsupported mode 0xFFFF
        byte[] program = new byte[] {
            // Set up buffer pointer
            0xB8, 0x00, 0x20,       // mov ax, 0x2000
            0x8E, 0xC0,             // mov es, ax
            0xBF, 0x00, 0x00,       // mov di, 0x0000
            // Call INT 10h Function 4F01h with invalid mode
            0xB9, 0xFF, 0xFF,       // mov cx, 0xFFFF - Invalid mode
            0xB8, 0x01, 0x4F,       // mov ax, 0x4F01
            0xCD, 0x10,             // int 10h
            // Check if AX = 014Fh (supported but failed)
            0x3D, 0x4F, 0x01,       // cmp ax, 0x014F
            0x74, 0x04,             // je success
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Act
        VbeTestHandler testHandler = RunVbeTest(program);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Unsupported mode should return failure status");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE set mode function (Function 02h).
    /// Should return AX=004Fh when setting a supported mode.
    /// </summary>
    [Fact]
    public void VbeSetMode_WithSupportedMode_ShouldReturnSuccess() {
        // Arrange - Set mode 0x100 without clearing memory
        byte[] program = new byte[] {
            // Call INT 10h Function 4F02h with mode 0x8100 (bit 15 set = don't clear)
            0xBB, 0x00, 0x81,       // mov bx, 0x8100 - Mode 0x100, don't clear
            0xB8, 0x02, 0x4F,       // mov ax, 0x4F02
            0xCD, 0x10,             // int 10h
            // Check if AX = 004Fh
            0x3D, 0x4F, 0x00,       // cmp ax, 0x004F
            0x74, 0x04,             // je success
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Act
        VbeTestHandler testHandler = RunVbeTest(program);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Setting supported VBE mode should succeed");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE set mode with unsupported mode.
    /// Should return AX=014Fh (function supported but failed).
    /// </summary>
    [Fact]
    public void VbeSetMode_WithUnsupportedMode_ShouldReturnFailure() {
        // Arrange - Try to set invalid mode
        byte[] program = new byte[] {
            // Call INT 10h Function 4F02h with invalid mode
            0xBB, 0xFF, 0xFF,       // mov bx, 0xFFFF - Invalid mode
            0xB8, 0x02, 0x4F,       // mov ax, 0x4F02
            0xCD, 0x10,             // int 10h
            // Check if AX = 014Fh (failed)
            0x3D, 0x4F, 0x01,       // cmp ax, 0x014F
            0x74, 0x04,             // je success
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Act
        VbeTestHandler testHandler = RunVbeTest(program);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Setting unsupported VBE mode should return failure");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE return current mode function (Function 03h).
    /// Should return the mode that was previously set.
    /// </summary>
    [Fact]
    public void VbeReturnCurrentMode_AfterSetMode_ShouldReturnSetMode() {
        // Arrange - Set mode then query it
        byte[] program = new byte[] {
            // Set mode 0x101
            0xBB, 0x01, 0x81,       // mov bx, 0x8101 - Mode 0x101, don't clear
            0xB8, 0x02, 0x4F,       // mov ax, 0x4F02
            0xCD, 0x10,             // int 10h
            // Get current mode
            0xB8, 0x03, 0x4F,       // mov ax, 0x4F03
            0xCD, 0x10,             // int 10h
            // Check if BX = 0x0101
            0x81, 0xFB, 0x01, 0x01, // cmp bx, 0x0101
            0x74, 0x04,             // je success
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Act
        VbeTestHandler testHandler = RunVbeTest(program);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Current mode should match previously set mode");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE save/restore state function 04h subfunction 00h.
    /// Should return buffer size needed in BX.
    /// </summary>
    [Fact]
    public void VbeSaveRestoreState_GetBufferSize_ShouldReturnSize() {
        // Arrange - Get buffer size for all states
        byte[] program = new byte[] {
            // Call INT 10h Function 4F04h subfunction 00h
            0xB9, 0x0F, 0x00,       // mov cx, 0x000F - All states (bits 0-3)
            0xB2, 0x00,             // mov dl, 0x00 - Subfunction: get buffer size
            0xB8, 0x04, 0x4F,       // mov ax, 0x4F04
            0xCD, 0x10,             // int 10h
            // Check if AX = 004Fh
            0x3D, 0x4F, 0x00,       // cmp ax, 0x004F
            0x75, 0x07,             // jne failed
            // Check if BX > 0 (some buffer size returned)
            0x83, 0xFB, 0x00,       // cmp bx, 0
            0x76, 0x02,             // jbe failed
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Act
        VbeTestHandler testHandler = RunVbeTest(program);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Get buffer size should return non-zero size");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE save state function (Function 04h subfunction 01h).
    /// Should return AX=004Fh indicating success.
    /// </summary>
    [Fact]
    public void VbeSaveState_ShouldReturnSuccess() {
        // Arrange - Save state to a buffer
        byte[] program = new byte[] {
            // Set up buffer pointer
            0xB8, 0x00, 0x20,       // mov ax, 0x2000
            0x8E, 0xC0,             // mov es, ax
            0xBB, 0x00, 0x00,       // mov bx, 0x0000
            // Call INT 10h Function 4F04h subfunction 01h
            0xB9, 0x0F, 0x00,       // mov cx, 0x000F - All states
            0xB2, 0x01,             // mov dl, 0x01 - Subfunction: save
            0xB8, 0x04, 0x4F,       // mov ax, 0x4F04
            0xCD, 0x10,             // int 10h
            // Check if AX = 004Fh
            0x3D, 0x4F, 0x00,       // cmp ax, 0x004F
            0x74, 0x04,             // je success
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Act
        VbeTestHandler testHandler = RunVbeTest(program);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Save state should succeed");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests VBE restore state function (Function 04h subfunction 02h).
    /// Should return AX=004Fh indicating success.
    /// </summary>
    [Fact]
    public void VbeRestoreState_ShouldReturnSuccess() {
        // Arrange - Restore state from a buffer
        byte[] program = new byte[] {
            // Set up buffer pointer
            0xB8, 0x00, 0x20,       // mov ax, 0x2000
            0x8E, 0xC0,             // mov es, ax
            0xBB, 0x00, 0x00,       // mov bx, 0x0000
            // Call INT 10h Function 4F04h subfunction 02h
            0xB9, 0x0F, 0x00,       // mov cx, 0x000F - All states
            0xB2, 0x02,             // mov dl, 0x02 - Subfunction: restore
            0xB8, 0x04, 0x4F,       // mov ax, 0x4F04
            0xCD, 0x10,             // int 10h
            // Check if AX = 004Fh
            0x3D, 0x4F, 0x00,       // cmp ax, 0x004F
            0x74, 0x04,             // je success
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Act
        VbeTestHandler testHandler = RunVbeTest(program);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success, "Restore state should succeed");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Runs a VBE test program and returns a test handler with results.
    /// </summary>
    private VbeTestHandler RunVbeTest(byte[] program,
        [CallerMemberName] string unitTestName = "test") {
        // Arrange
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

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
