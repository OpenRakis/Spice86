namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration tests for DOS overlay loading via INT 21h AH=4Bh AL=03h.
/// Tests simulate games like The Summoning that load code overlays at runtime.
/// </summary>
/// <remarks>
/// The Summoning (SUMMON.COM) loads overlays CODE.1 and CODE.2 during execution.
/// These tests verify that overlay loading works correctly with proper relocation handling.
/// </remarks>
public class DosOverlayIntegrationTests {
    private const int ResultPort = 0x999;    // Port to write test results
    private const int DetailsPort = 0x998;   // Port to write test details/error messages

    enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests loading a simple EXE overlay and verifying it's loaded correctly.
    /// This simulates The Summoning loading CODE.1 or CODE.2 overlays.
    /// </summary>
    [Fact]
    public void LoadOverlay_SimpleExeOverlay_LoadsSuccessfully() {
        // Create a minimal EXE overlay file
        string testDir = CreateTestDirectory();
        string overlayPath = Path.Combine(testDir, "OVERLAY.EXE");
        
        // Create a minimal valid EXE with recognizable code pattern
        byte[] overlayExe = CreateMinimalExeWithCode(new byte[] { 0x90, 0x90, 0x90 }); // Three NOPs
        File.WriteAllBytes(overlayPath, overlayExe);

        // Test program that loads the overlay at segment 0x3000
        byte[] program = new byte[] {
            // Push CS and pop DS to setup data segment
            0x0E,                   // push cs
            0x1F,                   // pop ds
            
            // Prepare parameter block for overlay load at DS:SI
            // Parameter block structure for AL=03h:
            // +00h WORD load segment (0x3000)
            // +02h WORD relocation factor (0x3000)
            0xBE, 0x60, 0x00,       // mov si, 0x0060 - point to param block (at offset 0x60 in code)
            
            // Setup filename pointer at DS:DX
            0xBA, 0x64, 0x01,       // mov dx, 0x164 - points to filename (at 0x64 in code)
            
            // Call INT 21h AH=4Bh AL=03h (Load Overlay)
            0xB8, 0x03, 0x4B,       // mov ax, 4B03h - EXEC Load Overlay
            0xCD, 0x21,             // int 21h
            
            // Output AX to details port for debugging
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al
            0x88, 0xE0,             // mov al, ah
            0xEE,                   // out dx, al
            
            0x72, 0x1A,             // jc failed (carry set = error)
            
            // Verify AX=0 and DX=0 on success (DOS convention for overlay load)
            0x85, 0xC0,             // test ax, ax
            0x75, 0x15,             // jnz failed (AX should be 0)
            0x85, 0xD2,             // test dx, dx
            0x75, 0x11,             // jnz failed (DX should be 0)
            
            // Verify overlay was loaded by checking first byte at 0x3000:0000
            0xB8, 0x00, 0x30,       // mov ax, 3000h
            0x8E, 0xC0,             // mov es, ax
            0x26, 0x8A, 0x06, 0x00, 0x00,  // mov al, es:[0000h]
            0x3C, 0x90,             // cmp al, 90h (NOP opcode)
            0x75, 0x04,             // jne failed
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4,                   // hlt
            
            // Padding to reach 0x60 (96 bytes from start)
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            
            // Parameter block at offset 0x60 (relative to PSP:0x100 = absolute 0x160)
            // +00h WORD load segment
            0x00, 0x30,             // 0x3000
            // +02h WORD relocation factor  
            0x00, 0x30,             // 0x3000
            
            // Filename "OVERLAY.EXE" + null at offset 0x64
            0x4F, 0x56, 0x45, 0x52, 0x4C, 0x41, 0x59, 0x2E, 0x45, 0x58, 0x45, 0x00
            // O     V     E     R     L     A     Y     .     E     X     E    \0
        };

        OverlayTestHandler testHandler = RunOverlayTest(program, testDir);

        // Log the details for debugging
        if (testHandler.Details.Count >= 2) {
            Console.WriteLine($"DOS Error Code: AX={testHandler.Details[1]:X2}{testHandler.Details[0]:X2}");
        }

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests loading multiple overlays sequentially, simulating The Summoning loading CODE.1 and CODE.2.
    /// </summary>
    [Fact]
    public void LoadOverlay_MultipleOverlays_LoadSuccessfully() {
        string testDir = CreateTestDirectory();
        
        // Create two overlay files with different code patterns
        string overlay1Path = Path.Combine(testDir, "CODE.1");
        string overlay2Path = Path.Combine(testDir, "CODE.2");
        
        byte[] overlay1 = CreateMinimalExeWithCode(new byte[] { 0xAA, 0xBB, 0xCC }); // Marker bytes
        byte[] overlay2 = CreateMinimalExeWithCode(new byte[] { 0x11, 0x22, 0x33 }); // Different markers
        
        File.WriteAllBytes(overlay1Path, overlay1);
        File.WriteAllBytes(overlay2Path, overlay2);

        // Test program that loads both overlays
        byte[] program = new byte[] {
            0x0E,                   // push cs
            0x1F,                   // pop ds
            
            // Load CODE.1 at segment 0x4000
            0xBE, 0x80, 0x01,       // mov si, 0x180 - param block for CODE.1
            0xBA, 0x88, 0x01,       // mov dx, 0x188 - filename "CODE.1"
            0xB8, 0x03, 0x4B,       // mov ax, 4B03h
            0xCD, 0x21,             // int 21h
            0x72, 0x3A,             // jc failed
            
            // Verify CODE.1 loaded (check first byte at 0x4000:0000)
            0xB8, 0x00, 0x40,       // mov ax, 4000h
            0x8E, 0xC0,             // mov es, ax
            0x26, 0x8A, 0x06, 0x00, 0x00,  // mov al, es:[0000h]
            0x3C, 0xAA,             // cmp al, 0AAh
            0x75, 0x2D,             // jne failed
            
            // Load CODE.2 at segment 0x5000
            0xBE, 0x84, 0x01,       // mov si, 0x184 - param block for CODE.2
            0xBA, 0x90, 0x01,       // mov dx, 0x190 - filename "CODE.2"
            0xB8, 0x03, 0x4B,       // mov ax, 4B03h
            0xCD, 0x21,             // int 21h
            0x72, 0x20,             // jc failed
            
            // Verify CODE.2 loaded (check first byte at 0x5000:0000)
            0xB8, 0x00, 0x50,       // mov ax, 5000h
            0x8E, 0xC0,             // mov es, ax
            0x26, 0x8A, 0x06, 0x00, 0x00,  // mov al, es:[0000h]
            0x3C, 0x11,             // cmp al, 11h
            0x75, 0x13,             // jne failed
            
            // Verify CODE.1 still intact
            0xB8, 0x00, 0x40,       // mov ax, 4000h
            0x8E, 0xC0,             // mov es, ax
            0x26, 0x8A, 0x06, 0x00, 0x00,  // mov al, es:[0000h]
            0x3C, 0xAA,             // cmp al, 0AAh
            0x75, 0x04,             // jne failed
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4,                   // hlt
            
            // Padding to 0x80
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90,
            
            // Parameter blocks at 0x80
            // CODE.1 param block at 0x80
            0x00, 0x40,             // load segment 0x4000
            0x00, 0x40,             // relocation 0x4000
            // CODE.2 param block at 0x84
            0x00, 0x50,             // load segment 0x5000
            0x00, 0x50,             // relocation 0x5000
            
            // Filenames at 0x88 and 0x90
            // "CODE.1" at 0x88
            0x43, 0x4F, 0x44, 0x45, 0x2E, 0x31, 0x00, 0x90,
            // C     O     D     E     .     1    \0   pad
            // "CODE.2" at 0x90
            0x43, 0x4F, 0x44, 0x45, 0x2E, 0x32, 0x00
            // C     O     D     E     .     2    \0
        };

        OverlayTestHandler testHandler = RunOverlayTest(program, testDir);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Tests that loading a non-existent overlay returns an error.
    /// </summary>
    [Fact]
    public void LoadOverlay_NonExistentFile_ReturnsError() {
        string testDir = CreateTestDirectory();

        byte[] program = new byte[] {
            0x0E,                   // push cs
            0x1F,                   // pop ds
            
            0xBE, 0x80, 0x01,       // mov si, 0x180 - param block
            0xBA, 0x84, 0x01,       // mov dx, 0x184 - filename "NOEXIST.EXE"
            0xB8, 0x03, 0x4B,       // mov ax, 4B03h
            0xCD, 0x21,             // int 21h
            0x73, 0x04,             // jnc failed (should fail with carry set)
            
            // Success - we expected an error
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4,                   // hlt
            
            // Padding and data
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            
            // Parameter block
            0x00, 0x30,             // load segment
            0x00, 0x30,             // relocation
            
            // Filename "NOEXIST.EXE"
            0x4E, 0x4F, 0x45, 0x58, 0x49, 0x53, 0x54, 0x2E, 0x45, 0x58, 0x45, 0x00
        };

        OverlayTestHandler testHandler = RunOverlayTest(program, testDir);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    /// <summary>
    /// Creates a minimal valid EXE file with specified code bytes.
    /// </summary>
    private byte[] CreateMinimalExeWithCode(byte[] codeBytes) {
        // Create a minimal EXE header (512 bytes) + code
        byte[] image = new byte[512 + codeBytes.Length];
        
        // MZ signature
        image[0] = 0x4D; // 'M'
        image[1] = 0x5A; // 'Z'
        
        // Bytes on last page
        int totalSize = 512 + codeBytes.Length;
        int lastPageBytes = totalSize % 512;
        WriteUInt16LittleEndian(image, 0x02, (ushort)(lastPageBytes == 0 ? 512 : lastPageBytes));
        
        // Pages in file
        int totalPages = (totalSize + 511) / 512;
        WriteUInt16LittleEndian(image, 0x04, (ushort)totalPages);
        
        // Relocation items (none)
        WriteUInt16LittleEndian(image, 0x06, 0);
        
        // Header size in paragraphs (32 paragraphs = 512 bytes)
        WriteUInt16LittleEndian(image, 0x08, 32);
        
        // Min/max extra paragraphs
        WriteUInt16LittleEndian(image, 0x0A, 0);  // min
        WriteUInt16LittleEndian(image, 0x0C, 0xFFFF);  // max
        
        // Initial SS:SP
        WriteUInt16LittleEndian(image, 0x0E, 0);  // SS
        WriteUInt16LittleEndian(image, 0x10, 0x100);  // SP
        
        // Checksum (not validated)
        WriteUInt16LittleEndian(image, 0x12, 0);
        
        // Initial CS:IP
        WriteUInt16LittleEndian(image, 0x14, 0);  // IP
        WriteUInt16LittleEndian(image, 0x16, 0);  // CS
        
        // Relocation table offset
        WriteUInt16LittleEndian(image, 0x18, 0x1C);
        
        // Overlay number
        WriteUInt16LittleEndian(image, 0x1A, 0);
        
        // Copy code bytes after header (at offset 512)
        Array.Copy(codeBytes, 0, image, 512, codeBytes.Length);
        
        return image;
    }
    
    private static void WriteUInt16LittleEndian(byte[] buffer, int offset, ushort value) {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    /// <summary>
    /// Creates a temporary directory for test files.
    /// </summary>
    private string CreateTestDirectory([CallerMemberName] string testName = "test") {
        string tempDir = Path.Combine(Path.GetTempPath(), $"overlay_test_{testName}_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// Runs the overlay test program and returns a test handler with results.
    /// </summary>
    private OverlayTestHandler RunOverlayTest(byte[] program, string workingDirectory,
        [CallerMemberName] string unitTestName = "test") {
        string filePath = Path.Combine(workingDirectory, $"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enablePit: false,
            recordData: false,
            maxCycles: 200000L,
            installInterruptVectors: true,
            enableA20Gate: true,
            cDrive: workingDirectory  // Set the C: drive to our test directory
        ).Create();

        OverlayTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    private class OverlayTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();
        public List<byte> Details { get; } = new();

        public OverlayTestHandler(State state, ILoggerService loggerService,
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
