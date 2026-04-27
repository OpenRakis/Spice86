namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Shared.Utils;

using System;
using System.IO;
using System.Text;

/// <summary>
/// Shared infrastructure for DOS batch integration tests.
/// Provides COM program builders, Spice86 runners, and temp directory management.
/// </summary>
internal static class BatchTestHelpers {

    internal static char[] RunAndCaptureVideoCells(string executablePath, string cDrivePath, int cellCount,
        string? exeArgs = null) {
        using Spice86DependencyInjection spice86 = new Spice86Creator(
            binName: executablePath,
            enablePit: true,
            maxCycles: 300000,
            installInterruptVectors: true,
            cDrive: cDrivePath,
            exeArgs: exeArgs).Create();

        spice86.ProgramExecutor.Run();

        char[] cells = new char[cellCount];
        for (int i = 0; i < cellCount; i++) {
            uint videoAddress = MemoryUtils.ToPhysicalAddress(0xB800, (ushort)(i * 2));
            cells[i] = (char)spice86.Machine.Memory.UInt8[videoAddress];
        }

        return cells;
    }

    internal static char RunAndCaptureVideoCell(string executablePath, string cDrivePath,
        string? exeArgs = null) {
        return RunAndCaptureVideoCells(executablePath, cDrivePath, 1, exeArgs)[0];
    }

    internal static void RunAndAssertVideoCell(string executablePath, string cDrivePath, char expectedChar) {
        char[] cells = RunAndCaptureVideoCells(executablePath, cDrivePath, 1);
        cells[0].Should().Be(expectedChar);
    }

    internal static void RunWithoutVideoRead(string executablePath, string cDrivePath, bool enablePit = true) {
        using Spice86DependencyInjection spice86 = new Spice86Creator(
            binName: executablePath,
            enablePit: enablePit,
            maxCycles: 300000,
            installInterruptVectors: true,
            cDrive: cDrivePath).Create();

        spice86.ProgramExecutor.Run();
    }

    internal static void RunBatchScript(string cDrivePath, string script) {
        string startBatchPath = WriteStartBatchScript(cDrivePath, script);
        RunWithoutVideoRead(startBatchPath, cDrivePath);
    }

    internal static byte[] RunWithPreloadedKeysAndCaptureVideoBytes(string executablePath, string cDrivePath,
        int byteCount, ushort[] keyCodes) {
        using Spice86DependencyInjection spice86 = new Spice86Creator(
            binName: executablePath,
            enablePit: true,
            maxCycles: 300000,
            installInterruptVectors: true,
            cDrive: cDrivePath).Create();

        BiosKeyboardBuffer buffer = spice86.Machine.BiosKeyboardInt9Handler.BiosKeyboardBuffer;
        for (int i = 0; i < keyCodes.Length; i++) {
            buffer.EnqueueKeyCode(keyCodes[i]);
        }

        spice86.ProgramExecutor.Run();

        byte[] bytes = new byte[byteCount];
        for (int i = 0; i < byteCount; i++) {
            uint videoAddress = MemoryUtils.ToPhysicalAddress(0xB800, (ushort)(i * 2));
            bytes[i] = spice86.Machine.Memory.UInt8[videoAddress];
        }

        return bytes;
    }

    internal static char[] RunWithPreloadedKeysAndCaptureVideoCells(string executablePath, string cDrivePath,
        int cellCount, ushort[] keyCodes) {
        byte[] bytes = RunWithPreloadedKeysAndCaptureVideoBytes(executablePath, cDrivePath, cellCount, keyCodes);
        char[] cells = new char[cellCount];
        for (int i = 0; i < cellCount; i++) {
            cells[i] = (char)bytes[i];
        }

        return cells;
    }

    internal static void AssertFirstVideoByteWithPreloadedKeys(string executablePath, string cDrivePath,
        ushort[] keyCodes, byte expected, string because) {
        byte[] bytes = RunWithPreloadedKeysAndCaptureVideoBytes(executablePath, cDrivePath, 1, keyCodes);
        bytes[0].Should().Be(expected, because);
    }

    internal static void AssertFirstVideoByteFromScript(string cDrivePath, string script,
        byte expected, string because) {
        string startBatchPath = WriteStartBatchScript(cDrivePath, script);
        char firstCell = RunAndCaptureVideoCell(startBatchPath, cDrivePath);
        byte firstByte = (byte)firstCell;
        firstByte.Should().Be(expected, because);
    }

    internal static void RunAndAssertVideoCellFromScript(string cDrivePath, string script, char expectedChar) {
        string startBatchPath = WriteStartBatchScript(cDrivePath, script);
        RunAndAssertVideoCell(startBatchPath, cDrivePath, expectedChar);
    }

    internal static void RunAndAssertVideoCellNotWrittenFromScript(string cDrivePath, string script,
        char unexpectedChar) {
        string startBatchPath = WriteStartBatchScript(cDrivePath, script);
        char[] cells = RunAndCaptureVideoCells(startBatchPath, cDrivePath, 1);
        cells[0].Should().NotBe(unexpectedChar);
    }

    internal static void RunAndAssertVideoCellsFromScript(string cDrivePath, string script,
        char[] expectedChars) {
        string startBatchPath = WriteStartBatchScript(cDrivePath, script);
        char[] cells = RunAndCaptureVideoCells(startBatchPath, cDrivePath, expectedChars.Length);
        for (int i = 0; i < expectedChars.Length; i++) {
            cells[i].Should().Be(expectedChars[i], $"video cell {i} should match");
        }
    }

    private static string WriteStartBatchScript(string cDrivePath, string script) {
        string startBatchPath = Path.Join(cDrivePath, "START.BAT");
        File.WriteAllText(startBatchPath, script);
        return startBatchPath;
    }


    internal static byte[] BuildVideoWriterCom(char value, ushort videoOffset) {
        return new byte[] {
            0xB8, 0x00, 0xB8,                                               // MOV AX, B800h
            0x8E, 0xC0,                                                     // MOV ES, AX
            0xBF, (byte)(videoOffset & 0xFF), (byte)(videoOffset >> 8),     // MOV DI, videoOffset
            0xB0, (byte)value,                                              // MOV AL, value
            0xB4, 0x07,                                                     // MOV AH, 07h (attribute)
            0xAB,                                                           // STOSW
            0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
            0xCD, 0x21                                                      // INT 21h
        };
    }

    internal static byte[] BuildExitCodeCom(byte exitCode) {
        return new byte[] {
            0xB8, exitCode, 0x4C,                                           // MOV AX, 4C##h  (AH=4Ch terminate, AL=exitCode)
            0xCD, 0x21                                                      // INT 21h
        };
    }

    /// <summary>
    /// Builds a COM program that calls IOCTL Get Input Status (INT 21h AH=44h AL=06h) on stdin (handle 0),
    /// then writes the AL result byte to video memory at the given offset.
    /// AL=0xFF means input available, AL=0x00 means no input.
    /// </summary>
    internal static byte[] BuildIoctlInputStatusCom(ushort videoOffset) {
        return BuildIoctlStatusCom(0x06, 0, videoOffset);
    }

    internal static byte[] BuildIoctlStatusCom(byte ioctlSubFunction, ushort handle, ushort videoOffset) {
        // mov ah, 44h
        // mov al, ioctlSubFunction
        // mov bx, handle
        // int 21h            ; AL = status byte
        // mov ah, 07h        ; video attribute
        // mov es, 0B800h
        // mov di, videoOffset
        // stosw              ; writes AL + AH
        // mov ax, 4C00h
        // int 21h
        return new byte[] {
            0xB4, 0x44,                                                     // MOV AH, 44h
            0xB0, ioctlSubFunction,                                         // MOV AL, subfunction
            0xBB, (byte)(handle & 0xFF), (byte)(handle >> 8),               // MOV BX, handle
            0xCD, 0x21,                                                     // INT 21h
            0xB4, 0x07,                                                     // MOV AH, 07h (attribute)
            0xBB, 0x00, 0xB8,                                               // MOV BX, B800h
            0x8E, 0xC3,                                                     // MOV ES, BX
            0xBF, (byte)(videoOffset & 0xFF), (byte)(videoOffset >> 8),     // MOV DI, videoOffset
            0xAB,                                                           // STOSW
            0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
            0xCD, 0x21                                                      // INT 21h
        };
    }

    /// <summary>
    /// Builds a COM program that polls IOCTL 06 (Get Input Status) on stdin in a loop,
    /// then reads a character via INT 21h AH=07h (direct stdin without echo) when input is available,
    /// and writes the read character to video memory. This demonstrates the real-world CHOICE-like
    /// polling pattern that requires correct IOCTL 06 behavior.
    /// </summary>
    internal static byte[] BuildIoctlPollAndReadCom(ushort videoOffset) {
        // poll_loop:
        //   MOV AH, 44h       ; IOCTL function
        //   MOV AL, 06h       ; Get Input Status
        //   XOR BX, BX        ; Handle 0 (stdin)
        //   INT 21h           ; Call DOS — AL has status
        //   OR AL, AL         ; Test if 0
        //   JZ poll_loop      ; If no input, keep polling
        //   MOV AH, 07h       ; Direct stdin without echo
        //   INT 21h           ; Read character → AL
        //   MOV AH, 07h       ; Video attribute
        //   MOV BX, B800h
        //   MOV ES, BX
        //   MOV DI, videoOffset
        //   STOSW
        //   MOV AX, 4C00h
        //   INT 21h
        return new byte[] {
            // poll_loop (offset 0):
            0xB4, 0x44,                                                     // MOV AH, 44h
            0xB0, 0x06,                                                     // MOV AL, 06h
            0x31, 0xDB,                                                     // XOR BX, BX
            0xCD, 0x21,                                                     // INT 21h
            0x08, 0xC0,                                                     // OR AL, AL
            0x74, 0xF4,                                                     // JZ poll_loop (-12 → offset 0)
            // read_key:
            0xB4, 0x07,                                                     // MOV AH, 07h
            0xCD, 0x21,                                                     // INT 21h (read char → AL)
            0xB4, 0x07,                                                     // MOV AH, 07h (attribute)
            0xBB, 0x00, 0xB8,                                               // MOV BX, B800h
            0x8E, 0xC3,                                                     // MOV ES, BX
            0xBF, (byte)(videoOffset & 0xFF), (byte)(videoOffset >> 8),     // MOV DI, videoOffset
            0xAB,                                                           // STOSW
            0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
            0xCD, 0x21                                                      // INT 21h
        };
    }

    /// <summary>
    /// Builds a COM program that reads a character via INT 21h with the given AH function
    /// (e.g. 0x07 for Direct Standard Input Without Echo, 0x08 for Direct Standard Input Without Echo
    /// checking for Ctrl-Break) and writes it to video memory at the given offset.
    /// </summary>
    internal static byte[] BuildConsoleReadToVideoCom(byte ahFunction, ushort videoOffset) {
        return new byte[] {
            0xB4, ahFunction,                                               // MOV AH, ahFunction
            0xCD, 0x21,                                                     // INT 21h
            0xB4, 0x07,                                                     // MOV AH, 07h (attribute)
            0xBB, 0x00, 0xB8,                                               // MOV BX, B800h
            0x8E, 0xC3,                                                     // MOV ES, BX
            0xBF, (byte)(videoOffset & 0xFF), (byte)(videoOffset >> 8),     // MOV DI, videoOffset
            0xAB,                                                           // STOSW
            0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
            0xCD, 0x21                                                      // INT 21h
        };
    }

    /// <summary>
    /// Builds a COM program that reads a character via INT 21h AH=07h (Direct Standard Input Without Echo)
    /// and writes it to video memory at the given offset.
    /// </summary>
    internal static byte[] BuildDirectInputToVideoCom(ushort videoOffset) {
        return BuildConsoleReadToVideoCom(0x07, videoOffset);
    }

    /// <summary>
    /// Builds a COM program that reads a character via INT 21h AH=01h (Character Input With Echo)
    /// and writes it to video memory at the given offset.
    /// </summary>
    internal static byte[] BuildCharInputWithEchoToVideoCom(ushort videoOffset) {
        return BuildConsoleReadToVideoCom(0x01, videoOffset);
    }

    /// <summary>
    /// Builds a COM program that calls IOCTL Get Device Information (INT 21h AH=44h AL=00h) on the given handle,
    /// then writes DL (low byte of device info) and DH (high byte) to video memory at consecutive offsets.
    /// </summary>
    internal static byte[] BuildIoctlDeviceInfoCom(ushort handle, ushort videoOffset) {
        return new byte[] {
            0xB4, 0x44,                                                     // MOV AH, 44h
            0xB0, 0x00,                                                     // MOV AL, 00h (Get Device Info)
            0xBB, (byte)(handle & 0xFF), (byte)(handle >> 8),               // MOV BX, handle
            0xCD, 0x21,                                                     // INT 21h — result in DX
            0x88, 0xD0,                                                     // MOV AL, DL
            0xB4, 0x07,                                                     // MOV AH, 07h (attribute)
            0xBB, 0x00, 0xB8,                                               // MOV BX, B800h
            0x8E, 0xC3,                                                     // MOV ES, BX
            0xBF, (byte)(videoOffset & 0xFF), (byte)(videoOffset >> 8),     // MOV DI, videoOffset
            0xAB,                                                           // STOSW (writes DL + attr)
            0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
            0xCD, 0x21                                                      // INT 21h
        };
    }

    /// <summary>
    /// Builds a COM program that calls IOCTL Get Output Status (INT 21h AH=44h AL=07h) on the given handle,
    /// then writes the AL result byte to video memory at the given offset.
    /// AL=0xFF means output ready, AL=0x00 means not ready.
    /// </summary>
    internal static byte[] BuildIoctlOutputStatusCom(ushort handle, ushort videoOffset) {
        return BuildIoctlStatusCom(0x07, handle, videoOffset);
    }

    /// <summary>
    /// Builds a COM program that reads 1 byte from stdin (AH=3Fh handle 0), then calls IOCTL 06 on stdin,
    /// and writes the IOCTL result to video memory. Used to test IOCTL 06 on file handles at EOF.
    /// </summary>
    internal static byte[] BuildReadThenIoctlInputStatusCom(ushort videoOffset) {
        // Data buffer for the 1-byte read is placed after the code at offset 0x100 + codeLength
        const int codeLength = 37;
        ushort bufferOffset = (ushort)(0x100 + codeLength);
        return new byte[] {
            // Read 1 byte from stdin (handle 0)
            0xBB, 0x00, 0x00,                                               // MOV BX, 0000h (stdin)
            0xBA, (byte)(bufferOffset & 0xFF), (byte)(bufferOffset >> 8),   // MOV DX, bufferOffset
            0xB9, 0x01, 0x00,                                               // MOV CX, 0001h (1 byte)
            0xB4, 0x3F,                                                     // MOV AH, 3Fh (Read)
            0xCD, 0x21,                                                     // INT 21h
            // IOCTL 06 on stdin
            0xB4, 0x44,                                                     // MOV AH, 44h
            0xB0, 0x06,                                                     // MOV AL, 06h (Get Input Status)
            0x31, 0xDB,                                                     // XOR BX, BX (stdin)
            0xCD, 0x21,                                                     // INT 21h — result in AL
            // Write AL to video
            0xB4, 0x07,                                                     // MOV AH, 07h (attribute)
            0xBB, 0x00, 0xB8,                                               // MOV BX, B800h
            0x8E, 0xC3,                                                     // MOV ES, BX
            0xBF, (byte)(videoOffset & 0xFF), (byte)(videoOffset >> 8),     // MOV DI, videoOffset
            0xAB,                                                           // STOSW
            0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
            0xCD, 0x21,                                                     // INT 21h
            0x00                                                            // Data buffer (1 byte)
        };
    }

    /// <summary>
    /// Builds a COM program that performs INT 21h AH=06h (Direct Console I/O) with DL=0xFF (input mode),
    /// then writes the AL result byte to video memory at the given offset.
    /// </summary>
    internal static byte[] BuildDirectConsoleIoInputCom(ushort videoOffset) {
        return BuildDirectConsoleIoCom(0xFF, true, videoOffset);
    }

    private static byte[] BuildDirectConsoleIoCom(byte dl, bool writeResultToVideo, ushort videoOffset) {
        // mov ah, 06h
        // mov dl, value       ; DL=FFh -> input mode, otherwise output mode
        // int 21h             ; input mode: AL has char/status
        if (!writeResultToVideo) {
            return new byte[] {
                0xB4, 0x06,                                                 // MOV AH, 06h
                0xB2, dl,                                                   // MOV DL, value
                0xCD, 0x21,                                                 // INT 21h
                0xB8, 0x00, 0x4C,                                           // MOV AX, 4C00h
                0xCD, 0x21                                                  // INT 21h
            };
        }

        // mov ah, 07h         ; video attribute
        // mov es, 0B800h
        // mov di, videoOffset
        // stosw               ; AL + AH
        // mov ax, 4C00h
        // int 21h
        return new byte[] {
            0xB4, 0x06,                                                     // MOV AH, 06h
            0xB2, dl,                                                       // MOV DL, value
            0xCD, 0x21,                                                     // INT 21h
            0xB4, 0x07,                                                     // MOV AH, 07h (attribute)
            0xBB, 0x00, 0xB8,                                               // MOV BX, B800h
            0x8E, 0xC3,                                                     // MOV ES, BX
            0xBF, (byte)(videoOffset & 0xFF), (byte)(videoOffset >> 8),     // MOV DI, videoOffset
            0xAB,                                                           // STOSW
            0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
            0xCD, 0x21                                                      // INT 21h
        };
    }

    /// <summary>
    /// Builds a COM program that writes a single character to stdout via INT 21h AH=06h
    /// (Direct Console I/O, output mode: DL != 0xFF), then exits.
    /// </summary>
    internal static byte[] BuildDirectConsoleIoOutputCom(byte character) {
        return BuildDirectConsoleIoCom(character, false, 0);
    }

    /// <summary>
    /// Builds a COM program that:
    /// 1) calls INT 21h AH=02h (Display Output) with the supplied character in DL,
    /// 2) then writes a sentinel byte directly to video memory,
    /// 3) then exits.
    ///
    /// If AH=02h performs a Ctrl-C/Ctrl-Break check and triggers INT 23h,
    /// step 2 is never reached and the sentinel is not written.
    /// </summary>
    internal static byte[] BuildDisplayOutputThenWriteSentinelCom(byte outputCharacter, byte sentinel) {
        return new byte[] {
            0xB2, outputCharacter,                                           // MOV DL, outputCharacter
            0xB4, 0x02,                                                      // MOV AH, 02h (Display Output)
            0xCD, 0x21,                                                      // INT 21h
            0xB0, sentinel,                                                  // MOV AL, sentinel
            0xB4, 0x07,                                                      // MOV AH, 07h (attribute)
            0xBB, 0x00, 0xB8,                                                // MOV BX, B800h
            0x8E, 0xC3,                                                      // MOV ES, BX
            0xBF, 0x00, 0x00,                                                // MOV DI, 0000h (cell 0)
            0xAB,                                                            // STOSW
            0xB8, 0x00, 0x4C,                                                // MOV AX, 4C00h
            0xCD, 0x21                                                       // INT 21h
        };
    }

    /// <summary>
    /// Builds a COM program that calls INT 21h with the provided AH value,
    /// then writes a sentinel byte directly to the first text video cell.
    /// If the INT 21h call triggers break handling, sentinel write is never reached.
    /// </summary>
    internal static byte[] BuildInt21CallThenWriteSentinelCom(byte ah, byte sentinel) {
        return new byte[] {
            0xB4, ah,                                                        // MOV AH, ah
            0xCD, 0x21,                                                      // INT 21h
            0xB0, sentinel,                                                  // MOV AL, sentinel
            0xB4, 0x07,                                                      // MOV AH, 07h (attribute)
            0xBB, 0x00, 0xB8,                                                // MOV BX, B800h
            0x8E, 0xC3,                                                      // MOV ES, BX
            0xBF, 0x00, 0x00,                                                // MOV DI, 0000h (cell 0)
            0xAB,                                                            // STOSW
            0xB8, 0x00, 0x4C,                                                // MOV AX, 4C00h
            0xCD, 0x21                                                       // INT 21h
        };
    }

    /// <summary>
    /// Builds a COM program that writes AL via AH=09h (DS:DX -> "$" terminated string),
    /// then writes a sentinel to video cell 0.
    /// </summary>
    internal static byte[] BuildPrintStringThenWriteSentinelCom(string text, byte sentinel) {
        // MOV DX, dataOffset    ; DS:DX -> $-terminated string
        // MOV AH, 09h
        // INT 21h               ; print string
        // MOV AL, sentinel
        // MOV AH, 07h           ; video attribute
        // MOV BX, B800h
        // MOV ES, BX
        // MOV DI, 0000h         ; cell 0
        // STOSW                 ; write sentinel + attr
        // RET                   ; terminate
        // [text + "$" + NUL]
        byte[] textBytes = Encoding.ASCII.GetBytes(text + "$\0");
        const int codeLength = 21;
        ushort dataOffset = (ushort)(0x100 + codeLength);
        byte[] code = new byte[] {
            0xBA, (byte)(dataOffset & 0xFF), (byte)(dataOffset >> 8),       // MOV DX, dataOffset
            0xB4, 0x09,                                                     // MOV AH, 09h (Print String)
            0xCD, 0x21,                                                     // INT 21h
            0xB0, sentinel,                                                 // MOV AL, sentinel
            0xB4, 0x07,                                                     // MOV AH, 07h (attribute)
            0xBB, 0x00, 0xB8,                                               // MOV BX, B800h
            0x8E, 0xC3,                                                     // MOV ES, BX
            0xBF, 0x00, 0x00,                                               // MOV DI, 0000h (cell 0)
            0xAB,                                                           // STOSW
            0xC3                                                            // RET
        };
        byte[] machineCode = new byte[code.Length + textBytes.Length];
        Array.Copy(code, machineCode, code.Length);
        Array.Copy(textBytes, 0, machineCode, code.Length, textBytes.Length);
        return machineCode;
    }

    /// <summary>
    /// Builds a COM program that executes AH=0Ch with AL=06h (flush + direct console input status),
    /// then writes AL to video memory.
    /// </summary>
    internal static byte[] BuildAh0CFlushThenAh06InputStatusToVideoCom(ushort videoOffset) {
        return new byte[] {
            0xB0, 0x06,                                                     // MOV AL, 06h (subfunction)
            0xB2, 0xFF,                                                     // MOV DL, FFh (AH=06h input mode)
            0xB4, 0x0C,                                                     // MOV AH, 0Ch
            0xCD, 0x21,                                                     // INT 21h
            0xB4, 0x07,                                                     // MOV AH, 07h (attribute)
            0xBB, 0x00, 0xB8,                                               // MOV BX, B800h
            0x8E, 0xC3,                                                     // MOV ES, BX
            0xBF, (byte)(videoOffset & 0xFF), (byte)(videoOffset >> 8),     // MOV DI, videoOffset
            0xAB,                                                           // STOSW
            0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
            0xCD, 0x21                                                      // INT 21h
        };
    }

    /// <summary>
    /// Builds a COM program that calls IOCTL AH=44h with the given AL subfunction,
    /// then writes two bytes to video:
    /// - byte 0: Carry Flag (0 or 1)
    /// - byte 1: AX low byte returned by INT 21h
    /// </summary>
    internal static byte[] BuildIoctlUnsupportedFunctionProbeCom(byte subFunction) {
        return new byte[] {
            0xB4, 0x44,                                                     // MOV AH, 44h
            0xB0, subFunction,                                              // MOV AL, subFunction
            0x31, 0xDB,                                                     // XOR BX, BX
            0xCD, 0x21,                                                     // INT 21h
            0x88, 0xC2,                                                     // MOV DL, AL (save AX low byte)
            0x9C,                                                           // PUSHF
            0x58,                                                           // POP AX
            0x24, 0x01,                                                     // AND AL, 01h (CF)
            0xB4, 0x07,                                                     // MOV AH, 07h
            0xBB, 0x00, 0xB8,                                               // MOV BX, B800h
            0x8E, 0xC3,                                                     // MOV ES, BX
            0xBF, 0x00, 0x00,                                               // MOV DI, 0000h
            0xAB,                                                           // STOSW (CF)
            0x8A, 0xC2,                                                     // MOV AL, DL (error code)
            0xB4, 0x07,                                                     // MOV AH, 07h
            0xAB,                                                           // STOSW (error code)
            0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
            0xCD, 0x21                                                      // INT 21h
        };
    }

    /// <summary>
    /// Builds a COM program that prints a $-terminated string via INT 21h AH=09h, then exits.
    /// The string data is placed immediately after the code.
    /// </summary>
    internal static byte[] BuildPrintDollarStringCom(string text) {
        // MOV DX, dataOffset    ; DS:DX -> $-terminated string
        // MOV AH, 09h
        // INT 21h               ; print until '$'
        // RET                   ; terminate (PSP INT 20h via RET)
        // [text + "$"]
        byte[] textBytes = Encoding.ASCII.GetBytes(text + "$");
        const int codeLength = 8;
        ushort dataOffset = (ushort)(0x100 + codeLength);
        byte[] code = new byte[] {
            0xBA, (byte)(dataOffset & 0xFF), (byte)(dataOffset >> 8),       // MOV DX, dataOffset
            0xB4, 0x09,                                                     // MOV AH, 09h (Print String)
            0xCD, 0x21,                                                     // INT 21h
            0xC3                                                            // RET
        };
        byte[] machineCode = new byte[code.Length + textBytes.Length];
        Array.Copy(code, machineCode, code.Length);
        Array.Copy(textBytes, 0, machineCode, code.Length, textBytes.Length);
        return machineCode;
    }

    /// <summary>
    /// Builds a COM program that calls INT 21h AH=0Bh (Check Standard Input Status),
    /// then writes the AL result byte to video memory at the given offset.
    /// AL=0xFF means input available, AL=0x00 means no input.
    /// </summary>
    internal static byte[] BuildCheckStdinStatusCom(ushort videoOffset) {
        return new byte[] {
            0xB4, 0x0B,                                                     // MOV AH, 0Bh
            0xCD, 0x21,                                                     // INT 21h — result in AL
            0xB4, 0x07,                                                     // MOV AH, 07h (attribute)
            0xBB, 0x00, 0xB8,                                               // MOV BX, B800h
            0x8E, 0xC3,                                                     // MOV ES, BX
            0xBF, (byte)(videoOffset & 0xFF), (byte)(videoOffset >> 8),     // MOV DI, videoOffset
            0xAB,                                                           // STOSW
            0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
            0xCD, 0x21                                                      // INT 21h
        };
    }

    internal static byte[] BuildStdoutWriterCom(string text) {
        // MOV BX, 0001h         ; handle 1 = stdout
        // MOV DX, dataOffset    ; DS:DX -> text data
        // MOV CX, len(text)     ; byte count
        // MOV AH, 40h           ; Write File/Device
        // INT 21h
        // MOV AX, 4C00h
        // INT 21h
        // [text data]
        byte[] ascii = Encoding.ASCII.GetBytes(text);
        const int codeLength = 18;
        ushort dataOffset = (ushort)(0x100 + codeLength);
        byte[] code = new byte[] {
            0xBB, 0x01, 0x00,                                               // MOV BX, 0001h (stdout)
            0xBA, (byte)(dataOffset & 0xFF), (byte)(dataOffset >> 8),       // MOV DX, dataOffset
            0xB9, (byte)ascii.Length, 0x00,                                 // MOV CX, length
            0xB4, 0x40,                                                     // MOV AH, 40h (Write)
            0xCD, 0x21,                                                     // INT 21h
            0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
            0xCD, 0x21                                                      // INT 21h
        };
        byte[] machineCode = new byte[code.Length + ascii.Length];
        Array.Copy(code, machineCode, code.Length);
        Array.Copy(ascii, 0, machineCode, code.Length, ascii.Length);
        return machineCode;
    }

    internal static byte[] BuildStdinToStdoutCom() {
        // MOV BX, 0000h         ; handle 0 = stdin
        // MOV DX, dataOffset    ; DS:DX -> 1-byte buffer
        // MOV CX, 0001h
        // MOV AH, 3Fh           ; Read File/Device
        // INT 21h
        // MOV BX, 0001h         ; handle 1 = stdout
        // MOV DX, dataOffset    ; same buffer
        // MOV CX, 0001h
        // MOV AH, 40h           ; Write File/Device
        // INT 21h
        // MOV AX, 4C00h
        // INT 21h
        // [1-byte buffer]
        const int codeLength = 31;
        ushort dataOffset = (ushort)(0x100 + codeLength);
        byte[] code = new byte[] {
            0xBB, 0x00, 0x00,                                               // MOV BX, 0000h (stdin)
            0xBA, (byte)(dataOffset & 0xFF), (byte)(dataOffset >> 8),       // MOV DX, dataOffset
            0xB9, 0x01, 0x00,                                               // MOV CX, 0001h (1 byte)
            0xB4, 0x3F,                                                     // MOV AH, 3Fh (Read)
            0xCD, 0x21,                                                     // INT 21h
            0xBB, 0x01, 0x00,                                               // MOV BX, 0001h (stdout)
            0xBA, (byte)(dataOffset & 0xFF), (byte)(dataOffset >> 8),       // MOV DX, dataOffset
            0xB9, 0x01, 0x00,                                               // MOV CX, 0001h (1 byte)
            0xB4, 0x40,                                                     // MOV AH, 40h (Write)
            0xCD, 0x21,                                                     // INT 21h
            0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
            0xCD, 0x21                                                      // INT 21h
        };
        byte[] machineCode = new byte[code.Length + 1];                     // +1 for data buffer
        Array.Copy(code, machineCode, code.Length);
        return machineCode;
    }

    internal static byte[] BuildStdinToVideoWriterCom(ushort videoOffset) {
        // MOV BX, 0000h         ; handle 0 = stdin
        // MOV DX, dataOffset    ; DS:DX -> 1-byte buffer
        // MOV CX, 0001h
        // MOV AH, 3Fh           ; Read File/Device
        // INT 21h
        // MOV AX, B800h
        // MOV ES, AX            ; ES -> video segment
        // MOV DI, videoOffset
        // MOV AL, [dataOffset]  ; load byte read from stdin
        // MOV AH, 07h           ; video attribute
        // STOSW                 ; write char + attr
        // MOV AX, 4C00h
        // INT 21h
        // [1-byte buffer]
        const int codeLength = 32;
        ushort dataOffset = (ushort)(0x100 + codeLength);
        byte[] code = new byte[] {
            0xBB, 0x00, 0x00,                                               // MOV BX, 0000h (stdin)
            0xBA, (byte)(dataOffset & 0xFF), (byte)(dataOffset >> 8),       // MOV DX, dataOffset
            0xB9, 0x01, 0x00,                                               // MOV CX, 0001h (1 byte)
            0xB4, 0x3F,                                                     // MOV AH, 3Fh (Read)
            0xCD, 0x21,                                                     // INT 21h
            0xB8, 0x00, 0xB8,                                               // MOV AX, B800h
            0x8E, 0xC0,                                                     // MOV ES, AX
            0xBF, (byte)(videoOffset & 0xFF), (byte)(videoOffset >> 8),     // MOV DI, videoOffset
            0xA0, (byte)(dataOffset & 0xFF), (byte)(dataOffset >> 8),       // MOV AL, [dataOffset]
            0xB4, 0x07,                                                     // MOV AH, 07h (attribute)
            0xAB,                                                           // STOSW
            0xB8, 0x00, 0x4C,                                               // MOV AX, 4C00h
            0xCD, 0x21                                                      // INT 21h
        };
        byte[] machineCode = new byte[code.Length + 1];                     // +1 for data buffer
        Array.Copy(code, machineCode, code.Length);
        return machineCode;
    }


    internal static string CreateBinaryFile(string directoryPath, string fileName, byte[] content) {
        string filePath = Path.Join(directoryPath, fileName);
        File.WriteAllBytes(filePath, content);
        return filePath;
    }

    internal static string CreateTextFile(string directoryPath, string fileName, string content) {
        string filePath = Path.Join(directoryPath, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    internal static string CreateDirectoryPath(string rootPath, params string[] segments) {
        string directoryPath = Path.Join([rootPath, .. segments]);
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    internal static string ResolveExistingPath(params string[] candidatePaths) {
        string? existingPath = candidatePaths.FirstOrDefault(File.Exists);
        if (existingPath is null) {
            throw new FileNotFoundException(
                $"Could not find any expected file. Candidates: {string.Join(", ", candidatePaths)}");
        }

        return existingPath;
    }

    private static string SingleTempPath => Path.GetTempPath();

    internal static void WithTempDirectory(string prefix, Action<string> test) {
        string tempDir = Path.Join(SingleTempPath, $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
            test(tempDir);
        } finally {
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
