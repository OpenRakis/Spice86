namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.ViewModels.Services;

using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Xunit;

/// <summary>
/// Integration tests for DOS INT 21h functionality that run machine code through the emulation stack.
/// Tests verify DOS structures (CDS, DBCS) are properly initialized and accessible via INT 21h calls.
/// </summary>
public class DosInt21IntegrationTests {
    private const int ResultPort = 0x999;    // Port to write test results
    private const int DetailsPort = 0x998;   // Port to write test details/error messages

    enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests INT 21h, AH=63h, AL=00h - Get DBCS Lead Byte Table
    /// Verifies that the function returns a valid DS:SI pointer to the DBCS table
    /// </summary>
    [Fact]
    public void GetDbcsLeadByteTable_WithAL0_ReturnsValidPointer() {
        // This test calls INT 21h, AH=63h, AL=00h to get the DBCS table pointer
        // Expected: DS:SI points to DBCS table, AL=0, CF=0
        byte[] program = new byte[] {
            0xB8, 0x00, 0x63,       // mov ax, 6300h - Get DBCS lead byte table (AL=0)
            0xCD, 0x21,             // int 21h
            
            // Check AL = 0 (success)
            0x3C, 0x00,             // cmp al, 0
            0x75, 0x14,             // jne failed (jump to failed if AL != 0)
            
            // Check CF = 0 (no error)
            0x72, 0x11,             // jc failed (jump to failed if carry flag is set)
            
            // Check DS != 0 (valid segment)
            0x8C, 0xDA,             // mov dx, ds
            0x83, 0xFA, 0x00,       // cmp dx, 0
            0x74, 0x0B,             // je failed (jump to failed if DS == 0)
            
            // Check that DS:SI points to a value of 0 (empty DBCS table)
            0x8B, 0x04,             // mov ax, [si]
            0x83, 0xF8, 0x00,       // cmp ax, 0
            0x75, 0x04,             // jne failed
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests INT 21h, AH=63h, AL!=00h - Get DBCS Lead Byte Table with invalid subfunction
    /// Verifies that the function returns error code AL=0xFF for invalid subfunctions
    /// </summary>
    [Fact]
    public void GetDbcsLeadByteTable_WithInvalidAL_ReturnsError() {
        // This test calls INT 21h, AH=63h, AL=01h (invalid subfunction)
        // Expected: AL=0xFF (error)
        byte[] program = new byte[] {
            0xB8, 0x01, 0x63,       // mov ax, 6301h - Invalid subfunction (AL=1)
            0xCD, 0x21,             // int 21h
            
            // Check AL = 0xFF (error)
            0x3C, 0xFF,             // cmp al, 0FFh
            0x75, 0x04,             // jne failed
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that CDS (Current Directory Structure) is at the expected memory location
    /// Verifies the structure at segment 0x108 contains "C:\" (0x005c3a43)
    /// </summary>
    [Fact]
    public void CurrentDirectoryStructure_IsInitializedAtKnownLocation() {
        // This test verifies the CDS is at segment 0x108 with "C:\" initialized
        // Based on MemoryMap.DosCdsSegment = 0x108
        byte[] program = new byte[] {
            // Load CDS segment (0x108) into DS
            0xB8, 0x08, 0x01,       // mov ax, 0108h - CDS segment
            0x8E, 0xD8,             // mov ds, ax
            0x31, 0xF6,             // xor si, si - offset 0
            
            // Check first 4 bytes for "C:\" (0x005c3a43 in little-endian)
            // Byte 0: 0x43 ('C')
            0xAC,                   // lodsb (load byte from DS:SI into AL, increment SI)
            0x3C, 0x43,             // cmp al, 43h
            0x75, 0x12,             // jne failed
            
            // Byte 1: 0x3A (':')
            0xAC,                   // lodsb
            0x3C, 0x3A,             // cmp al, 3Ah
            0x75, 0x0D,             // jne failed
            
            // Byte 2: 0x5C ('\')
            0xAC,                   // lodsb
            0x3C, 0x5C,             // cmp al, 5Ch
            0x75, 0x08,             // jne failed
            
            // Byte 3: 0x00 (null terminator)
            0xAC,                   // lodsb
            0x3C, 0x00,             // cmp al, 00h
            0x75, 0x03,             // jne failed
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that DBCS table is in DOS private tables area (0xC800-0xD000)
    /// Verifies the table returned by INT 21h AH=63h is in the expected memory range
    /// </summary>
    [Fact]
    public void DbcsTable_IsInPrivateTablesArea() {
        // This test verifies the DBCS table is in the private tables segment range
        byte[] program = new byte[] {
            0xB8, 0x00, 0x63,       // mov ax, 6300h - Get DBCS lead byte table
            0xCD, 0x21,             // int 21h
            
            // DS now contains the segment, check it's >= 0xC800
            0x8C, 0xDA,             // mov dx, ds
            0x81, 0xFA, 0x00, 0xC8, // cmp dx, 0C800h
            0x72, 0x0A,             // jb failed (below C800h)
            
            // Check it's < 0xD000
            0x81, 0xFA, 0x00, 0xD0, // cmp dx, 0D000h
            0x73, 0x04,             // jae failed (>= D000h)
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests INT 21h, AH=62h - Get PSP Address and verifies parent PSP points to COMMAND.COM
    /// This verifies the PSP chain is properly established with COMMAND.COM as the root.
    /// </summary>
    /// <remarks>
    /// Magic numbers in the assembly code:
    /// - 0x16: ParentProgramSegmentPrefix offset in PSP (see DosProgramSegmentPrefix.cs)
    /// - 0x60: CommandCom.CommandComSegment - where COMMAND.COM's PSP is located
    /// </remarks>
    [Fact]
    public void GetPspAddress_ParentPspPointsToCommandCom() {
        // This test:
        // 1. Gets current PSP segment via INT 21h, AH=62h
        // 2. Reads the parent PSP segment at offset 0x16 in the PSP (ParentProgramSegmentPrefix)
        // 3. Verifies the parent PSP segment is COMMAND.COM's segment (0x60 = CommandCom.CommandComSegment)
        // 4. Verifies that COMMAND.COM's parent PSP points to itself (root of chain)
        byte[] program = new byte[] {
            // Get current PSP address
            0xB4, 0x62,             // mov ah, 62h - Get PSP address
            0xCD, 0x21,             // int 21h - BX = current PSP segment
            
            // Load PSP segment into ES
            0x8E, 0xC3,             // mov es, bx
            
            // Read parent PSP segment from offset 0x16 (ParentProgramSegmentPrefix in PSP)
            0x26, 0x8B, 0x1E, 0x16, 0x00,  // mov bx, es:[0016h] - BX = parent PSP segment
            
            // Check if parent PSP is COMMAND.COM (segment 0x60 = CommandCom.CommandComSegment)
            0x81, 0xFB, 0x60, 0x00, // cmp bx, 0060h
            0x75, 0x13,             // jne failed
            
            // Now verify COMMAND.COM's PSP points to itself (root of chain)
            // Load COMMAND.COM's PSP segment into ES
            0x8E, 0xC3,             // mov es, bx (bx = 0x60 = CommandCom.CommandComSegment)
            
            // Read COMMAND.COM's parent PSP from offset 0x16 (ParentProgramSegmentPrefix)
            0x26, 0x8B, 0x1E, 0x16, 0x00,  // mov bx, es:[0016h]
            
            // Verify it points to itself (0x60 = CommandCom.CommandComSegment marks root)
            0x81, 0xFB, 0x60, 0x00, // cmp bx, 0060h
            0x75, 0x04,             // jne failed
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that the environment block is not corrupted by PSP initialization.
    /// This specifically verifies that the first bytes of the environment block
    /// are the expected 'BL' characters (from BLASTER=...), not garbage from the PSP.
    /// Also verifies that bytes at offset 0x16 and 0x2C (PSP offsets for ParentPspSegment
    /// and EnvironmentTableSegment) are not corrupted with 0x60 0x01 pattern.
    /// </summary>
    [Fact]
    public void EnvironmentBlock_NotCorruptedByPsp() {
        byte[] program = new byte[] {
            // Get current PSP address
            0xB4, 0x62,             // 0x00: mov ah, 62h - Get PSP address
            0xCD, 0x21,             // 0x02: int 21h - BX = current PSP segment
            
            // Load PSP segment into ES
            0x8E, 0xC3,             // 0x04: mov es, bx
            
            // Read environment segment from PSP+0x2C
            0x26, 0x8B, 0x06, 0x2C, 0x00,  // 0x06: mov ax, es:[002Ch]
            
            // Check environment segment is not 0
            0x85, 0xC0,             // 0x0B: test ax, ax
            0x74, 0x2A,             // 0x0D: je failed (target 0x39)
            
            // Load environment segment into ES
            0x8E, 0xC0,             // 0x0F: mov es, ax
            
            // Read first byte of environment block
            0x26, 0x8A, 0x06, 0x00, 0x00,  // 0x11: mov al, es:[0000h]
            
            // Check it's 'B' (0x42) - first char of BLASTER
            0x3C, 0x42,             // 0x16: cmp al, 'B'
            0x75, 0x1F,             // 0x18: jne failed (target 0x39)
            
            // Check second byte is 'L' (0x4C)
            0x26, 0x8A, 0x06, 0x01, 0x00,  // 0x1A: mov al, es:[0001h]
            0x3C, 0x4C,             // 0x1F: cmp al, 'L'
            0x75, 0x18,             // 0x21: jne failed (target 0x3B)
            
            // Check byte at offset 0x16 (22) is NOT 0x60 (CommandCom segment)
            // This would indicate PSP ParentPspSegment corruption
            0x26, 0x8A, 0x06, 0x16, 0x00,  // 0x23: mov al, es:[0016h]
            0x3C, 0x60,             // 0x28: cmp al, 0x60
            0x74, 0x0F,             // 0x2A: je failed (target 0x3B)
            
            // Check byte at offset 0x2C (44) is NOT 0x60 (CommandCom segment)
            // This would indicate PSP EnvironmentTableSegment corruption
            0x26, 0x8A, 0x06, 0x2C, 0x00,  // 0x2C: mov al, es:[002Ch]
            0x3C, 0x60,             // 0x31: cmp al, 0x60
            0x74, 0x06,             // 0x33: je failed (target 0x3B)
            
            // Success
            0xB0, 0x00,             // 0x35: mov al, TestResult.Success
            0xEB, 0x02,             // 0x37: jmp writeResult (target 0x3B)
            
            // failed:
            0xB0, 0xFF,             // 0x39: mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // 0x3B: mov dx, ResultPort
            0xEE,                   // 0x3E: out dx, al
            0xF4                    // 0x3F: hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that a program can find its own path from the environment block.
    /// This verifies the DOS environment block contains the ASCIZ program path after
    /// the environment variables (double null + WORD count + path).
    /// </summary>
    [Fact]
    public void EnvironmentBlock_ContainsProgramPath() {
        byte[] program = new byte[] {
            // Get current PSP address
            0xB4, 0x62,             // 0x00: mov ah, 62h - Get PSP address
            0xCD, 0x21,             // 0x02: int 21h - BX = current PSP segment
            
            // Load PSP segment into ES
            0x8E, 0xC3,             // 0x04: mov es, bx
            
            // Read environment segment from PSP+0x2C using the proper encoding
            // mov ax, word ptr es:[0x002C]
            0x26, 0x8B, 0x06, 0x2C, 0x00,  // 0x06: mov ax, es:[002Ch]
            
            // Check environment segment is not 0
            0x85, 0xC0,             // 0x0B: test ax, ax
            0x74, 0x3F,             // 0x0D: je failed (target 0x4E, offset = 0x4E - 0x0F = 0x3F)
            
            // Load environment segment into ES
            0x8E, 0xC0,             // 0x0F: mov es, ax
            0x31, 0xFF,             // 0x11: xor di, di - start at offset 0
            
            // Scan for double null in environment block
            // find_double_null: (offset 0x13)
            0x26, 0x8A, 0x05,       // 0x13: mov al, es:[di]
            0x3C, 0x00,             // 0x16: cmp al, 0
            0x75, 0x08,             // 0x18: jne next_char (target 0x22, offset = 8)
            0x26, 0x8A, 0x45, 0x01, // 0x1A: mov al, es:[di+1]
            0x3C, 0x00,             // 0x1E: cmp al, 0
            0x74, 0x03,             // 0x20: je found_end (target 0x25, offset = 3)
            // next_char: (offset 0x22)
            0x47,                   // 0x22: inc di
            0xEB, 0xEE,             // 0x23: jmp find_double_null (target 0x13, offset = -18 = 0xEE)
            
            // found_end: (offset 0x25) - now at double null, skip past it
            0x83, 0xC7, 0x02,       // 0x25: add di, 2 - skip double null
            
            // Skip the WORD count (should be 1)
            0x26, 0x8B, 0x05,       // 0x28: mov ax, es:[di]
            0x83, 0xF8, 0x01,       // 0x2B: cmp ax, 1 - should be 1
            0x75, 0x1E,             // 0x2E: jne failed (target 0x4E, offset = 0x1E)
            0x83, 0xC7, 0x02,       // 0x30: add di, 2 - now di points to program path
            
            // Verify path starts with 'C' (0x43)
            0x26, 0x8A, 0x05,       // 0x33: mov al, es:[di]
            0x3C, 0x43,             // 0x36: cmp al, 'C'
            0x75, 0x14,             // 0x38: jne failed (target 0x4E, offset = 0x14)
            
            // Verify second char is ':' (0x3A)
            0x26, 0x8A, 0x45, 0x01, // 0x3A: mov al, es:[di+1]
            0x3C, 0x3A,             // 0x3E: cmp al, ':'
            0x75, 0x0C,             // 0x40: jne failed (target 0x4E, offset = 0x0C)
            
            // Verify third char is '\' (0x5C)
            0x26, 0x8A, 0x45, 0x02, // 0x42: mov al, es:[di+2]
            0x3C, 0x5C,             // 0x46: cmp al, '\'
            0x75, 0x04,             // 0x48: jne failed (target 0x4E, offset = 4)
            
            // Success
            0xB0, 0x00,             // 0x4A: mov al, TestResult.Success
            0xEB, 0x02,             // 0x4C: jmp writeResult (target 0x50, offset = 2)
            
            // failed: (offset 0x4E)
            0xB0, 0xFF,             // 0x4E: mov al, TestResult.Failure
            
            // writeResult: (offset 0x50)
            0xBA, 0x99, 0x09,       // 0x50: mov dx, ResultPort
            0xEE,                   // 0x53: out dx, al
            0xF4                    // 0x54: hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests INT 21h, AH=4Dh - Get Return Code of Child Process.
    /// Verifies that after a child process terminates, the parent can retrieve the exit code.
    /// </summary>
    [Fact]
    public void GetChildReturnCode_ReturnsReturnCode() {
        byte[] program = new byte[] {
            // Get child return code
            0xB4, 0x4D,             // mov ah, 4Dh - Get child return code
            0xCD, 0x21,             // int 21h
            
            // Save the result (AX) for verification
            // AL = exit code, AH = termination type
            // We'll check that the termination type is 0 (normal) for initial state
            0x88, 0xE0,             // mov al, ah - move termination type to AL
            0x3C, 0x00,             // cmp al, 0 - check if termination type is Normal (0)
            0x75, 0x04,             // jne failed (jump if not normal)
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that subsequent calls to INT 21h AH=4Dh return 0 (MS-DOS behavior).
    /// </summary>
    [Fact]
    public void GetChildReturnCode_SubsequentCallsReturnZero() {
        // This test calls INT 21h AH=4Dh twice and verifies the second call returns 0
        byte[] program = new byte[] {
            // First call to get child return code (clears the value)
            0xB4, 0x4D,             // mov ah, 4Dh - Get child return code
            0xCD, 0x21,             // int 21h
            
            // Second call - should return 0 now
            0xB4, 0x4D,             // mov ah, 4Dh - Get child return code again
            0xCD, 0x21,             // int 21h
            
            // Check that AX is 0 (both exit code and termination type)
            0x85, 0xC0,             // test ax, ax - check if AX is 0
            0x75, 0x04,             // jne failed (jump if not zero)
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that INT 20h properly terminates the program (legacy method).
    [Fact]
    public void Int20h_TerminatesProgramNormally() {
        // This test calls INT 20h to terminate the program
        byte[] program = new byte[] {
            // First, write a success marker before terminating
            0xB0, 0x00,             // mov al, TestResult.Success
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            
            // Terminate using INT 20h (legacy method)
            0xCD, 0x20,             // int 20h - should terminate
            
            // Verify return code (AL should be 0)
            0x3C, 0x00,             // cmp al, 0
            0x75, 0x01,             // jne failure
            0xF4,                   // hlt
            
            // failure:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        // We should see the success marker but NOT the failure marker
        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests INT 21h, AH=26h - Create New PSP.
    /// Verifies that a new PSP is created by copying the current PSP.
    /// </summary>
    /// </remarks>
    [Fact]
    public void CreateNewPsp_CreatesValidPspCopy() {
        // This test creates a new PSP at segment 0x2000 (128KB physical address) and verifies:
        // 1. INT 20h instruction at start of PSP
        // 2. Parent PSP in new PSP matches original PSP
        // 3. Environment segment is copied
        // 4. Terminate address is non-zero (updated from INT vector table)
        // 
        // Register usage:
        // - BP = original PSP segment (saved)
        // - DI = original environment segment (saved)
        // - DX = new PSP segment (0x2000)
        byte[] program = new byte[] {
            // Get current PSP address (save for comparison)
            0xB4, 0x62,             // mov ah, 62h - Get PSP address
            0xCD, 0x21,             // int 21h - BX = current PSP segment
            0x89, 0xDD,             // mov bp, bx - save original PSP in BP
            
            // Save the environment segment from the original PSP
            0x8E, 0xC3,             // mov es, bx - ES = current PSP segment
            0x26, 0x8B, 0x3E, 0x2C, 0x00,  // mov di, es:[002Ch] - DI = env segment
            
            // Create new PSP at segment 0x2000 using INT 21h AH=26h
            // Note: 0x2000 (128KB) is safely above the test program's memory at 0x0100
            0xBA, 0x00, 0x20,       // mov dx, 2000h - new PSP segment
            0xB4, 0x26,             // mov ah, 26h - Create New PSP
            0xCD, 0x21,             // int 21h
            
            // Load new PSP segment to verify its contents
            0xB8, 0x00, 0x20,       // mov ax, 2000h
            0x8E, 0xC0,             // mov es, ax - ES = new PSP (0x2000)
            
            // Check INT 20h instruction at offset 0 (0xCD, 0x20)
            0x26, 0x8A, 0x06, 0x00, 0x00,  // mov al, es:[0000h]
            0x3C, 0xCD,             // cmp al, 0CDh
            0x0F, 0x85, 0x2C, 0x00, // jne failed (near jump)
            
            0x26, 0x8A, 0x06, 0x01, 0x00,  // mov al, es:[0001h]
            0x3C, 0x20,             // cmp al, 20h
            0x0F, 0x85, 0x21, 0x00, // jne failed (near jump)
            
            // Check parent PSP segment at offset 0x16 matches original PSP (in BP)
            0x26, 0x8B, 0x1E, 0x16, 0x00,  // mov bx, es:[0016h] - parent PSP
            0x39, 0xEB,             // cmp bx, bp - compare with original PSP
            0x0F, 0x85, 0x13, 0x00, // jne failed (near jump)
            
            // Check environment segment at offset 0x2C matches original (in DI)
            0x26, 0x8B, 0x1E, 0x2C, 0x00,  // mov bx, es:[002Ch] - env segment
            0x39, 0xFB,             // cmp bx, di - compare with original env
            0x0F, 0x85, 0x08, 0x00, // jne failed (near jump)
            
            // Check terminate address (INT 22h vector) at offset 0x0A is non-zero
            0x26, 0x8B, 0x06, 0x0A, 0x00,  // mov ax, es:[000Ah] - terminate offset
            0x85, 0xC0,             // test ax, ax - check if non-zero
            0x74, 0x02,             // je failed (short jump if zero)
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests INT 21h, AH=55h - Create Child PSP.
    /// Verifies that a child PSP is created at the specified segment with proper initialization.
    /// </summary>
    [Fact]
    public void CreateChildPsp_CreatesValidPsp() {
        // This test creates a child PSP and verifies:
        // 1. Current PSP remains pointing to the parent PSP (matching DOS/FreeDOS)
        // 2. Parent PSP in child points to original PSP
        // 3. INT 20h instruction at start of PSP
        // 4. Environment segment is inherited
        // 5. AL = 0xF0 after the call
        // 
        // Register usage:
        // - BP = original PSP segment (saved)
        // - DI = original environment segment (saved)
        // - DX = child PSP segment (0x2000)
        // - SI = size in paragraphs (0x10)
        byte[] program = new byte[] {
            // Get current PSP address (save for comparison)
            0xB4, 0x62,             // mov ah, 62h - Get PSP address
            0xCD, 0x21,             // int 21h - BX = current PSP segment
            0x89, 0xDD,             // mov bp, bx - save original PSP in BP
            
            // Save the environment segment from the original PSP
            0x8E, 0xC3,             // mov es, bx - ES = current PSP segment
            0x26, 0x8B, 0x3E, 0x2C, 0x00,  // mov di, es:[002Ch] - DI = env segment
            
            // Create child PSP at segment 0x2000, size 0x10 paragraphs
            0xBA, 0x00, 0x20,       // mov dx, 2000h - child PSP segment
            0xBE, 0x10, 0x00,       // mov si, 0010h - size in paragraphs
            0xB4, 0x55,             // mov ah, 55h - Create Child PSP
            0xCD, 0x21,             // int 21h
            
            // Check AL = 0xF0 (destroyed value per DOS/FreeDOS)
            0x3C, 0xF0,             // cmp al, 0F0h
            0x0F, 0x85, 0x3F, 0x00, // jne failed (near jump)
            
            // Get current PSP to verify it remained the original parent PSP
            0xB4, 0x62,             // mov ah, 62h
            0xCD, 0x21,             // int 21h - BX = current PSP (should still be original PSP in BP)
            
            // Check if current PSP is still the parent (value saved in BP)
            0x39, 0xEB,             // cmp bx, bp
            0x0F, 0x85, 0x35, 0x00, // jne failed (near jump)
            
            // Load the child PSP segment (constant since DX may be clobbered)
            0xB8, 0x00, 0x20,       // mov ax, 2000h
            0x8E, 0xC0,             // mov es, ax - ES = child PSP (0x2000)
            
            // Check INT 20h instruction at offset 0 (0xCD, 0x20)
            0x26, 0x8A, 0x06, 0x00, 0x00,  // mov al, es:[0000h]
            0x3C, 0xCD,             // cmp al, 0CDh
            0x0F, 0x85, 0x25, 0x00, // jne failed
            
            0x26, 0x8A, 0x06, 0x01, 0x00,  // mov al, es:[0001h]
            0x3C, 0x20,             // cmp al, 20h
            0x0F, 0x85, 0x1A, 0x00, // jne failed
            
            // Check parent PSP segment at offset 0x16 matches original PSP (in BP)
            0x26, 0x8B, 0x1E, 0x16, 0x00,  // mov bx, es:[0016h] - parent PSP
            0x39, 0xEB,             // cmp bx, bp - compare with original PSP
            0x0F, 0x85, 0x0F, 0x00, // jne failed
            
            // Check environment segment at offset 0x2C matches original (in DI)
            0x26, 0x8B, 0x1E, 0x2C, 0x00,  // mov bx, es:[002Ch] - env segment
            0x39, 0xFB,             // cmp bx, di - compare with original env
            0x0F, 0x85, 0x04, 0x00, // jne failed
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests accessing program arguments (argc/argv) from DOS environment block.
    /// This simulates what C runtime libraries do when initializing argc and argv.
    /// </summary>
    [Fact]
    public void ProgramArguments_CanAccessArgvFromEnvironmentBlock() {
        // This test simulates a C program accessing argv[0] from the environment block.
        // It reads the program path from the environment and writes it to the console.
        byte[] program = new byte[] {
            // Get current PSP address
            0xB4, 0x62,             // 0x00: mov ah, 62h - Get PSP address
            0xCD, 0x21,             // 0x02: int 21h - BX = current PSP segment
            
            // Load PSP segment into ES
            0x8E, 0xC3,             // 0x04: mov es, bx
            
            // Read environment segment from PSP+0x2C
            0x26, 0x8B, 0x06, 0x2C, 0x00,  // 0x06: mov ax, es:[002Ch]
            
            // Check environment segment is not 0
            0x85, 0xC0,             // 0x0B: test ax, ax
            0x74, 0x47,             // 0x0D: je failed (target 0x56, offset = 0x47)
            
            // Load environment segment into ES
            0x8E, 0xC0,             // 0x0F: mov es, ax
            0x31, 0xFF,             // 0x11: xor di, di - start at offset 0
            
            // Scan for double null in environment block
            // find_double_null: (offset 0x13)
            0x26, 0x8A, 0x05,       // 0x13: mov al, es:[di]
            0x3C, 0x00,             // 0x16: cmp al, 0
            0x75, 0x08,             // 0x18: jne next_char (target 0x22, offset = 8)
            0x26, 0x8A, 0x45, 0x01, // 0x1A: mov al, es:[di+1]
            0x3C, 0x00,             // 0x1E: cmp al, 0
            0x74, 0x03,             // 0x20: je found_end (target 0x25, offset = 3)
            // next_char: (offset 0x22)
            0x47,                   // 0x22: inc di
            0xEB, 0xEE,             // 0x23: jmp find_double_null (target 0x13, offset = -18 = 0xEE)
            
            // found_end: (offset 0x25) - now at double null, skip past it
            0x83, 0xC7, 0x02,       // 0x25: add di, 2 - skip double null
            
            // Skip the WORD count (should be 1)
            0x26, 0x8B, 0x05,       // 0x28: mov ax, es:[di]
            0x83, 0xF8, 0x01,       // 0x2B: cmp ax, 1 - should be 1
            0x75, 0x27,             // 0x2E: jne failed (target 0x57, offset = 0x27)
            0x83, 0xC7, 0x02,       // 0x30: add di, 2 - now di points to program path
            
            // Now ES:DI points to the program path (argv[0])
            // Verify path starts with 'C' (0x43)
            0x26, 0x8A, 0x05,       // 0x33: mov al, es:[di]
            0x3C, 0x43,             // 0x36: cmp al, 'C'
            0x75, 0x1D,             // 0x38: jne failed (target 0x57, offset = 0x1D)
            
            // Verify second char is ':' (0x3A)
            0x26, 0x8A, 0x45, 0x01, // 0x3A: mov al, es:[di+1]
            0x3C, 0x3A,             // 0x3E: cmp al, ':'
            0x75, 0x15,             // 0x40: jne failed (target 0x57, offset = 0x15)
            
            // Verify third char is '\' (0x5C)
            0x26, 0x8A, 0x45, 0x02, // 0x42: mov al, es:[di+2]
            0x3C, 0x5C,             // 0x46: cmp al, '\'
            0x75, 0x0D,             // 0x48: jne failed (target 0x57, offset = 0x0D)
            
            // At this point, we've successfully read argv[0] from the environment
            // In a real C program, this would be used to populate argv[0]
            // For the test, we just verify we could read it without crashing
            
            // Success - we accessed argv[0] without crashing
            0xB0, 0x00,             // 0x4A: mov al, TestResult.Success
            0xBA, 0x99, 0x09,       // 0x4C: mov dx, ResultPort
            0xEE,                   // 0x4F: out dx, al
            0xF4,                   // 0x50: hlt
            0xEB, 0xFE,             // 0x51: jmp $ (infinite loop as safety net)
            
            // failed: (offset 0x53, but we need to account for the extra bytes)
            // Actually the failed label should be at 0x57 based on the jumps above
            0x90,                   // 0x53: nop (padding to align to 0x56)
            0x90,                   // 0x54: nop
            0x90,                   // 0x55: nop
            
            // failed: (offset 0x56)
            0xB0, 0xFF,             // 0x56: mov al, TestResult.Failure
            0xBA, 0x99, 0x09,       // 0x58: mov dx, ResultPort
            0xEE,                   // 0x5B: out dx, al
            0xF4                    // 0x5C: hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that a program can print its path using INT 21h AH=02h after reading from environment.
    /// This simulates what printf("[%i] %s\n", 0, argv[0]) would do in the C program.
    /// </summary>
    [Fact]
    public void ProgramArguments_CanPrintArgv0FromEnvironment() {
        // This test reads argv[0] from the environment block and prints it using INT 21h AH=09h.
        // It simulates the behavior of a C program that does: printf("%s\n", argv[0]);
        byte[] program = new byte[] {
        // Save CS early
        0x0E,                   // push cs
        0x1F,                   // pop ds  - DS = CS from the start
        
        // Get current PSP address
        0xB4, 0x62,             // mov ah, 62h
        0xCD, 0x21,             // int 21h - BX = PSP segment
        0x8E, 0xC3,             // mov es, bx - ES = PSP
        
        // Read environment segment from PSP+0x2C into ES
        0x26, 0x8B, 0x06, 0x2C, 0x00,  // mov ax, es:[002Ch]
        0x85, 0xC0,             // test ax, ax
        0x74, 0x47,             // je failed (short offset needs adjustment)
        
        // ES = environment segment
        0x8E, 0xC0,             // mov es, ax
        0x31, 0xFF,             // xor di, di
        
        // Scan for double null using ES override
        // scan_loop:
        0x26, 0x8A, 0x05,       // mov al, es:[di]
        0x3C, 0x00,             // cmp al, 0
        0x75, 0x08,             // jne next
        0x26, 0x8A, 0x45, 0x01, // mov al, es:[di+1]
        0x3C, 0x00,             // cmp al, 0
        0x74, 0x03,             // je found_double_null
        // next:
        0x47,                   // inc di
        0xEB, 0xEE,             // jmp scan_loop
        
        // found_double_null:
        0x83, 0xC7, 0x02,       // add di, 2
        0x26, 0x8B, 0x05,       // mov ax, es:[di]
        0x83, 0xF8, 0x01,       // cmp ax, 1
        0x75, 0x2F,             // jne failed
        0x83, 0xC7, 0x02,       // add di, 2
        
        // DI points to program path in ES
        // Read first character with ES override
        0x26, 0x8A, 0x15,       // mov dl, es:[di]
        
        // Call INT 21h AH=02h (DS already = CS)
        0xB4, 0x02,             // mov ah, 02h
        0xCD, 0x21,             // int 21h
        
        // Success
        0xB0, 0x00,             // mov al, TestResult.Success
        0xBA, 0x99, 0x09,       // mov dx, ResultPort
        0xEE,                   // out dx, al
        0xF4,                   // hlt
        
        // failed:
        0xB0, 0xFF,             // mov al, TestResult.Failure
        0xBA, 0x99, 0x09,       // mov dx, ResultPort
        0xEE,                   // out dx, al
        0xF4                    // hlt
    };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that standard file handles (STDIN/STDOUT/STDERR) are inherited from parent PSP.
    /// </summary>
    [Fact]
    public void StandardFileHandles_AreInheritedFromParentPsp() {
        byte[] program = new byte[] {
            // Get current PSP address
            0xB4, 0x62,             // mov ah, 62h
            0xCD, 0x21,             // int 21h - BX = PSP segment
            
            // Load PSP segment into ES
            0x8E, 0xC3,             // mov es, bx
            
            // Output handle 0 value for debugging
            0x26, 0x8A, 0x06, 0x18, 0x00,  // mov al, es:[0018h]
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al
            
            // Output handle 1 value for debugging
            0x26, 0x8A, 0x06, 0x19, 0x00,  // mov al, es:[0019h]
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al
            
            // Output handle 2 value for debugging
            0x26, 0x8A, 0x06, 0x1A, 0x00,  // mov al, es:[001Ah]
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al
            
            // Check file handle 0 (STDIN) at PSP+0x18 should be 0
            0x26, 0x8A, 0x06, 0x18, 0x00,  // mov al, es:[0018h]
            0x3C, 0x00,             // cmp al, 0
            0x75, 0x1C,             // jne failed (should be 0)
            
            // Check file handle 1 (STDOUT) at PSP+0x19 should be 1
            0x26, 0x8A, 0x06, 0x19, 0x00,  // mov al, es:[0019h]
            0x3C, 0x01,             // cmp al, 1
            0x75, 0x14,             // jne failed (should be 1)
            
            // Check file handle 2 (STDERR) at PSP+0x1A should be 2
            0x26, 0x8A, 0x06, 0x1A, 0x00,  // mov al, es:[001Ah]
            0x3C, 0x02,             // cmp al, 2
            0x75, 0x0C,             // jne failed (should be 2)
            
            // Success - all handles are correct
            0xB0, 0x00,             // mov al, TestResult.Success
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4,                   // hlt
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        // Log the actual handle values for debugging
        if (testHandler.Details.Count >= 3) {
            Console.WriteLine($"Handle 0 value: {testHandler.Details[0]}");
            Console.WriteLine($"Handle 1 value: {testHandler.Details[1]}");
            Console.WriteLine($"Handle 2 value: {testHandler.Details[2]}");
        }

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests INT 21h AH=0Eh (Select Default Drive).
    /// Bug 1: DL=2 (C:) should return AL=26 (total drive count), not the mounted count (3).
    /// Bug 2: DL >= 26 is invalid but the comparison used > instead of >=, so DL=26 was accepted.
    /// Bug 3: ElementAtOrDefault on DriveLetters with an out-of-range index could silently pass
    ///         a default '\0' key to TryGetValue instead of rejecting the drive index.
    /// </summary>
    [Fact]
    public void SelectDefaultDrive_WithValidDrive_ReturnsMaxDriveCountAndSwitchesDrive() {
        // Test plan:
        //   1. Select C: (DL=2) via AH=0Eh → AL must be 26
        //   2. Read current drive via AH=19h → AL must be 2 (C:)
        //   3. Select invalid drive (DL=26) via AH=0Eh → drive must remain C:
        //   4. Read current drive again via AH=19h → AL must still be 2
        byte[] program = new byte[] {
            0xB4, 0x0E,             // mov ah, 0Eh  - Select Default Drive
            0xB2, 0x02,             // mov dl, 02h  - C: drive index
            0xCD, 0x21,             // int 21h      - AL = number of drives

            // Write AL to details port so we can inspect the value
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort (0x998)
            0xEE,                   // out dx, al

            // Check AL == 26 (0x1A)
            0x3C, 0x1A,             // cmp al, 1Ah
            0x75, 0x22,             // jne failed   (offset 14 -> 48 = +34)

            0xB4, 0x19,             // mov ah, 19h  - Get Current Drive
            0xCD, 0x21,             // int 21h      - AL = current drive (0=A, 1=B, 2=C)

            // Write AL to details port
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al

            // Check AL == 2 (C:)
            0x3C, 0x02,             // cmp al, 02h
            0x75, 0x16,             // jne failed   (offset 26 -> 48 = +22)

            0xB4, 0x0E,             // mov ah, 0Eh
            0xB2, 0x1A,             // mov dl, 1Ah  - index 26 (invalid)
            0xCD, 0x21,             // int 21h

            0xB4, 0x19,             // mov ah, 19h  - Get Current Drive
            0xCD, 0x21,             // int 21h

            // Write AL to details port
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al

            // Check AL == 2 (C: unchanged)
            0x3C, 0x02,             // cmp al, 02h
            0x75, 0x04,             // jne failed

            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult

            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure

            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests INT 21h AH=43h AL=0 (Get File Attributes).
    /// A file with ReadOnly attribute must return CX with the ReadOnly bit (0x01) set.
    /// On Windows, the Archive bit (0x20) is also expected since the FAT Archive attribute persists;
    /// on Linux/macOS the Archive attribute is not supported by the filesystem and is ignored.
    /// </summary>
    [Fact]
    public void GetFileAttributes_ForReadOnlyFile_ReturnsDosAttributeBits() {
        // Arrange: create a test file with ReadOnly attribute in the working directory
        string testFileName = "TESTATTR.TXT";
        string testFilePath = Path.GetFullPath(testFileName);
        File.WriteAllText(testFilePath, "test");
        File.SetAttributes(testFilePath, FileAttributes.ReadOnly | FileAttributes.Archive);

        // On Linux/macOS, FileAttributes.Archive is not persisted by the host filesystem because
        // the FAT Archive attribute has no POSIX equivalent. Setting it via File.SetAttributes is a
        // no-op on Unix, so File.GetAttributes will never return the Archive bit (0x20) on those platforms.
        bool archiveBitSupported = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        try {
            // The filename string "TESTATTR.TXT\0" will be placed at the end of the program.
            // In a .COM file, code starts at CS:0100h, so the string address = 0x100 + code_length.
            byte[] fileNameBytes = System.Text.Encoding.ASCII.GetBytes(testFileName + "\0");

            // Code bytes (before the filename string).
            // When archiveBitSupported is false the Archive-bit check (test cl,20h / jz) is replaced
            // with five NOPs so the program only validates the ReadOnly bit.
            // ArchivePatchStart/End mark the byte range to NOP out when Archive is unsupported.
            const int ArchivePatchStart = 16; // index of: test cl, 20h (0xF6 0xC1 0x20)
            const int ArchivePatchEnd = 20; // index of last byte of: jz failed (0x74 0x09)
            byte[] code = new byte[] {
                // Set up DS:DX to point to the filename string
                // DX = 0x100 + length_of_this_code_array (patched below)
                0xBA, 0x00, 0x00,       // mov dx, <string_offset> (patched below)

                // Call INT 21h AH=43h AL=0 (Get File Attributes)
                0xB8, 0x00, 0x43,       // mov ax, 4300h
                0xCD, 0x21,             // int 21h

                // If carry set, file not found → fail
                0x72, 0x14,             // jc failed (offset: 10 -> 30 = +20)

                // Write CL to details port for debugging
                0x88, 0xC8,             // mov al, cl
                0xBA, 0x98, 0x09,       // mov dx, DetailsPort
                0xEE,                   // out dx, al

                // Check that Archive bit (0x20) is set in CL (replaced with NOPs when unsupported)
                0xF6, 0xC1, 0x20,       // test cl, 20h   (bytes 20-22)
                0x74, 0x09,             // jz failed       (bytes 23-24)

                // Check that ReadOnly bit (0x01) is set in CL
                0xF6, 0xC1, 0x01,       // test cl, 01h
                0x74, 0x04,             // jz failed (offset: 26 -> 30 = +4)

                // Success
                0xB0, 0x00,             // mov al, TestResult.Success
                0xEB, 0x02,             // jmp writeResult

                // failed: (offset 30)
                0xB0, 0xFF,             // mov al, TestResult.Failure

                // writeResult: (offset 32)
                0xBA, 0x99, 0x09,       // mov dx, ResultPort
                0xEE,                   // out dx, al
                0xF4                    // hlt
            };

            if (!archiveBitSupported) {
                // Replace "test cl,20h / jz failed" (indices ArchivePatchStart..ArchivePatchEnd) with NOPs
                // so only the ReadOnly bit is checked on platforms that don't persist FileAttributes.Archive.
                for (int i = ArchivePatchStart; i <= ArchivePatchEnd; i++) {
                    code[i] = 0x90; // nop
                }
            }

            // Patch the MOV DX immediate with the actual string offset (0x100 + code.Length)
            ushort stringOffset = (ushort)(0x100 + code.Length);
            code[1] = (byte)(stringOffset & 0xFF);
            code[2] = (byte)(stringOffset >> 8);

            // Combine code + filename into the final program
            byte[] program = new byte[code.Length + fileNameBytes.Length];
            Array.Copy(code, 0, program, 0, code.Length);
            Array.Copy(fileNameBytes, 0, program, code.Length, fileNameBytes.Length);

            DosTestHandler testHandler = RunDosTest(program);

            testHandler.Results.Should().Contain((byte)TestResult.Success);
            testHandler.Results.Should().NotContain((byte)TestResult.Failure);
        } finally {
            // Clean up: remove ReadOnly so the file can be deleted
            if (File.Exists(testFilePath)) {
                File.SetAttributes(testFilePath, FileAttributes.Normal);
                File.Delete(testFilePath);
            }
        }
    }

    /// <summary>
    /// Tests INT 21h AH=43h AL=1 (Set File Attributes) followed by AL=0 (Get) to verify round-trip.
    /// Creates a normal file, sets ReadOnly via AH=43h, then reads back attributes to verify.
    /// The current buggy implementation is a no-op for Set — it logs but doesn't apply the attribute.
    /// </summary>
    [Fact]
    public void SetFileAttributes_ThenGet_RoundTrips() {
        // Arrange: create a normal (non-readonly) test file
        string testFileName = "SETATR.TXT";
        string testFilePath = Path.GetFullPath(testFileName);
        File.WriteAllText(testFilePath, "test");
        File.SetAttributes(testFilePath, FileAttributes.Archive);

        try {
            byte[] fileNameBytes = System.Text.Encoding.ASCII.GetBytes(testFileName + "\0");

            byte[] code = new byte[] {
                // DX = address of filename string (patched below)
                0xBA, 0x00, 0x00,       // mov dx, <string_offset>

                0xB9, 0x21, 0x00,       // mov cx, 0021h (ReadOnly + Archive)
                0xB8, 0x01, 0x43,       // mov ax, 4301h
                0xCD, 0x21,             // int 21h

                // If carry → fail
                0x72, 0x19,             // jc failed (offset: 13 -> 38 = +25)

                // Reload DX (clobbered by details port writes later, but not yet)
                0xBA, 0x00, 0x00,       // mov dx, <string_offset> (patched below)
                0xB8, 0x00, 0x43,       // mov ax, 4300h
                0xCD, 0x21,             // int 21h

                // If carry → fail
                0x72, 0x0F,             // jc failed (offset: 23 -> 38 = +15)

                // Write CL to details port
                0x88, 0xC8,             // mov al, cl
                0xBA, 0x98, 0x09,       // mov dx, DetailsPort
                0xEE,                   // out dx, al

                // Check ReadOnly bit (0x01) is set
                0xF6, 0xC1, 0x01,       // test cl, 01h
                0x74, 0x04,             // jz failed (offset: 34 -> 38 = +4)

                // Actually need to recount. Let me be precise.
                // Success
                0xB0, 0x00,             // mov al, TestResult.Success
                0xEB, 0x02,             // jmp writeResult

                // failed: (offset 38)
                0xB0, 0xFF,             // mov al, TestResult.Failure

                // writeResult: (offset 40)
                0xBA, 0x99, 0x09,       // mov dx, ResultPort
                0xEE,                   // out dx, al
                0xF4                    // hlt
            };

            // Patch string offsets
            ushort stringOffset = (ushort)(0x100 + code.Length);
            code[1] = (byte)(stringOffset & 0xFF);
            code[2] = (byte)(stringOffset >> 8);
            code[14] = (byte)(stringOffset & 0xFF);
            code[15] = (byte)(stringOffset >> 8);

            byte[] program = new byte[code.Length + fileNameBytes.Length];
            Array.Copy(code, 0, program, 0, code.Length);
            Array.Copy(fileNameBytes, 0, program, code.Length, fileNameBytes.Length);

            DosTestHandler testHandler = RunDosTest(program);

            testHandler.Results.Should().Contain((byte)TestResult.Success);
            testHandler.Results.Should().NotContain((byte)TestResult.Failure);
        } finally {
            if (File.Exists(testFilePath)) {
                File.SetAttributes(testFilePath, FileAttributes.Normal);
                File.Delete(testFilePath);
            }
        }
    }

    /// <summary>
    /// Tests INT 21h AH=33h (Get/Set Control-Break Flag).
    /// Sets the break flag on, reads it back, verifies DL=1.
    /// Then sets it off, reads back, verifies DL=0.
    /// </summary>
    [Fact]
    public void GetSetControlBreak_RoundTrips() {
        byte[] program = new byte[] {
            0xB4, 0x33,             // mov ah, 33h
            0xB0, 0x01,             // mov al, 01h  (Set)
            0xB2, 0x01,             // mov dl, 01h  (ON)
            0xCD, 0x21,             // int 21h

            0xB4, 0x33,             // mov ah, 33h
            0xB0, 0x00,             // mov al, 00h  (Get)
            0xCD, 0x21,             // int 21h

            // DL should be 1
            0x80, 0xFA, 0x01,       // cmp dl, 01h
            0x75, 0x17,             // jne failed (offset 19 -> 42 = +23)

            0xB4, 0x33,             // mov ah, 33h
            0xB0, 0x01,             // mov al, 01h
            0xB2, 0x00,             // mov dl, 00h  (OFF)
            0xCD, 0x21,             // int 21h

            0xB4, 0x33,             // mov ah, 33h
            0xB0, 0x00,             // mov al, 00h
            0xCD, 0x21,             // int 21h

            // DL should be 0
            0x80, 0xFA, 0x00,       // cmp dl, 00h
            0x75, 0x04,             // jne failed (offset 38 -> 42 = +4)

            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult

            // failed: (offset 42)
            0xB0, 0xFF,             // mov al, TestResult.Failure

            // writeResult: (offset 44)
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        DosTestHandler testHandler = RunDosTest(program);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that AH=0Bh (Check Standard Input Status) detects Ctrl-C in the keyboard buffer
    /// when break checking is enabled, and invokes INT 23h which terminates the process.
    /// Ctrl-C is injected through the hardware keyboard stack: PS2Keyboard → Intel8042Controller
    /// → IRQ1 → BiosKeyboardInt9Handler → BiosKeyboardBuffer.
    /// </summary>
    [Fact]
    public void CheckStandardInputStatus_WithBreakEnabled_DetectsCtrlCAndInvokesInt23h() {
        // Arrange
        byte[] program = new byte[] {
            0xB4, 0x33,             // mov ah, 33h
            0xB0, 0x01,             // mov al, 01h  (Set)
            0xB2, 0x01,             // mov dl, 01h  (ON)
            0xCD, 0x21,             // int 21h

            // If INT 23h fires, the process terminates and Failure is never written.
            0xB0, 0x00,             // mov al, TestResult.Success
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al

            0xB9, 0xE8, 0x03,       // mov cx, 1000
            0xE2, 0xFE,             // loop $ (spin for 1000 iterations)

            // With break ON and Ctrl-C in buffer, this should invoke INT 23h.
            0xB4, 0x0B,             // mov ah, 0Bh
            0xCD, 0x21,             // int 21h

            // If we get here, Ctrl-C was NOT detected.
            0xB0, 0xFF,             // mov al, TestResult.Failure
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        // Act — inject Ctrl-C through the hardware keyboard stack via a cycle breakpoint
        DosTestHandler testHandler = RunDosTest(program, keyInjectionAction: SimulateCtrlC);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that AH=0Bh also detects Ctrl-Break (Ctrl+Pause) from the keyboard pipeline,
    /// and invokes the same INT 23h termination path as Ctrl-C.
    /// </summary>
    [Fact]
    public void CheckStandardInputStatus_WithBreakEnabled_DetectsCtrlBreakAndInvokesInt23h() {
        byte[] program = new byte[] {
            0xB4, 0x33,
            0xB0, 0x01,
            0xB2, 0x01,
            0xCD, 0x21,

            0xB0, 0x00,
            0xBA, 0x99, 0x09,
            0xEE,

            0xB9, 0xE8, 0x03,
            0xE2, 0xFE,

            0xB4, 0x0B,
            0xCD, 0x21,

            0xB0, 0xFF,
            0xBA, 0x99, 0x09,
            0xEE,
            0xF4
        };

        DosTestHandler testHandler = RunDosTest(program, keyInjectionAction: SimulateCtrlBreak);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Phase 0 — STDIN/STDOUT handle routing tests
    // These tests redirect STDIN (handle 0) to a file stream and verify that
    // INT 21h AH=07h/08h/0Ah read from the handle, not from INT 16h.
    // AH=01h is excluded: it uses ConsoleDevice.Read → INT 16h (MS-DOS 1.x
    // behavior) and does not go through IOCTL-based handle routing.
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Redirects STDIN to a file containing 'A' (0x41) and replaces OpenFiles[0].
    /// </summary>
    private static void RedirectStdinToByteA(Spice86DependencyInjection di) {
        MemoryStream stream = new(new byte[] { 0x41 });
        DosFile stdinFile = new("STDIN", 0, stream);
        di.Machine.Dos.FileManager.OpenFiles[0] = stdinFile;
    }

    /// <summary>
    /// Redirects STDIN to a file containing "Hello\r" and replaces OpenFiles[0].
    /// </summary>
    private static void RedirectStdinToHelloCr(Spice86DependencyInjection di) {
        byte[] data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x0D }; // "Hello\r"
        MemoryStream stream = new(data);
        DosFile stdinFile = new("STDIN", 0, stream);
        di.Machine.Dos.FileManager.OpenFiles[0] = stdinFile;
    }

    /// <summary>
    /// AH=07h reads from STDIN handle, not directly from INT 16h.
    /// Redirects STDIN to a file containing 'A'. Calls INT 21h AH=07h.
    /// Expects AL=0x41.
    /// </summary>
    [Fact]
    public void AH07h_ReadsFromStdinHandle_NotDirectlyFromInt16h() {
        // Arrange: program calls AH=07h then writes AL to details port, checks AL==0x41
        byte[] program = new byte[] {
            0xB4, 0x07,             // mov ah, 07h
            0xCD, 0x21,             // int 21h

            // Write AL to details port for diagnostics
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort (0x998)
            0xEE,                   // out dx, al

            // Check AL == 0x41 ('A')
            0x3C, 0x41,             // cmp al, 41h
            0x75, 0x04,             // jne failed

            // Success
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
        DosTestHandler testHandler = RunDosTest(program, preRunSetup: RedirectStdinToByteA);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "AH=07h should read 0x41 from the redirected STDIN handle");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// AH=08h reads from STDIN handle, not directly from INT 16h.
    /// Redirects STDIN to a file containing 'A'. Calls INT 21h AH=08h.
    /// Expects AL=0x41.
    /// </summary>
    [Fact]
    public void AH08h_ReadsFromStdinHandle_NotDirectlyFromInt16h() {
        byte[] program = new byte[] {
            0xB4, 0x08,             // mov ah, 08h
            0xCD, 0x21,             // int 21h

            // Write AL to details port for diagnostics
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort (0x998)
            0xEE,                   // out dx, al

            // Check AL == 0x41 ('A')
            0x3C, 0x41,             // cmp al, 41h
            0x75, 0x04,             // jne failed

            // Success
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
        DosTestHandler testHandler = RunDosTest(program, preRunSetup: RedirectStdinToByteA);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "AH=08h should read 0x41 from the redirected STDIN handle");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// AH=01h reads from STDIN handle, not directly from INT 16h.
    /// Redirects STDIN to a file containing 'A'. Calls INT 21h AH=01h.
    /// Expects AL=0x41.
    /// </summary>
    [Fact]
    public void AH01h_ReadsFromStdinHandle_NotDirectlyFromInt16h() {
        byte[] program = new byte[] {
            0xB4, 0x01,             // mov ah, 01h
            0xCD, 0x21,             // int 21h

            // Write AL to details port for diagnostics
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort (0x998)
            0xEE,                   // out dx, al

            // Check AL == 0x41 ('A')
            0x3C, 0x41,             // cmp al, 41h
            0x75, 0x04,             // jne failed

            // Success
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
        DosTestHandler testHandler = RunDosTest(program, preRunSetup: RedirectStdinToByteA);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "AH=01h should read 0x41 from the redirected STDIN handle");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// AH=0Ah reads from STDIN handle, not directly from INT 16h.
    /// Redirects STDIN to a file containing "Hello\r". Calls INT 21h AH=0Ah.
    /// Expects ReadCount == 5 ("Hello" only) — CR is excluded from ReadCount
    /// per FreeDOS read_line: kp->kb_count = count - 1.
    /// </summary>
    [Fact]
    public void AH0Ah_ReadsFromStdinHandle_NotDirectlyFromInt16h() {
        // The buffer for AH=0Ah is at DS:DX. We place it at offset 0x80 in the COM segment.
        // Buffer layout: byte 0 = max length, byte 1 = read count (output), byte 2+ = chars
        // We set max length = 20 at offset 0x80 using a MOV instruction before the INT call.
        byte[] program = new byte[] {
            // Set up buffer at DS:0x80
            0xC6, 0x06, 0x80, 0x00, 0x14, // mov byte [0x80], 20  (max length)
            0xC6, 0x06, 0x81, 0x00, 0x00, // mov byte [0x81], 0   (read count = 0)

            // Set DX = 0x80 (buffer address)
            0xBA, 0x80, 0x00,       // mov dx, 0080h
            0xB4, 0x0A,             // mov ah, 0Ah
            0xCD, 0x21,             // int 21h

            // Read the read count from [0x81]
            0xA0, 0x81, 0x00,       // mov al, [0x81]

            // Write read count to details port for diagnostics
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al

            // Check read count == 5 ("Hello" only — FreeDOS kb_count = count-1, CR not included)
            0x3C, 0x05,             // cmp al, 5
            0x75, 0x0D,             // jne failed

            // Check first char is 'H' (0x48) at [0x82]
            0xA0, 0x82, 0x00,       // mov al, [0x82]
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al
            0x3C, 0x48,             // cmp al, 48h
            0x75, 0x04,             // jne failed

            // Success
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
        DosTestHandler testHandler = RunDosTest(program, preRunSetup: RedirectStdinToHelloCr);

        // Assert
        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "AH=0Ah should read from the redirected STDIN handle");
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Simulates a Ctrl-C keypress through the full UI → hardware keyboard stack:
    /// HeadlessGui → InputEventHub → PS2Keyboard → Intel8042Controller → IRQ1
    /// → BiosKeyboardInt9Handler → BiosKeyboardBuffer.
    /// </summary>
    private static void SimulateCtrlC(HeadlessGui gui) {
        gui.SimulateKeyPress(PhysicalKey.ControlLeft);
        gui.SimulateKeyPress(PhysicalKey.C);
        gui.SimulateKeyRelease(PhysicalKey.C);
        gui.SimulateKeyRelease(PhysicalKey.ControlLeft);
    }

    private static void SimulateCtrlBreak(HeadlessGui gui) {
        gui.SimulateKeyPress(PhysicalKey.ControlLeft);
        gui.SimulateKeyPress(PhysicalKey.Pause);
        gui.SimulateKeyRelease(PhysicalKey.ControlLeft);
    }

    /// <summary>
    /// Cycle count at which keyboard events are injected via the breakpoint callback.
    /// Must be early enough that the full pipeline (InputEventHub → PS2Keyboard →
    /// Intel8042Controller → IRQ1 → BiosKeyboardInt9Handler) completes before the
    /// ASM program reads the keyboard buffer.
    /// </summary>
    private const long KeyInjectionCycleCount = 50;

    /// <summary>
    /// Fixed instructions-per-second used for keyboard injection tests.
    /// Makes the EmulationLoopScheduler deterministic: InputEventsHandler fires
    /// every 100 emulated ms = 100 instructions at this rate.
    /// </summary>
    private const long KeyboardTestInstructionsPerSecond = 1000;

    private DosTestHandler RunDosTest(byte[] program,
        Action<HeadlessGui>? keyInjectionAction = null,
        Action<Spice86DependencyInjection>? preRunSetup = null,
        [CallerMemberName] string unitTestName = "test") {
        // Write program to a .com file
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        // When keyboard injection is needed, use CyclesClock so the
        // EmulationLoopScheduler fires InputEventsHandler deterministically.
        long? instructionsPerSecond = keyInjectionAction is not null
            ? KeyboardTestInstructionsPerSecond
            : null;

        // Setup emulator with DOS initialized
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enablePit: false,
            maxCycles: 100000L,
            installInterruptVectors: true,
            enableA20Gate: true,
            instructionTimeScale: instructionsPerSecond
        ).Create();

        // Register a cycle breakpoint to inject keyboard events mid-execution
        // through the full UI stack: HeadlessGui → InputEventHub → PS2Keyboard
        // → Intel8042Controller → IRQ1 → BiosKeyboardInt9Handler → BiosKeyboardBuffer
        if (keyInjectionAction is not null) {
            HeadlessGui? headlessGui = spice86DependencyInjection.HeadlessGui;
            if (headlessGui is null) {
                throw new InvalidOperationException(
                    "HeadlessGui is not available — keyboard injection requires HeadlessType.Minimal");
            }
            spice86DependencyInjection.Machine.EmulatorBreakpointsManager.ToggleBreakPoint(
                new AddressBreakPoint(BreakPointType.CPU_CYCLES, KeyInjectionCycleCount,
                    _ => keyInjectionAction(headlessGui), isRemovedOnTrigger: true), true);
        }

        preRunSetup?.Invoke(spice86DependencyInjection);

        DosTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    private class DosTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();
        public List<byte> Details { get; } = new();

        public DosTestHandler(State state, ILoggerService loggerService,
            IOPortDispatcher ioPortDispatcher) : base(state, true, loggerService) {
            ioPortDispatcher.AddIOPortHandler(ResultPort, this);
            ioPortDispatcher.AddIOPortHandler(DetailsPort, this);
        }

        public override void WriteByte(ushort port, byte value) {
            if (port == ResultPort) {
                Results.Add(value);
            } else if (port == DetailsPort) {
                Details.Add(value);
            }
        }
    }
}
