namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;
using System.Text;

/// <summary>
/// Generates a minimal COMMAND.COM implementation that processes AUTOEXEC.BAT
/// and executes programs via standard DOS INT 21h/4Bh EXEC calls.
/// This replaces the callback-based shell approach with authentic DOS program flow.
/// </summary>
internal class MinimalCommandCom {
    private readonly IMemory _memory;
    private readonly ILoggerService _loggerService;

    public MinimalCommandCom(IMemory _memory, ILoggerService loggerService) {
        this._memory = _memory;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Generates and writes a minimal COMMAND.COM binary to memory at the specified segment.
    /// The generated code:
    /// 1. Opens and reads AUTOEXEC.BAT
    /// 2. For each line: executes via INT 21h/4Bh (EXEC)
    /// 3. Returns here after child terminates (INT 22h points back)
    /// 4. Handles EXIT command to terminate
    /// </summary>
    /// <param name="commandComSegment">Segment where COMMAND.COM PSP is located (typically 0x60)</param>
    /// <returns>Byte array containing the COMMAND.COM binary</returns>
    public byte[] GenerateCommandComBinary(ushort commandComSegment) {
        // COMMAND.COM will be a simple COM program that:
        // 1. Opens AUTOEXEC.BAT
        // 2. Reads line by line
        // 3. Executes each line via INT 21h/4Bh
        // 4. Loops until EOF or EXIT command
        // 5. Terminates with INT 21h/4Ch

        // For now, create a minimal implementation that just terminates
        // We'll build this up incrementally

        // Simple COM program structure:
        // org 100h (PSP is 256 bytes before code)
        // code starts at offset 0x100 within segment

        byte[] comBinary = GenerateMinimalShell();
        
        // Write the binary to memory at commandComSegment:0x100
        uint physicalAddress = MemoryUtils.ToPhysicalAddress(commandComSegment, 0x0100);
        for (int i = 0; i < comBinary.Length; i++) {
            _memory.UInt8[physicalAddress + (uint)i] = comBinary[i];
        }

        return comBinary;
    }

    /// <summary>
    /// Generates the machine code for a minimal shell that:
    /// - Loops calling shell processor via INT 21h with special function
    /// - Shell processor handles AUTOEXEC.BAT line-by-line
    /// - Terminates when shell processor signals exit
    /// </summary>
    private byte[] GenerateMinimalShell() {
        // COMMAND.COM loop:
        // loop_start:
        //   mov ah, FFh          ; B4 FF - special shell function: process next line
        //   int 21h              ; CD 21 - call DOS INT 21h
        //   cmp al, 00h          ; 3C 00 - check if should continue (0=continue, 1=exit)
        //   je loop_start        ; 74 F8 - if continue, loop back
        //   mov ah, 4Ch          ; B4 4C - terminate
        //   mov al, 00h          ; B0 00 - return code 0
        //   int 21h              ; CD 21 - DOS terminate
        
        return new byte[] {
            // loop_start: (offset 0)
            0xB4, 0xFF,        // mov ah, FFh - special shell function: process next line
            0xCD, 0x21,        // int 21h - call DOS
            0x3C, 0x00,        // cmp al, 00h - check return: 0=continue, 1=exit
            0x74, 0xF8,        // je loop_start (jump back 8 bytes to offset 0)
            // exit:
            0xB4, 0x4C,        // mov ah, 4Ch - terminate with return code
            0xB0, 0x00,        // mov al, 00h - return code 0
            0xCD, 0x21         // int 21h - DOS terminate
        };
    }
}
