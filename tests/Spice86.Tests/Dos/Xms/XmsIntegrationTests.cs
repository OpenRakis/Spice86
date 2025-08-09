namespace Spice86.Tests.Dos.Xms;

using FluentAssertions;

using Serilog;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;
using System.Text;

using Xunit;

/// <summary>
/// Integration tests for XMS functionality that run machine code through the emulation stack,
/// similar to how real programs like HITEST.ASM interact with the XMS driver.
/// </summary>
public class XmsIntegrationTests
{
    private const int ResultPort = 0x999;    // Port to write test results
    private const int DetailsPort = 0x998;   // Port to write test details/error messages

    private static class TestResult
    {
        public const byte Success = 0x00;
        public const byte Failure = 0xFF;
        public const byte A20Enabled = 0x01;
        public const byte A20Disabled = 0x02;
        public const byte HmaInUse = 0x03;
        public const byte HmaNotInUse = 0x04;
    }

    static XmsIntegrationTests()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Debug()
            .CreateLogger();
    }

    /// <summary>
    /// Tests XMS installation check via INT 2Fh, AH=43h, AL=00h
    /// </summary>
    [Fact]
    public void XmsInstallationCheck_ShouldBeInstalled()
    {
        // This test checks if the XMS driver is installed by calling INT 2Fh, AH=43h, AL=00h
        // If AL returns 80h, XMS is installed
        byte[] program = new byte[]
        {
            0xB8, 0x00, 0x43,       // mov ax, 4300h - XMS installation check
            0xCD, 0x2F,             // int 2Fh
            0x3C, 0x80,             // cmp al, 80h - is XMS installed?
            0x75, 0x04,             // jne notInstalled
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // notInstalled:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        XmsTestHandler testHandler = RunXmsTest(program, enableA20Gate: false);
        
        testHandler.Results.Should().Contain(TestResult.Success);
        testHandler.Results.Should().NotContain(TestResult.Failure);
    }

    /// <summary>
    /// Tests XMS entry point retrieval via INT 2Fh, AH=43h, AL=10h
    /// </summary>
    [Fact]
    public void GetXmsEntryPoint_ShouldReturnValidAddress()
    {
        // This test checks if we can get the XMS entry point
        // Result should be non-zero ES:BX
        byte[] program = new byte[]
        {
            0xB8, 0x10, 0x43,       // mov ax, 4310h - Get XMS entry point
            0xCD, 0x2F,             // int 2Fh
            0x26, 0x81, 0xFB, 0x00, 0x00, // cmp es:bx, 0 - check if we got a valid address
            0x74, 0x04,             // je failed
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        XmsTestHandler testHandler = RunXmsTest(program, enableA20Gate: false);
        
        testHandler.Results.Should().Contain(TestResult.Success);
        testHandler.Results.Should().NotContain(TestResult.Failure);
    }

    /// <summary>
    /// Tests XMS version check (Function 00h)
    /// </summary>
    [Fact]
    public void GetXmsVersion_ShouldReturnVersion3()
    {
        // This test checks if XMS reports version 3.00
        byte[] program = new byte[]
        {
            // First get XMS entry point
            0xB8, 0x10, 0x43,       // mov ax, 4310h - Get XMS entry point
            0xCD, 0x2F,             // int 2Fh
            
            // Save entry point
            0x89, 0xDE,             // mov si, bx
            0x8C, 0xC0,             // mov ax, es
            0x8E, 0xD8,             // mov ds, ax
            
            // Call function 00h - Get XMS Version
            0xB4, 0x00,             // mov ah, 00h
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if AX = 0300h (version 3.00)
            0x3D, 0x00, 0x03,       // cmp ax, 0300h
            0x75, 0x04,             // jne failed
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            
            // Write version to details port
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0x89, 0xC3,             // mov bx, ax (saved version)
            0x88, 0xF8,             // mov al, bh (high byte)
            0xEE,                   // out dx, al
            0x88, 0xD8,             // mov al, bl (low byte)
            0xEE,                   // out dx, al
            
            0xF4                    // hlt
        };

        XmsTestHandler testHandler = RunXmsTest(program, enableA20Gate: false);
        
        testHandler.Results.Should().Contain(TestResult.Success);
        testHandler.Results.Should().NotContain(TestResult.Failure);
    }

    /// <summary>
    /// Tests HMA request and release (Functions 01h and 02h)
    /// </summary>
    [Fact]
    public void RequestAndReleaseHma_ShouldSucceed()
    {
        // This test attempts to request and then release the HMA
        byte[] program = new byte[]
        {
            // First get XMS entry point
            0xB8, 0x10, 0x43,       // mov ax, 4310h - Get XMS entry point
            0xCD, 0x2F,             // int 2Fh
            
            // Save entry point
            0x89, 0xDE,             // mov si, bx
            0x8C, 0xC0,             // mov ax, es
            0x8E, 0xD8,             // mov ds, ax
            
            // Call function 01h - Request HMA
            0xB4, 0x01,             // mov ah, 01h
            0xBA, 0xFF, 0xFF,       // mov dx, FFFFh (request full HMA)
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if AX = 0001h (success)
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x10,             // jne requestFailed
            
            // Call function 02h - Release HMA
            0xB4, 0x02,             // mov ah, 02h
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if AX = 0001h (success)
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x04,             // jne releaseFailed
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x04,             // jmp writeResult
            
            // requestFailed or releaseFailed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xEB, 0x00,             // jmp writeResult (nop)
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        XmsTestHandler testHandler = RunXmsTest(program, enableA20Gate: false);
        
        testHandler.Results.Should().Contain(TestResult.Success);
        testHandler.Results.Should().NotContain(TestResult.Failure);
    }

    /// <summary>
    /// Tests global A20 line control (Functions 03h and 04h)
    /// </summary>
    [Fact]
    public void GlobalA20Control_ShouldEnableAndDisable()
    {
        // This test toggles the A20 line globally
        byte[] program = new byte[]
        {
            // First get XMS entry point
            0xB8, 0x10, 0x43,       // mov ax, 4310h - Get XMS entry point
            0xCD, 0x2F,             // int 2Fh
            
            // Save entry point
            0x89, 0xDE,             // mov si, bx
            0x8C, 0xC0,             // mov ax, es
            0x8E, 0xD8,             // mov ds, ax
            
            // Call function 03h - Global Enable A20
            0xB4, 0x03,             // mov ah, 03h
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if AX = 0001h (success)
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x1C,             // jne enableFailed
            
            // Call function 07h - Query A20
            0xB4, 0x07,             // mov ah, 07h
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if AX = 0001h (A20 enabled)
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x06,             // jne queryFailed
            
            // Signal that A20 is enabled
            0xB0, 0x01,             // mov al, TestResult.A20Enabled
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            
            // Call function 04h - Global Disable A20
            0xB4, 0x04,             // mov ah, 04h
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check final status
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x04,             // jne disableFailed
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // enableFailed, queryFailed, or disableFailed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        XmsTestHandler testHandler = RunXmsTest(program, enableA20Gate: false);
        
        testHandler.Results.Should().Contain(TestResult.A20Enabled);
        testHandler.Results.Should().Contain(TestResult.Success);
        testHandler.Results.Should().NotContain(TestResult.Failure);
    }

    /// <summary>
    /// Tests local A20 line control (Functions 05h and 06h)
    /// </summary>
    [Fact]
    public void LocalA20Control_ShouldEnableAndDisable()
    {
        // This test toggles the A20 line locally with nesting
        byte[] program = new byte[]
        {
            // First get XMS entry point
            0xB8, 0x10, 0x43,       // mov ax, 4310h - Get XMS entry point
            0xCD, 0x2F,             // int 2Fh
            
            // Save entry point
            0x89, 0xDE,             // mov si, bx
            0x8C, 0xC0,             // mov ax, es
            0x8E, 0xD8,             // mov ds, ax
            
            // First local enable
            0xB4, 0x05,             // mov ah, 05h
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x3A,             // jne failed
            
            // Query A20
            0xB4, 0x07,             // mov ah, 07h
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            0x3D, 0x01, 0x00,       // cmp ax, 0001h (A20 enabled)
            0x75, 0x32,             // jne failed
            
            // Second local enable (nested)
            0xB4, 0x05,             // mov ah, 05h
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x2A,             // jne failed
            
            // First local disable
            0xB4, 0x06,             // mov ah, 06h
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x22,             // jne failed
            
            // A20 should still be enabled (nested)
            0xB4, 0x07,             // mov ah, 07h
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            0x3D, 0x01, 0x00,       // cmp ax, 0001h (A20 enabled)
            0x75, 0x1A,             // jne failed
            
            // Second local disable
            0xB4, 0x06,             // mov ah, 06h
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x12,             // jne failed
            
            // A20 should now be disabled
            0xB4, 0x07,             // mov ah, 07h
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            0x3D, 0x00, 0x00,       // cmp ax, 0000h (A20 disabled)
            0x75, 0x0A,             // jne failed
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xEB, 0x08,             // jmp end
            
            // Failed
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            
            // End
            0xF4                    // hlt
        };

        XmsTestHandler testHandler = RunXmsTest(program, true); // Enable A20 gate
        
        testHandler.Results.Should().Contain(TestResult.Success);
        testHandler.Results.Should().NotContain(TestResult.Failure);
    }

    /// <summary>
    /// Tests memory allocation and freeing (Functions 09h and 0Ah)
    /// </summary>
    [Fact]
    public void AllocateAndFreeMemory_ShouldSucceed()
    {
        // This test allocates memory and then frees it
        byte[] program = new byte[]
        {
            // First get XMS entry point
            0xB8, 0x10, 0x43,       // mov ax, 4310h - Get XMS entry point
            0xCD, 0x2F,             // int 2Fh
            
            // Save entry point
            0x89, 0xDE,             // mov si, bx
            0x8C, 0xC0,             // mov ax, es
            0x8E, 0xD8,             // mov ds, ax
            
            // Call function 09h - Allocate Extended Memory Block (64K)
            0xB4, 0x09,             // mov ah, 09h
            0xBA, 0x40, 0x00,       // mov dx, 64 (64K)
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if AX = 0001h (success)
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x10,             // jne allocFailed
            
            // Save handle in BX
            0x89, 0xD3,             // mov bx, dx
            
            // Call function 0Ah - Free Extended Memory Block
            0xB4, 0x0A,             // mov ah, 0Ah
            0x89, 0xDA,             // mov dx, bx (handle)
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if AX = 0001h (success)
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x04,             // jne freeFailed
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // allocFailed or freeFailed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        XmsTestHandler testHandler = RunXmsTest(program, true); // Enable A20 gate
        
        testHandler.Results.Should().Contain(TestResult.Success);
        testHandler.Results.Should().NotContain(TestResult.Failure);
    }

    /// <summary>
    /// Tests memory block info (Function 0Eh)
    /// </summary>
    [Fact]
    public void GetMemoryBlockInfo_ShouldReturnCorrectInfo()
    {
        // This test allocates memory and then gets info about the allocated block
        byte[] program = new byte[]
        {
            // First get XMS entry point
            0xB8, 0x10, 0x43,       // mov ax, 4310h - Get XMS entry point
            0xCD, 0x2F,             // int 2Fh
            
            // Save entry point
            0x89, 0xDE,             // mov si, bx
            0x8C, 0xC0,             // mov ax, es
            0x8E, 0xD8,             // mov ds, ax
            
            // Call function 09h - Allocate Extended Memory Block (64K)
            0xB4, 0x09,             // mov ah, 09h
            0xBA, 0x40, 0x00,       // mov dx, 64 (64K)
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if AX = 0001h (success)
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x26,             // jne allocFailed
            
            // Save handle in BX
            0x89, 0xD3,             // mov bx, dx
            
            // Call function 0Eh - Get EMB Handle Information
            0xB4, 0x0E,             // mov ah, 0Eh
            0x89, 0xDA,             // mov dx, bx (handle)
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if AX = 0001h (success) and DX = 64 (size in K)
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x16,             // jne infoFailed
            0x81, 0xFA, 0x40, 0x00, // cmp dx, 64
            0x75, 0x10,             // jne sizeMismatch
            
            // Free the block
            0xB4, 0x0A,             // mov ah, 0Ah
            0x89, 0xDA,             // mov dx, bx (handle)
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Signal success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xEB, 0x08,             // jmp end
            
            // allocFailed, infoFailed, or sizeMismatch:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            
            // End
            0xF4                    // hlt
        };

        XmsTestHandler testHandler = RunXmsTest(program, true); // Enable A20 gate
        
        testHandler.Results.Should().Contain(TestResult.Success);
        testHandler.Results.Should().NotContain(TestResult.Failure);
    }

    /// <summary>
    /// Tests memory block move and lock operations (Functions 0Bh and 0Ch)
    /// </summary>
    [Fact]
    public void MoveAndLockMemoryBlock_ShouldSucceed()
    {
        // This test allocates memory, locks it to get physical address, moves data to/from it,
        // and verifies the operations succeed
        byte[] program = new byte[]
        {
            // First get XMS entry point
            0xB8, 0x10, 0x43,       // mov ax, 4310h - Get XMS entry point
            0xCD, 0x2F,             // int 2Fh
            
            // Save entry point
            0x89, 0xDE,             // mov si, bx
            0x8C, 0xC0,             // mov ax, es
            0x8E, 0xD8,             // mov ds, ax
            
            // Call function 09h - Allocate Extended Memory Block (16K)
            0xB4, 0x09,             // mov ah, 09h
            0xBA, 0x10, 0x00,       // mov dx, 16 (16K)
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if AX = 0001h (success) and save handle
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x72,             // jne allocFailed
            0x89, 0xD7,             // mov di, dx (save handle)
            
            // Lock the memory block (Function 0Ch)
            0xB4, 0x0C,             // mov ah, 0Ch
            0x89, 0xFA,             // mov dx, di (handle)
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if lock succeeded
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x66,             // jne lockFailed
            
            // Save physical address (DX:BX) for verification
            0x89, 0xD9,             // mov cx, bx
            0x89, 0xD0,             // mov ax, dx
            
            // Create source data at 0x5000:0 in conventional memory
            0xB8, 0x00, 0x50,       // mov ax, 5000h
            0x8E, 0xC0,             // mov es, ax
            0x26, 0xC7, 0x06, 0x00, 0x00, 0x34, 0x12,  // mov word ptr es:[0000h], 1234h
            0x26, 0xC7, 0x06, 0x02, 0x00, 0x78, 0x56,  // mov word ptr es:[0002h], 5678h
            
            // Setup Move Extended Memory Block structure at 0x4000:0
            0xB8, 0x00, 0x40,       // mov ax, 4000h
            0x8E, 0xC0,             // mov es, ax
            0x26, 0xC7, 0x06, 0x00, 0x00, 0x04, 0x00,  // mov word ptr es:[0000h], 0004h (length low)
            0x26, 0xC7, 0x06, 0x02, 0x00, 0x00, 0x00,  // mov word ptr es:[0002h], 0000h (length high)
            0x26, 0xC7, 0x06, 0x04, 0x00, 0x00, 0x00,  // mov word ptr es:[0004h], 0000h (srcHandle=0 for conventional)
            0x26, 0xC7, 0x06, 0x06, 0x00, 0x00, 0x00,  // mov word ptr es:[0006h], 0000h (srcOffset low)
            0x26, 0xC7, 0x06, 0x08, 0x00, 0x00, 0x50,  // mov word ptr es:[0008h], 5000h (srcOffset high = segment)
            0x26, 0x89, 0x3E, 0x0A, 0x00,              // mov word ptr es:[000Ah], di (destHandle)
            0x26, 0xC7, 0x06, 0x0C, 0x00, 0x00, 0x00,  // mov word ptr es:[000Ch], 0000h (destOffset low)
            0x26, 0xC7, 0x06, 0x0E, 0x00, 0x00, 0x00,  // mov word ptr es:[000Eh], 0000h (destOffset high)
            
            // Call function 0Bh - Move Memory Block
            0xB4, 0x0B,             // mov ah, 0Bh
            0xBB, 0x00, 0x40,       // mov bx, 4000h
            0x8E, 0xDB,             // mov ds, bx
            0xBE, 0x00, 0x00,       // mov si, 0000h
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if move succeeded
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x36,             // jne moveFailed
            
            // Unlock the memory block (Function 0Dh)
            0xB4, 0x0D,             // mov ah, 0Dh
            0x89, 0xFA,             // mov dx, di (handle)
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if unlock succeeded
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x2C,             // jne unlockFailed
            
            // Free the block (Function 0Ah)
            0xB4, 0x0A,             // mov ah, 0Ah
            0x89, 0xFA,             // mov dx, di (handle)
            0xFF, 0x1E, 0x1A, 0x00, // call far [dword ptr si] - Call XMS driver
            
            // Check if free succeeded
            0x3D, 0x01, 0x00,       // cmp ax, 0001h
            0x75, 0x22,             // jne freeFailed
            
            // Success!
            0xB0, 0x00,             // mov al, TestResult.Success
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            
            // Send physical address to details port
            0x88, 0xC3,             // mov bl, al (save success code)
            0x89, 0xC8,             // mov ax, cx (BX from lock)
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al (low byte)
            0x88, 0xE0,             // mov al, ah
            0xEE,                   // out dx, al (high byte)
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al (low byte)
            0x88, 0xE0,             // mov al, ah
            0xEE,                   // out dx, al (high byte)
            0x88, 0xD8,             // mov al, bl (restore success code)
            0xEB, 0x0A,             // jmp end
            
            // Failed paths
            // allocFailed, lockFailed, moveFailed, unlockFailed, freeFailed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            
            // End:
            0xF4                    // hlt
        };

        XmsTestHandler testHandler = RunXmsTest(program, true); // Enable A20 gate
        
        testHandler.Results.Should().Contain(TestResult.Success);
        testHandler.Results.Should().NotContain(TestResult.Failure);
    }

    /// <summary>
    /// Runs the XMS test program and returns a test handler with results
    /// </summary>
    private XmsTestHandler RunXmsTest(byte[] program, bool enableA20Gate,
        [CallerMemberName] string unitTestName = "test")
    {
        byte[] comFile = new byte[program.Length + 0x100];
        Array.Copy(program, 0, comFile, 0x100, program.Length);


        // Use program bytes directly without any padding
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        // Setup emulator with .com extension
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enableCfgCpu: true,
            enablePit: true,
            recordData: false,
            maxCycles: 100000L,
            installInterruptVectors: true,
            enableA20Gate: enableA20Gate,
            enableXms: true
        ).Create();

        // Create and register test handler
        ILoggerService loggerService = new LoggerService()
            .WithLogLevel(Serilog.Events.LogEventLevel.Verbose);
        
        XmsTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            loggerService,
            spice86DependencyInjection.Machine.IoPortDispatcher
        );

        try
        {
            spice86DependencyInjection.ProgramExecutor.Run();
        }
        catch (HaltRequestedException)
        {
            // HLT instruction will cause this exception, it's expected
        }

        return testHandler;
    }

    /// <summary>
    /// Captures XMS test results from designated I/O ports
    /// </summary>
    private class XmsTestHandler : DefaultIOPortHandler
    {
        public List<byte> Results { get; } = new();
        public string Details { get; private set; } = "";

        public XmsTestHandler(State state, ILoggerService loggerService, IOPortDispatcher ioPortDispatcher)
            : base(state, true, loggerService)
        {
            ioPortDispatcher.AddIOPortHandler(ResultPort, this);
            ioPortDispatcher.AddIOPortHandler(DetailsPort, this);
        }

        public override void WriteByte(ushort port, byte value)
        {
            if (port == ResultPort)
            {
                Results.Add(value);
            }
            else if (port == DetailsPort)
            {
                Details += Encoding.ASCII.GetString(new byte[] { value });
                _state.IsRunning = false;
            }
        }
    }
}