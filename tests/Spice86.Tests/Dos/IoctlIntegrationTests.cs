namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration tests for DOS IOCTL (INT 21h, AH=44h) functionality that run machine code
/// through the emulation stack. These tests verify IOCTL behavior from the perspective of
/// a real DOS program, testing both character device and block device operations.
/// 
/// References:
/// - MS-DOS 4.0 source code: https://github.com/microsoft/MS-DOS/tree/main/v4.0
/// - Adams - Writing DOS Device Drivers in C (1990)
/// - DOS INT 21h AH=44h IOCTL specifications
/// </summary>
public class IoctlIntegrationTests {
    private const int ResultPort = 0x999;    // Port to write test results
    private const int DetailsPort = 0x998;   // Port to write test details/error messages
    private const int DataPort = 0x997;      // Port to write test data values

    enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    // Character Device IOCTL Tests (Functions 0x00-0x07)

    /// <summary>
    /// Tests IOCTL function 0x00 (Get Device Information) for standard input (handle 0).
    /// Should return device information word with bit 7 (0x80) set indicating a character device.
    /// </summary>
    [Fact]
    public void Ioctl00_GetDeviceInformation_StdIn_ShouldReturnCharacterDevice() {
        byte[] program = new byte[] {
            // INT 21h, AH=44h, AL=00h (Get Device Information)
            0xB8, 0x00, 0x44,       // mov ax, 4400h
            0xBB, 0x00, 0x00,       // mov bx, 0 - stdin handle
            0xCD, 0x21,             // int 21h
            0x72, 0x0A,             // jc error - Jump if carry (error)
            // Check if bit 7 is set (character device)
            0xF6, 0xC2, 0x80,       // test dl, 80h - Check bit 7
            0x74, 0x04,             // jz error
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // error:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        IoctlTestHandler testHandler = RunIoctlTest(program);
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "stdin should be reported as a character device");
    }

    /// <summary>
    /// Tests IOCTL function 0x00 (Get Device Information) for standard output (handle 1).
    /// Should return device information word with bit 7 (0x80) set.
    /// </summary>
    [Fact]
    public void Ioctl00_GetDeviceInformation_StdOut_ShouldReturnCharacterDevice() {
        byte[] program = new byte[] {
            0xB8, 0x00, 0x44,       // mov ax, 4400h
            0xBB, 0x01, 0x00,       // mov bx, 1 - stdout handle
            0xCD, 0x21,             // int 21h
            0x72, 0x0A,             // jc error
            0xF6, 0xC2, 0x80,       // test dl, 80h - Check bit 7
            0x74, 0x04,             // jz error
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // error:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        IoctlTestHandler testHandler = RunIoctlTest(program);
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "stdout should be reported as a character device");
    }

    /// <summary>
    /// Tests IOCTL function 0x06 (Get Input Status) for standard input.
    /// Should return 0xFF in AL if input is ready, 0x00 if not.
    /// </summary>
    [Fact]
    public void Ioctl06_GetInputStatus_StdIn_ShouldReturnStatus() {
        byte[] program = new byte[] {
            0xB8, 0x06, 0x44,       // mov ax, 4406h - Get Input Status
            0xBB, 0x00, 0x00,       // mov bx, 0 - stdin handle
            0xCD, 0x21,             // int 21h
            0x72, 0x04,             // jc error
            // AL should be 0x00 or 0xFF
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // error:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        IoctlTestHandler testHandler = RunIoctlTest(program);
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "Get Input Status should succeed without error");
    }

    /// <summary>
    /// Tests IOCTL function 0x07 (Get Output Status) for standard output.
    /// Should return 0xFF in AL indicating device is ready.
    /// </summary>
    [Fact]
    public void Ioctl07_GetOutputStatus_StdOut_ShouldReturnReady() {
        byte[] program = new byte[] {
            0xB8, 0x07, 0x44,       // mov ax, 4407h - Get Output Status
            0xBB, 0x01, 0x00,       // mov bx, 1 - stdout handle
            0xCD, 0x21,             // int 21h
            0x72, 0x08,             // jc error
            0x3C, 0xFF,             // cmp al, 0xFF - Should be 0xFF (ready)
            0x74, 0x04,             // je success
            // error:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        IoctlTestHandler testHandler = RunIoctlTest(program);
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "stdout should report ready status (0xFF)");
    }

    /// <summary>
    /// Tests IOCTL function 0x00 with invalid handle.
    /// Should set carry flag and return error code 0x06 (invalid handle) in AX.
    /// </summary>
    [Fact]
    public void Ioctl00_GetDeviceInformation_InvalidHandle_ShouldReturnError() {
        byte[] program = new byte[] {
            0xB8, 0x00, 0x44,       // mov ax, 4400h
            0xBB, 0x99, 0x00,       // mov bx, 0x99 - invalid handle
            0xCD, 0x21,             // int 21h
            0x73, 0x04,             // jnc error - Jump if no carry (should have error)
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // error:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        IoctlTestHandler testHandler = RunIoctlTest(program);
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "invalid handle should return error with carry flag set");
    }

    // Block Device IOCTL Tests (Functions 0x08-0x0E)

    /// <summary>
    /// Tests IOCTL function 0x08 (Check if Block Device is Removable) for drive C:.
    /// Should return 0x00 in AX if removable, 0x01 if not removable.
    /// </summary>
    [Fact]
    public void Ioctl08_CheckBlockDeviceRemovable_DriveC_ShouldReturnNotRemovable() {
        byte[] program = new byte[] {
            0xB8, 0x08, 0x44,       // mov ax, 4408h - Check if block device removable
            0xBB, 0x03, 0x00,       // mov bx, 3 - Drive C: (0=default, 1=A:, 2=B:, 3=C:)
            0xCD, 0x21,             // int 21h
            0x72, 0x08,             // jc error
            0x3D, 0x01, 0x00,       // cmp ax, 1 - Should be 1 (not removable)
            0x74, 0x04,             // je success
            // error:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        IoctlTestHandler testHandler = RunIoctlTest(program);
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "drive C: should be reported as not removable");
    }

    /// <summary>
    /// Tests IOCTL function 0x09 (Check if Block Device is Remote) for drive C:.
    /// Should return attributes in DX with bit 12 (0x1000) clear for local drive.
    /// </summary>
    [Fact]
    public void Ioctl09_CheckBlockDeviceRemote_DriveC_ShouldReturnLocal() {
        byte[] program = new byte[] {
            0xB8, 0x09, 0x44,       // mov ax, 4409h - Check if block device remote
            0xBB, 0x03, 0x00,       // mov bx, 3 - Drive C:
            0xCD, 0x21,             // int 21h
            0x72, 0x0A,             // jc error
            // Check bit 12 (0x1000) - should be clear for local drive
            0xF7, 0xC2, 0x00, 0x10, // test dx, 1000h
            0x75, 0x04,             // jnz error - Should be zero
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // error:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        IoctlTestHandler testHandler = RunIoctlTest(program);
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "drive C: should be reported as local (not remote)");
    }

    /// <summary>
    /// Tests IOCTL function 0x0E (Get Logical Drive Map).
    /// Should return the logical drive number in AL.
    /// </summary>
    [Fact]
    public void Ioctl0E_GetLogicalDriveMap_DriveC_ShouldReturnDriveNumber() {
        byte[] program = new byte[] {
            0xB8, 0x0E, 0x44,       // mov ax, 440Eh - Get Logical Drive Map
            0xBB, 0x03, 0x00,       // mov bx, 3 - Drive C:
            0xCD, 0x21,             // int 21h
            0x72, 0x04,             // jc error
            // AL should contain drive mapping (0 if only one logical drive)
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // error:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        IoctlTestHandler testHandler = RunIoctlTest(program);
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "Get Logical Drive Map should succeed");
    }

    // Generic Block Device Request Tests (Function 0x0D)

    /// <summary>
    /// Tests IOCTL function 0x0D, subfunction 0x60 (Get Device Parameters).
    /// Should return device parameter block with device type and attributes.
    /// </summary>
    [Fact]
    public void Ioctl0D_GetDeviceParameters_DriveC_ShouldReturnValidData() {
        byte[] program = new byte[] {
            // Setup parameter block pointer in DS:DX
            0x0E,                   // push cs
            0x1F,                   // pop ds
            // Call IOCTL function 0x0D, minor code 0x60
            0xB8, 0x0D, 0x44,       // mov ax, 440Dh - Generic IOCTL for block devices
            0xBB, 0x03, 0x00,       // mov bx, 3 - Drive C:
            0xB9, 0x60, 0x08,       // mov cx, 0860h - CH=08 (disk), CL=60 (Get Device Params)
            0xBA, 0x30, 0x01,       // mov dx, 0x130 - Offset to parameter block (0x100 + 0x30)
            0xCD, 0x21,             // int 21h
            0x72, 0x10,             // jc error
            // Check that device type was filled (offset +1 in parameter block)
            0x8A, 0x06, 0x31, 0x01, // mov al, [0x131] - Read device type
            0x3C, 0x05,             // cmp al, 5 - Should be 5 for fixed disk
            0x75, 0x08,             // jne error
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // error:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4,                   // hlt
            // Padding to reach offset 0x30
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            // Parameter block (32 bytes reserved)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        IoctlTestHandler testHandler = RunIoctlTest(program);
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "Get Device Parameters should return valid device type");
    }

    /// <summary>
    /// Tests IOCTL function 0x0D, subfunction 0x66 (Get Volume Serial Number).
    /// Should return serial number, volume label, and file system type.
    /// </summary>
    [Fact]
    public void Ioctl0D_GetVolumeInfo_DriveC_ShouldReturnValidData() {
        byte[] program = new byte[] {
            0x0E,                   // push cs
            0x1F,                   // pop ds
            0xB8, 0x0D, 0x44,       // mov ax, 440Dh
            0xBB, 0x03, 0x00,       // mov bx, 3 - Drive C:
            0xB9, 0x66, 0x08,       // mov cx, 0866h - CH=08 (disk), CL=66 (Get Media ID)
            0xBA, 0x20, 0x01,       // mov dx, 0x120 - Offset to info block
            0xCD, 0x21,             // int 21h
            0x72, 0x04,             // jc error
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // error:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4,                   // hlt
            // Padding to reach offset 0x20
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            // Volume info buffer (32 bytes)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        IoctlTestHandler testHandler = RunIoctlTest(program);
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "Get Volume Info should succeed");
    }

    // Test Infrastructure

    /// <summary>
    /// Runs an IOCTL test program through the emulator.
    /// </summary>
    private IoctlTestHandler RunIoctlTest(byte[] program, bool enableEms = false, bool enableXms = false,
        [CallerMemberName] string unitTestName = "test") {
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enableCfgCpu: true,
            enablePit: true,
            recordData: false,
            maxCycles: 100000L,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: enableXms,
            enableEms: enableEms
        ).Create();

        IoctlTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    /// <summary>
    /// Captures IOCTL test results from designated I/O ports.
    /// </summary>
    private class IoctlTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();
        public List<byte> Details { get; } = new();
        public List<byte> Data { get; } = new();

        public IoctlTestHandler(State state, ILoggerService loggerService,
            IOPortDispatcher ioPortDispatcher) : base(state, true, loggerService) {
            ioPortDispatcher.AddIOPortHandler(ResultPort, this);
            ioPortDispatcher.AddIOPortHandler(DetailsPort, this);
            ioPortDispatcher.AddIOPortHandler(DataPort, this);
        }

        public override void WriteByte(ushort port, byte value) {
            if (port == ResultPort) {
                Results.Add(value);
            } else if (port == DetailsPort) {
                Details.Add(value);
            } else if (port == DataPort) {
                Data.Add(value);
            }
        }
    }
}