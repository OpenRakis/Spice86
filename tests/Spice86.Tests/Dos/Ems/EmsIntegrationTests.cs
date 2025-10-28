namespace Spice86.Tests.Dos.Ems;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration tests for EMS functionality that run machine code through the emulation stack.
/// These tests verify EMS behavior from the perspective of a real DOS program,
/// including both detection methods and IOCTL access as per LIM EMS 4.0 specification.
/// </summary>
public class EmsIntegrationTests {
    private const int ResultPort = 0x999;    // Port to write test results
    private const int DetailsPort = 0x998;   // Port to write test details/error messages

    enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests EMS detection via INT 67h vector check (first detection method).
    /// A valid EMS driver should have INT 67h pointing to a valid handler.
    /// </summary>
    [Fact]
    public void EmsDetection_ViaInterruptVector_ShouldBePresent() {
        // This test verifies the first detection method: checking if INT 67h vector is non-zero
        byte[] program = new byte[] {
            // Get INT 67h vector
            0xB8, 0x35, 0x67,       // mov ax, 3567h - Get interrupt vector
            0xCD, 0x21,             // int 21h (DOS get vector)
            // Check if ES:BX is non-zero
            0x8C, 0xC0,             // mov ax, es
            0x0B, 0xC3,             // or ax, bx - combine ES and BX
            0x74, 0x04,             // jz notInstalled
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // notInstalled:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "INT 67h vector should be installed");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests EMS detection via device driver name check (second detection method).
    /// The EMS driver should be accessible via the EMMXXXX0 device name.
    /// </summary>
    [Fact]
    public void EmsDetection_ViaDeviceDriverName_ShouldFindEMMXXXX0() {
        // This test verifies the second detection method:
        // Opening the "EMMXXXX0" device and checking its presence
        byte[] program = new byte[] {
            // Try to open EMMXXXX0 device
            0xB8, 0x00, 0x3D,       // mov ax, 3D00h - Open file for reading
            0xBA, 0x20, 0x00,       // mov dx, 0x0020 - Offset to device name
            0xCD, 0x21,             // int 21h
            0x72, 0x06,             // jc openFailed - Jump if carry (error)
            // Success - close the handle
            0x89, 0xC3,             // mov bx, ax - Move handle to BX
            0xB8, 0x00, 0x3E,       // mov ax, 3E00h - Close file
            0xCD, 0x21,             // int 21h
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // openFailed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4,                   // hlt
            // Device name at offset 0x20:
            0x45, 0x4D, 0x4D, 0x58, 0x58, 0x58, 0x58, 0x30, 0x00  // "EMMXXXX0\0"
        };

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "EMMXXXX0 device should be accessible");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests EMS GetStatus function (INT 67h, AH=40h).
    /// Should return status 00h in AH indicating EMM is operational.
    /// </summary>
    [Fact]
    public void EmsGetStatus_ShouldReturnNoError() {
        byte[] program = new byte[] {
            0xB4, 0x40,             // mov ah, 40h - Get Status
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0 - Check if status is 00h
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

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "EMS GetStatus should return no error");
    }

    /// <summary>
    /// Tests EMS GetPageFrameSegment function (INT 67h, AH=41h).
    /// Should return 0xE000 in BX and status 00h in AH.
    /// </summary>
    [Fact]
    public void EmsGetPageFrameSegment_ShouldReturnE000() {
        byte[] program = new byte[] {
            0xB4, 0x41,             // mov ah, 41h - Get Page Frame Segment
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0 - Check status
            0x75, 0x0A,             // jne failed
            0x81, 0xFB, 0x00, 0xE0, // cmp bx, 0xE000 - Check if BX = E000h
            0x74, 0x04,             // je success
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "GetPageFrameSegment should return 0xE000");
    }

    /// <summary>
    /// Tests EMS GetUnallocatedPageCount function (INT 67h, AH=42h).
    /// Should return page counts in BX and DX, with status 00h in AH.
    /// </summary>
    [Fact]
    public void EmsGetUnallocatedPageCount_ShouldReturnValidCounts() {
        byte[] program = new byte[] {
            0xB4, 0x42,             // mov ah, 42h - Get Unallocated Page Count
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0 - Check status
            0x75, 0x0C,             // jne failed
            // Check if DX > 0 (total pages)
            0x83, 0xFA, 0x00,       // cmp dx, 0
            0x76, 0x07,             // jbe failed
            // Check if BX <= DX (available <= total)
            0x39, 0xD3,             // cmp bx, dx
            0x77, 0x03,             // ja failed
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "GetUnallocatedPageCount should return valid counts");
    }

    /// <summary>
    /// Tests EMS AllocatePages function (INT 67h, AH=43h).
    /// Should successfully allocate pages and return a handle in DX.
    /// </summary>
    [Fact]
    public void EmsAllocatePages_ShouldReturnValidHandle() {
        byte[] program = new byte[] {
            0xBB, 0x04, 0x00,       // mov bx, 4 - Allocate 4 pages
            0xB4, 0x43,             // mov ah, 43h - Allocate Pages
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0 - Check status
            0x75, 0x07,             // jne failed
            // Check if handle (DX) is non-zero
            0x83, 0xFA, 0x00,       // cmp dx, 0
            0x76, 0x02,             // jbe failed
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort (overwrites handle, but we're done)
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "AllocatePages should return a valid handle");
    }

    /// <summary>
    /// Tests EMS MapUnmapHandlePage function (INT 67h, AH=44h).
    /// Should successfully map a logical page to a physical page.
    /// </summary>
    [Fact]
    public void EmsMapHandlePage_ShouldSucceed() {
        byte[] program = new byte[] {
            // First allocate pages
            0xBB, 0x04, 0x00,       // mov bx, 4 - Allocate 4 pages
            0xB4, 0x43,             // mov ah, 43h - Allocate Pages
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x75, 0x11,             // jne failed
            0x89, 0xD1,             // mov cx, dx - Save handle in CX
            // Now map logical page 0 to physical page 0
            0xB0, 0x00,             // mov al, 0 - Physical page 0
            0xBB, 0x00, 0x00,       // mov bx, 0 - Logical page 0
            0x89, 0xCA,             // mov dx, cx - Restore handle
            0xB4, 0x44,             // mov ah, 44h - Map/Unmap Handle Page
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x74, 0x04,             // je success
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "MapHandlePage should succeed");
    }

    /// <summary>
    /// Tests EMS memory read/write through mapped pages.
    /// Allocates pages, maps them, writes data, and verifies it can be read back.
    /// </summary>
    [Fact]
    public void EmsMemoryAccess_ThroughMappedPages_ShouldWork() {
        byte[] program = new byte[] {
            // Allocate 4 pages
            0xBB, 0x04, 0x00,       // mov bx, 4
            0xB4, 0x43,             // mov ah, 43h
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x75, 0x25,             // jne failed
            0x89, 0xD1,             // mov cx, dx - Save handle
            // Map logical page 0 to physical page 0
            0xB0, 0x00,             // mov al, 0
            0xBB, 0x00, 0x00,       // mov bx, 0
            0x89, 0xCA,             // mov dx, cx
            0xB4, 0x44,             // mov ah, 44h
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x75, 0x16,             // jne failed
            // Write test value to EMS page at E000:0000
            0xB8, 0x00, 0xE0,       // mov ax, 0xE000
            0x8E, 0xC0,             // mov es, ax
            0xC6, 0x06, 0x00, 0x00, 0x42, // mov byte [es:0], 42h
            // Read back and verify
            0x26, 0xA0, 0x00, 0x00, // mov al, [es:0]
            0x3C, 0x42,             // cmp al, 42h
            0x74, 0x04,             // je success
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "EMS memory should be readable and writable");
    }

    /// <summary>
    /// Tests EMS DeallocatePages function (INT 67h, AH=45h).
    /// Should successfully deallocate previously allocated pages.
    /// </summary>
    [Fact]
    public void EmsDeallocatePages_ShouldSucceed() {
        byte[] program = new byte[] {
            // Allocate 4 pages
            0xBB, 0x04, 0x00,       // mov bx, 4
            0xB4, 0x43,             // mov ah, 43h
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x75, 0x0C,             // jne failed
            0x89, 0xD1,             // mov cx, dx - Save handle
            // Deallocate the pages
            0x89, 0xCA,             // mov dx, cx
            0xB4, 0x45,             // mov ah, 45h - Deallocate Pages
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x74, 0x04,             // je success
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "DeallocatePages should succeed");
    }

    /// <summary>
    /// Tests EMS GetEmmVersion function (INT 67h, AH=46h).
    /// Should return version 3.2 (32h) in AL.
    /// </summary>
    [Fact]
    public void EmsGetVersion_ShouldReturn32() {
        byte[] program = new byte[] {
            0xB4, 0x46,             // mov ah, 46h - Get EMM Version
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0 - Check status
            0x75, 0x07,             // jne failed
            0x3C, 0x32,             // cmp al, 32h - Check if version is 3.2
            0x74, 0x04,             // je success
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "GetEmmVersion should return 3.2");
    }

    /// <summary>
    /// Tests EMS SavePageMap function (INT 67h, AH=47h).
    /// Should successfully save the current page map.
    /// </summary>
    [Fact]
    public void EmsSavePageMap_ShouldSucceed() {
        byte[] program = new byte[] {
            0xBA, 0x00, 0x00,       // mov dx, 0 - Use system handle
            0xB4, 0x47,             // mov ah, 47h - Save Page Map
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
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

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "SavePageMap should succeed");
    }

    /// <summary>
    /// Tests EMS RestorePageMap function (INT 67h, AH=48h).
    /// Should successfully restore a previously saved page map.
    /// </summary>
    [Fact]
    public void EmsRestorePageMap_ShouldSucceed() {
        byte[] program = new byte[] {
            // First save the page map
            0xBA, 0x00, 0x00,       // mov dx, 0
            0xB4, 0x47,             // mov ah, 47h - Save Page Map
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x75, 0x0B,             // jne failed
            // Now restore it
            0xBA, 0x00, 0x00,       // mov dx, 0
            0xB4, 0x48,             // mov ah, 48h - Restore Page Map
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x74, 0x04,             // je success
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "RestorePageMap should succeed");
    }

    /// <summary>
    /// Tests EMS GetEmmHandleCount function (INT 67h, AH=4Bh).
    /// Should return the number of active handles in BX.
    /// </summary>
    [Fact]
    public void EmsGetHandleCount_ShouldReturnValidCount() {
        byte[] program = new byte[] {
            0xB4, 0x4B,             // mov ah, 4Bh - Get EMM Handle Count
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x75, 0x07,             // jne failed
            // Check if BX > 0 (at least system handle exists)
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

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "GetHandleCount should return valid count");
    }

    /// <summary>
    /// Tests EMS GetHandlePages function (INT 67h, AH=4Ch).
    /// Should return the number of pages allocated to a handle.
    /// </summary>
    [Fact]
    public void EmsGetHandlePages_ShouldReturnCorrectCount() {
        byte[] program = new byte[] {
            // Allocate 8 pages
            0xBB, 0x08, 0x00,       // mov bx, 8
            0xB4, 0x43,             // mov ah, 43h
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x75, 0x0F,             // jne failed
            // Get handle pages
            0xB4, 0x4C,             // mov ah, 4Ch - Get Handle Pages
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x75, 0x07,             // jne failed
            0x81, 0xFB, 0x08, 0x00, // cmp bx, 8 - Should have 8 pages
            0x74, 0x04,             // je success
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "GetHandlePages should return correct count");
    }

    /// <summary>
    /// Tests that allocating zero pages fails with the appropriate error code.
    /// </summary>
    [Fact]
    public void EmsAllocateZeroPages_ShouldFail() {
        byte[] program = new byte[] {
            0xBB, 0x00, 0x00,       // mov bx, 0 - Try to allocate 0 pages
            0xB4, 0x43,             // mov ah, 43h - Allocate Pages
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x89,       // cmp ah, 89h - Should return error 89h
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

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "Allocating zero pages should fail with error 89h");
    }

    /// <summary>
    /// Tests that mapping with an invalid handle fails with the appropriate error code.
    /// </summary>
    [Fact]
    public void EmsMapWithInvalidHandle_ShouldFail() {
        byte[] program = new byte[] {
            0xB0, 0x00,             // mov al, 0 - Physical page 0
            0xBB, 0x00, 0x00,       // mov bx, 0 - Logical page 0
            0xBA, 0xFF, 0xFF,       // mov dx, 0FFFFh - Invalid handle
            0xB4, 0x44,             // mov ah, 44h - Map/Unmap Handle Page
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x83,       // cmp ah, 83h - Should return error 83h (invalid handle)
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

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "Mapping with invalid handle should fail with error 83h");
    }

    /// <summary>
    /// Tests that different logical pages maintain independent data.
    /// </summary>
    [Fact]
    public void EmsLogicalPages_ShouldBeIndependent() {
        byte[] program = new byte[] {
            // Allocate 2 pages
            0xBB, 0x02, 0x00,       // mov bx, 2
            0xB4, 0x43,             // mov ah, 43h
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x75, 0x4C,             // jne failed
            0x89, 0xD1,             // mov cx, dx - Save handle
            // Map logical page 0 to physical page 0
            0xB0, 0x00,             // mov al, 0
            0xBB, 0x00, 0x00,       // mov bx, 0
            0x89, 0xCA,             // mov dx, cx
            0xB4, 0x44,             // mov ah, 44h
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x75, 0x3D,             // jne failed
            // Write 11h to first byte of logical page 0
            0xB8, 0x00, 0xE0,       // mov ax, 0xE000
            0x8E, 0xC0,             // mov es, ax
            0xC6, 0x06, 0x00, 0x00, 0x11, // mov byte [es:0], 11h
            // Map logical page 1 to physical page 0
            0xB0, 0x00,             // mov al, 0
            0xBB, 0x01, 0x00,       // mov bx, 1
            0x89, 0xCA,             // mov dx, cx
            0xB4, 0x44,             // mov ah, 44h
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x75, 0x26,             // jne failed
            // Write 22h to first byte of logical page 1
            0xC6, 0x06, 0x00, 0x00, 0x22, // mov byte [es:0], 22h
            // Map logical page 0 back to physical page 0
            0xB0, 0x00,             // mov al, 0
            0xBB, 0x00, 0x00,       // mov bx, 0
            0x89, 0xCA,             // mov dx, cx
            0xB4, 0x44,             // mov ah, 44h
            0xCD, 0x67,             // int 67h
            0x80, 0xFC, 0x00,       // cmp ah, 0
            0x75, 0x0F,             // jne failed
            // Verify logical page 0 still has 11h
            0x26, 0xA0, 0x00, 0x00, // mov al, [es:0]
            0x3C, 0x11,             // cmp al, 11h
            0x74, 0x04,             // je success
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x02,             // jmp writeResult
            // success:
            0xB0, 0x00,             // mov al, TestResult.Success
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        EmsTestHandler testHandler = RunEmsTest(program, enableEms: true);

        testHandler.Results.Should().Contain((byte)TestResult.Success, "Logical pages should maintain independent data");
    }

    /// <summary>
    /// Runs the EMS test program and returns a test handler with results.
    /// </summary>
    private EmsTestHandler RunEmsTest(byte[] program, bool enableEms,
        [CallerMemberName] string unitTestName = "test") {
        // Write program to file
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        // Setup emulator with EMS enabled
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enableCfgCpu: true,
            enablePit: true,
            recordData: false,
            maxCycles: 100000L,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: false,
            enableEms: enableEms
        ).Create();

        EmsTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    /// <summary>
    /// Captures EMS test results from designated I/O ports.
    /// </summary>
    private class EmsTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();

        public EmsTestHandler(State state, ILoggerService loggerService,
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
