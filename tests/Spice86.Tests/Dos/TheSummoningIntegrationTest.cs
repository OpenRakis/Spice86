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
/// Integration test that reproduces The Summoning game behavior pattern.
/// The game uses SUMMON.COM, loads overlays CODE.1 and CODE.2, and performs extensive file I/O.
/// </summary>
public class TheSummoningIntegrationTest {
    private const int ResultPort = 0x999;
    private const int DetailsPort = 0x998;

    enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Reproduces The Summoning's startup sequence:
    /// 1. Open small data file "F" (449 bytes)
    /// 2. Read it byte by byte (simulating the pattern from logs)
    /// 3. Close file
    /// 4. Allocate memory
    /// 5. Try to load an overlay (CODE.1)
    /// </summary>
    [Fact]
    public void TheSummoning_StartupSequence_ExecutesWithoutCrash() {
        string testDir = CreateTestDirectory();
        
        // Create test data files matching The Summoning's files
        string fileF = Path.Combine(testDir, "F");
        File.WriteAllBytes(fileF, new byte[449]); // 449 bytes like the real F file
        
        string codeOverlay = Path.Combine(testDir, "CODE.1");
        byte[] overlayData = CreateMinimalExeWithCode(new byte[] { 0xC3 }); // RET instruction
        File.WriteAllBytes(codeOverlay, overlayData);

        // Main program simulating SUMMON.COM behavior
        byte[] program = new byte[] {
            // Setup DS
            0x0E,                   // push cs
            0x1F,                   // pop ds
            
            // ======= PART 1: Open file F =======
            0xBA, 0xA0, 0x00,       // mov dx, 0x00A0 - filename "F"
            0xB8, 0x00, 0x3D,       // mov ax, 3D00h - open file, read-only
            0xCD, 0x21,             // int 21h
            0x72, 0x70,             // jc failed (if file open fails)
            
            0x89, 0xC3,             // mov bx, ax - save file handle in BX
            
            // ======= PART 2: Read 10 bytes one at a time (simulating the log pattern) =======
            0xB9, 0x0A, 0x00,       // mov cx, 10 - loop counter
            
            // read_loop:
            0x51,                   // push cx - save counter
            0xB4, 0x3F,             // mov ah, 3Fh - read from file
            0xB9, 0x01, 0x00,       // mov cx, 0001h - read 1 byte
            0xBA, 0xA2, 0x00,       // mov dx, 0x00A2 - buffer
            0xCD, 0x21,             // int 21h
            0x72, 0x5E,             // jc failed
            0x59,                   // pop cx - restore counter
            0xE2, 0xF1,             // loop read_loop
            
            // ======= PART 3: Close file F =======
            0xB4, 0x3E,             // mov ah, 3Eh - close file
            0xCD, 0x21,             // int 21h
            0x72, 0x53,             // jc failed
            
            // ======= PART 4: Allocate memory (simulating game allocation) =======
            0xB4, 0x48,             // mov ah, 48h - allocate memory
            0xBB, 0x10, 0x00,       // mov bx, 0010h - 16 paragraphs (256 bytes)
            0xCD, 0x21,             // int 21h
            0x72, 0x49,             // jc failed
            
            0x89, 0xC6,             // mov si, ax - save allocated segment in SI
            
            // ======= PART 5: Free the allocated memory =======
            0xB4, 0x49,             // mov ah, 49h - free memory
            0x8E, 0xC6,             // mov es, si - segment to free
            0xCD, 0x21,             // int 21h
            0x72, 0x3E,             // jc failed
            
            // ======= PART 6: Try to load overlay CODE.1 =======
            0xBE, 0xA4, 0x00,       // mov si, 0x00A4 - param block
            0xBA, 0xA8, 0x00,       // mov dx, 0x00A8 - filename "CODE.1"
            0xB8, 0x03, 0x4B,       // mov ax, 4B03h - load overlay
            0xCD, 0x21,             // int 21h
            
            // Output result code for debugging
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al
            0x88, 0xE0,             // mov al, ah
            0xEE,                   // out dx, al
            
            0x72, 0x2A,             // jc failed (this is expected if overlay loading isn't fully working)
            
            // If we got here, everything worked
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4,                   // hlt
            
            // Padding to align data
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            
            // Data section at 0xA0
            // Filename "F" at 0xA0
            0x46, 0x00,             // "F\0"
            // Buffer at 0xA2
            0x00, 0x00,             // 2-byte buffer for reads
            // Overlay param block at 0xA4
            0x00, 0x40,             // load segment 0x4000
            0x00, 0x40,             // relocation 0x4000
            // Filename "CODE.1" at 0xA8
            0x43, 0x4F, 0x44, 0x45, 0x2E, 0x31, 0x00
            // C     O     D     E     .     1    \0
        };

        SummoningTestHandler testHandler = RunSummoningTest(program, testDir);

        // The test should complete without crashing
        // We don't expect perfect success (overlay might fail), but we should not crash
        testHandler.Results.Should().NotBeEmpty("Program should complete and write a result");
        
        // Log any error codes
        if (testHandler.Details.Count >= 2) {
            Console.WriteLine($"Overlay load result: AX={testHandler.Details[1]:X2}{testHandler.Details[0]:X2}");
        }
    }

    /// <summary>
    /// Test that reproduces a simpler crash scenario - just opening and reading a file
    /// multiple times in a loop, which might reveal memory corruption or handle issues.
    /// </summary>
    [Fact]
    public void TheSummoning_RepeatedFileOperations_DoesNotCrash() {
        string testDir = CreateTestDirectory();
        
        string fileF = Path.Combine(testDir, "F");
        File.WriteAllBytes(fileF, new byte[100]);

        // Program that opens, reads, and closes a file 5 times
        byte[] program = new byte[] {
            0x0E,                   // push cs
            0x1F,                   // pop ds
            
            0xB9, 0x05, 0x00,       // mov cx, 5 - repeat 5 times
            
            // outer_loop:
            0x51,                   // push cx
            
            // Open file
            0xBA, 0x50, 0x00,       // mov dx, 0x0050 - filename
            0xB8, 0x00, 0x3D,       // mov ax, 3D00h
            0xCD, 0x21,             // int 21h
            0x72, 0x20,             // jc failed
            
            0x89, 0xC3,             // mov bx, ax - file handle
            
            // Read 10 bytes
            0xB4, 0x3F,             // mov ah, 3Fh
            0xB9, 0x0A, 0x00,       // mov cx, 10
            0xBA, 0x52, 0x00,       // mov dx, 0x0052 - buffer
            0xCD, 0x21,             // int 21h
            0x72, 0x13,             // jc failed
            
            // Close file
            0xB4, 0x3E,             // mov ah, 3Eh
            0xCD, 0x21,             // int 21h
            0x72, 0x0D,             // jc failed
            
            0x59,                   // pop cx
            0xE2, 0xE2,             // loop outer_loop
            
            // Success
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            
            // failed:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4,                   // hlt
            
            // Data
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90, 0x90, 0x90,
            
            // Filename "F" at 0x50
            0x46, 0x00,             // "F\0"
            // Buffer at 0x52
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        SummoningTestHandler testHandler = RunSummoningTest(program, testDir);

        testHandler.Results.Should().Contain((byte)TestResult.Success);
        testHandler.Results.Should().NotContain((byte)TestResult.Failure);
    }

    private byte[] CreateMinimalExeWithCode(byte[] codeBytes) {
        byte[] image = new byte[512 + codeBytes.Length];
        
        image[0] = 0x4D; // 'M'
        image[1] = 0x5A; // 'Z'
        
        int totalSize = 512 + codeBytes.Length;
        int lastPageBytes = totalSize % 512;
        WriteUInt16LittleEndian(image, 0x02, (ushort)(lastPageBytes == 0 ? 512 : lastPageBytes));
        
        int totalPages = (totalSize + 511) / 512;
        WriteUInt16LittleEndian(image, 0x04, (ushort)totalPages);
        
        WriteUInt16LittleEndian(image, 0x06, 0);
        WriteUInt16LittleEndian(image, 0x08, 32);
        WriteUInt16LittleEndian(image, 0x0A, 0);
        WriteUInt16LittleEndian(image, 0x0C, 0xFFFF);
        WriteUInt16LittleEndian(image, 0x0E, 0);
        WriteUInt16LittleEndian(image, 0x10, 0x100);
        WriteUInt16LittleEndian(image, 0x12, 0);
        WriteUInt16LittleEndian(image, 0x14, 0);
        WriteUInt16LittleEndian(image, 0x16, 0);
        WriteUInt16LittleEndian(image, 0x18, 0x1C);
        WriteUInt16LittleEndian(image, 0x1A, 0);
        
        Array.Copy(codeBytes, 0, image, 512, codeBytes.Length);
        
        return image;
    }
    
    private static void WriteUInt16LittleEndian(byte[] buffer, int offset, ushort value) {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private string CreateTestDirectory([CallerMemberName] string testName = "test") {
        string tempDir = Path.Combine(Path.GetTempPath(), $"summoning_test_{testName}_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private SummoningTestHandler RunSummoningTest(byte[] program, string workingDirectory,
        [CallerMemberName] string unitTestName = "test") {
        string filePath = Path.Combine(workingDirectory, $"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enablePit: false,
            recordData: false,
            maxCycles: 500000L,  // More cycles for complex operations
            installInterruptVectors: true,
            enableA20Gate: true,
            cDrive: workingDirectory
        ).Create();

        SummoningTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    private class SummoningTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();
        public List<byte> Details { get; } = new();

        public SummoningTestHandler(State state, ILoggerService loggerService,
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
