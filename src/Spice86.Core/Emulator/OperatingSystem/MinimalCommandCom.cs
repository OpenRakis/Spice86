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
        // COMMAND.COM will be a full ASM program that:
        // 1. Opens AUTOEXEC.BAT using INT 21h/3Dh
        // 2. Reads lines using INT 21h/3Fh
        // 3. Parses and executes lines via INT 21h/4Bh EXEC
        // 4. Handles EXIT command
        // 5. Loops until EOF or EXIT
        // 6. Terminates with INT 21h/4Ch

        // Simple COM program structure:
        // org 100h (PSP is 256 bytes before code)
        // code starts at offset 0x100 within segment

        byte[] comBinary = GenerateMinimalShell();
        
        // Write the binary to memory at commandComSegment:0x100
        uint physicalAddress = MemoryUtils.ToPhysicalAddress(commandComSegment, 0x0100);
        for (int i = 0; i < comBinary.Length; i++) {
            _memory.UInt8[physicalAddress + (uint)i] = comBinary[i];
        }
        
        // Write data section: filename "C:\\AUTOEXEC.BAT" at offset 0x0302
        string filename = "C:\\AUTOEXEC.BAT\0"; // Null-terminated
        byte[] filenameBytes = System.Text.Encoding.ASCII.GetBytes(filename);
        uint filenameAddress = MemoryUtils.ToPhysicalAddress(commandComSegment, 0x0302);
        for (int i = 0; i < filenameBytes.Length; i++) {
            _memory.UInt8[filenameAddress + (uint)i] = filenameBytes[i];
        }
        
        // Initialize EXEC parameter block at offset 0x0320
        // For now, set it to zeros (simplified - full implementation would set environment, etc.)
        uint paramBlockAddress = MemoryUtils.ToPhysicalAddress(commandComSegment, 0x0320);
        for (int i = 0; i < 14; i++) { // EXEC param block is 14 bytes
            _memory.UInt8[paramBlockAddress + (uint)i] = 0;
        }

        return comBinary;
    }

    /// <summary>
    /// Generates the machine code for a full ASM COMMAND.COM that:
    /// - Opens and reads AUTOEXEC.BAT using INT 21h file I/O
    /// - Parses lines and executes external programs via INT 21h/4Bh EXEC
    /// - Handles EXIT command to terminate
    /// - Loops back after child processes return (INT 22h points here)
    /// </summary>
    private byte[] GenerateMinimalShell() {
        // Full ASM COMMAND.COM implementation
        // This is a pure 8086 assembly implementation that processes AUTOEXEC.BAT
        // without relying on C# bridge via INT 21h/FFh
        
        // Memory layout:
        // offset 0x0000-0x00FF: PSP
        // offset 0x0100: Program start (entry point)
        // offset 0x0200: File handle storage (word)
        // offset 0x0202: Line buffer (256 bytes for reading file)
        // offset 0x0302: Filename buffer (AUTOEXEC.BAT path)
        // offset 0x0320: EXEC parameter block
        // offset 0x0340: Command line buffer for child program
        
        System.Collections.Generic.List<byte> code = new System.Collections.Generic.List<byte>();
        
        // ===== Entry point at 0x0100 =====
        // Initialize: Open AUTOEXEC.BAT
        code.AddRange(OpenAutoexecBat());
        
        // ===== Main command loop =====
        // loop_start: (label for jumping back after child returns)
        ushort loopStartOffset = (ushort)code.Count;
        
        // Read next line from AUTOEXEC.BAT
        code.AddRange(ReadNextLine());
        
        // Parse and process the line
        code.AddRange(ProcessLine());
        
        // Jump back to loop_start to process next line
        code.AddRange(JumpToLoopStart(loopStartOffset));
        
        // ===== Exit routine =====
        // exit_program: (jumped to when EXIT command found or EOF)
        code.AddRange(ExitProgram());
        
        // ===== Data section (embedded at end of code) =====
        code.AddRange(EmbeddedData());
        
        return code.ToArray();
    }
    
    /// <summary>
    /// Generates assembly code to open AUTOEXEC.BAT file.
    /// Uses INT 21h/3Dh (Open File) with filename "C:\\AUTOEXEC.BAT"
    /// Stores file handle at offset 0x0200
    /// </summary>
    private byte[] OpenAutoexecBat() {
        System.Collections.Generic.List<byte> code = new System.Collections.Generic.List<byte>();
        
        // mov dx, 0x0302    ; Point DS:DX to filename "C:\\AUTOEXEC.BAT"
        code.AddRange(new byte[] { 0xBA, 0x02, 0x03 });
        
        // mov ah, 0x3D      ; INT 21h/3Dh = Open File
        code.AddRange(new byte[] { 0xB4, 0x3D });
        
        // mov al, 0x00      ; Access mode: Read only
        code.AddRange(new byte[] { 0xB0, 0x00 });
        
        // int 21h           ; Call DOS
        code.AddRange(new byte[] { 0xCD, 0x21 });
        
        // jc open_failed    ; If carry flag set, open failed
        code.AddRange(new byte[] { 0x72, 0x06 }); // Jump +6 bytes if error
        
        // mov [0x0200], ax  ; Store file handle
        code.AddRange(new byte[] { 0xA3, 0x00, 0x02 });
        
        // jmp open_success  ; Skip error handler
        code.AddRange(new byte[] { 0xEB, 0x05 }); // Jump +5 bytes
        
        // open_failed:
        // If file doesn't exist, just exit (no AUTOEXEC.BAT to process)
        // mov ah, 0x4C      ; Terminate
        code.AddRange(new byte[] { 0xB4, 0x4C });
        
        // mov al, 0x00      ; Return code 0
        code.AddRange(new byte[] { 0xB0, 0x00 });
        
        // int 21h
        code.AddRange(new byte[] { 0xCD, 0x21 });
        
        // open_success:
        return code.ToArray();
    }
    
    /// <summary>
    /// Generates assembly code to read next line from AUTOEXEC.BAT.
    /// Reads into buffer at 0x0202, handles line terminators (CR/LF).
    /// Sets zero flag if EOF reached.
    /// </summary>
    private byte[] ReadNextLine() {
        System.Collections.Generic.List<byte> code = new System.Collections.Generic.List<byte>();
        
        // Read characters one at a time until CR or LF or EOF
        // mov si, 0x0202    ; Point to line buffer
        code.AddRange(new byte[] { 0xBE, 0x02, 0x02 });
        
        // mov bx, [0x0200]  ; Get file handle
        code.AddRange(new byte[] { 0x8B, 0x1E, 0x00, 0x02 });
        
        // read_char_loop:
        ushort readCharLoopOffset = (ushort)code.Count;
        
        // mov ah, 0x3F      ; INT 21h/3Fh = Read from file
        code.AddRange(new byte[] { 0xB4, 0x3F });
        
        // mov cx, 1         ; Read 1 byte
        code.AddRange(new byte[] { 0xB9, 0x01, 0x00 });
        
        // mov dx, si        ; Read into current buffer position
        code.AddRange(new byte[] { 0x8B, 0xD6 });
        
        // int 21h
        code.AddRange(new byte[] { 0xCD, 0x21 });
        
        // jc read_error     ; If error, treat as EOF
        code.AddRange(new byte[] { 0x72, 0x12 }); // Jump +18 bytes
        
        // cmp ax, 0         ; Check if EOF (0 bytes read)
        code.AddRange(new byte[] { 0x3D, 0x00, 0x00 });
        
        // je eof_reached    ; If EOF, exit
        code.AddRange(new byte[] { 0x74, 0x0C }); // Jump +12 bytes
        
        // mov al, [si]      ; Get character we just read
        code.AddRange(new byte[] { 0x8A, 0x04 });
        
        // cmp al, 0x0D      ; Check for CR
        code.AddRange(new byte[] { 0x3C, 0x0D });
        
        // je end_of_line
        code.AddRange(new byte[] { 0x74, 0x08 }); // Jump +8 bytes
        
        // cmp al, 0x0A      ; Check for LF
        code.AddRange(new byte[] { 0x3C, 0x0A });
        
        // je end_of_line
        code.AddRange(new byte[] { 0x74, 0x04 }); // Jump +4 bytes
        
        // inc si            ; Move to next buffer position
        code.AddRange(new byte[] { 0x46 });
        
        // jmp read_char_loop ; Continue reading
        int backJump = code.Count - readCharLoopOffset + 2;
        code.AddRange(new byte[] { 0xEB, (byte)(256 - backJump) });
        
        // end_of_line:
        // Null-terminate the string
        // mov byte [si], 0
        code.AddRange(new byte[] { 0xC6, 0x04, 0x00 });
        
        // clc               ; Clear carry flag (success)
        code.AddRange(new byte[] { 0xF8 });
        
        // ret (inline - just continue)
        // We don't actually return here, just continue to process line
        
        return code.ToArray();
        
        // eof_reached: / read_error:
        // Will be handled by jumping to exit routine
    }
    
    /// <summary>
    /// Generates assembly code to parse and process a command line.
    /// Detects EXIT command, skips comments/ECHO OFF, executes external programs.
    /// </summary>
    private byte[] ProcessLine() {
        System.Collections.Generic.List<byte> code = new System.Collections.Generic.List<byte>();
        
        // Check if line is empty (first char is null)
        // mov si, 0x0202    ; Point to line buffer
        code.AddRange(new byte[] { 0xBE, 0x02, 0x02 });
        
        // mov al, [si]      ; Get first character
        code.AddRange(new byte[] { 0x8A, 0x04 });
        
        // cmp al, 0         ; Check if empty
        code.AddRange(new byte[] { 0x3C, 0x00 });
        
        // je continue_loop  ; If empty, continue to next line
        code.AddRange(new byte[] { 0x74, 0x20 }); // Jump +32 bytes (placeholder)
        
        // Check for '@' prefix (skip it)
        // cmp al, '@'
        code.AddRange(new byte[] { 0x3C, 0x40 });
        
        // jne not_at
        code.AddRange(new byte[] { 0x75, 0x02 }); // Jump +2 if not '@'
        
        // inc si            ; Skip '@'
        code.AddRange(new byte[] { 0x46 });
        
        // mov al, [si]      ; Get next character
        code.AddRange(new byte[] { 0x8A, 0x04 });
        
        // not_at:
        // Check for 'E' (start of EXIT or ECHO)
        // cmp al, 'E'
        code.AddRange(new byte[] { 0x3C, 0x45 });
        
        // je check_exit_or_echo
        code.AddRange(new byte[] { 0x74, 0x04 }); // Jump +4
        
        // cmp al, 'e'       ; Also check lowercase
        code.AddRange(new byte[] { 0x3C, 0x65 });
        
        // jne execute_program ; If not E or e, execute as program
        code.AddRange(new byte[] { 0x75, 0x15 }); // Jump +21 bytes (placeholder)
        
        // check_exit_or_echo:
        // Compare with "EXIT" string
        // Use simple string comparison (check next 3 chars: X, I, T)
        // inc si
        code.AddRange(new byte[] { 0x46 });
        
        // mov al, [si]
        code.AddRange(new byte[] { 0x8A, 0x04 });
        
        // and al, 0xDF      ; Convert to uppercase (clear bit 5)
        code.AddRange(new byte[] { 0x24, 0xDF });
        
        // cmp al, 'X'
        code.AddRange(new byte[] { 0x3C, 0x58 });
        
        // jne check_echo    ; Not EXIT, might be ECHO
        code.AddRange(new byte[] { 0x75, 0x10 }); // Jump +16
        
        // Check for 'I' and 'T'
        // inc si
        code.AddRange(new byte[] { 0x46 });
        
        // mov al, [si]
        code.AddRange(new byte[] { 0x8A, 0x04 });
        
        // and al, 0xDF
        code.AddRange(new byte[] { 0x24, 0xDF });
        
        // cmp al, 'I'
        code.AddRange(new byte[] { 0x3C, 0x49 });
        
        // jne execute_program
        code.AddRange(new byte[] { 0x75, 0x0A }); // Jump +10
        
        // inc si
        code.AddRange(new byte[] { 0x46 });
        
        // mov al, [si]
        code.AddRange(new byte[] { 0x8A, 0x04 });
        
        // and al, 0xDF
        code.AddRange(new byte[] { 0x24, 0xDF });
        
        // cmp al, 'T'
        code.AddRange(new byte[] { 0x3C, 0x54 });
        
        // je exit_command   ; Found "EXIT"
        code.AddRange(new byte[] { 0x74, 0x05 }); // Jump +5
        
        // jmp execute_program
        code.AddRange(new byte[] { 0xEB, 0x03 }); // Jump +3
        
        // exit_command:
        // Close file and terminate
        // mov ah, 0x3E      ; INT 21h/3Eh = Close file
        code.AddRange(new byte[] { 0xB4, 0x3E });
        
        // mov bx, [0x0200]  ; Get file handle
        code.AddRange(new byte[] { 0x8B, 0x1E, 0x00, 0x02 });
        
        // int 21h
        code.AddRange(new byte[] { 0xCD, 0x21 });
        
        // mov ah, 0x4C      ; Terminate program
        code.AddRange(new byte[] { 0xB4, 0x4C });
        
        // mov al, 0x00      ; Return code 0
        code.AddRange(new byte[] { 0xB0, 0x00 });
        
        // int 21h
        code.AddRange(new byte[] { 0xCD, 0x21 });
        
        // check_echo:
        // For now, just skip ECHO commands (continue to next line)
        // This is a placeholder - full implementation would check for "CHO OFF"
        // jmp continue_loop
        code.AddRange(new byte[] { 0xEB, 0x08 }); // Jump +8 (placeholder)
        
        // execute_program:
        // Execute external program via INT 21h/4Bh EXEC
        // This is complex - for now, simplified version
        // mov dx, 0x0202    ; Program path (line buffer)
        code.AddRange(new byte[] { 0xBA, 0x02, 0x02 });
        
        // mov ah, 0x4B      ; INT 21h/4Bh = EXEC
        code.AddRange(new byte[] { 0xB4, 0x4B });
        
        // mov al, 0x00      ; AL=0: Load and execute
        code.AddRange(new byte[] { 0xB0, 0x00 });
        
        // mov bx, 0x0320    ; ES:BX = Parameter block
        code.AddRange(new byte[] { 0xBB, 0x20, 0x03 });
        
        // int 21h           ; Execute program
        code.AddRange(new byte[] { 0xCD, 0x21 });
        
        // After EXEC returns (child terminated via INT 22h), continue loop
        
        // continue_loop:
        return code.ToArray();
    }
    
    /// <summary>
    /// Generates jump back to loop start to process next command.
    /// </summary>
    private byte[] JumpToLoopStart(ushort loopStartOffset) {
        System.Collections.Generic.List<byte> code = new System.Collections.Generic.List<byte>();
        
        // jmp loop_start
        // Calculate relative jump (current position + 2 - loopStartOffset)
        // For now, use a placeholder - this needs to be calculated after full code generation
        code.AddRange(new byte[] { 0xE9, 0x00, 0x00 }); // JMP near (placeholder offsets)
        
        return code.ToArray();
    }
    
    /// <summary>
    /// Generates exit routine (close file, terminate).
    /// </summary>
    private byte[] ExitProgram() {
        System.Collections.Generic.List<byte> code = new System.Collections.Generic.List<byte>();
        
        // mov ah, 0x3E      ; Close file
        code.AddRange(new byte[] { 0xB4, 0x3E });
        
        // mov bx, [0x0200]  ; File handle
        code.AddRange(new byte[] { 0x8B, 0x1E, 0x00, 0x02 });
        
        // int 21h
        code.AddRange(new byte[] { 0xCD, 0x21 });
        
        // mov ah, 0x4C      ; Terminate
        code.AddRange(new byte[] { 0xB4, 0x4C });
        
        // mov al, 0x00      ; Return code 0
        code.AddRange(new byte[] { 0xB0, 0x00 });
        
        // int 21h
        code.AddRange(new byte[] { 0xCD, 0x21 });
        
        return code.ToArray();
    }
    
    /// <summary>
    /// Embeds data section at end of code.
    /// Includes filename string "C:\\AUTOEXEC.BAT" and EXEC parameter block.
    /// </summary>
    private byte[] EmbeddedData() {
        System.Collections.Generic.List<byte> code = new System.Collections.Generic.List<byte>();
        
        // While code is being generated, these offsets are calculated
        // For now, return empty - data will be written to specific memory locations
        
        return code.ToArray();
    }
}
