namespace Spice86.Tests.Bios;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration tests for keyboard input through INT9H (hardware interrupt) and INT16H (BIOS services).
/// Tests run inline x86 machine code through the emulation stack.
/// </summary>
public class KeyboardIntegrationTests {
    private const int ResultPort = 0x999;
    private const int DetailsPort = 0x998;

    enum TestResult : byte {
        Success = 0x00,
        Failure = 0xFF
    }

    /// <summary>
    /// Tests that INT16H function 00h (read character) properly receives keys from keyboard buffer.
    /// Verifies the entire chain: KbdKey -> scancode -> INT9H -> buffer -> INT16H
    /// </summary>
    [Fact]
    public void Int16H_ReadChar_ShouldReceiveKeyFromBuffer() {
        // Program that reads one character using INT 16h, AH=00h
        // and reports the scan code and ASCII code via I/O ports
        byte[] program = new byte[]
        {
            0xB4, 0x00,             // mov ah, 00h - Read character (wait)
            0xCD, 0x16,             // int 16h - Returns: AH=scan code, AL=ASCII
            
            // Save ASCII code to BL
            0x88, 0xC3,             // mov bl, al - save ASCII to BL
            
            // Write scan code (AH) to details port
            0x88, 0xE0,             // mov al, ah - copy scan code to AL
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al
            
            // Write ASCII code (from BL) to result port
            0x88, 0xD8,             // mov al, bl - restore ASCII from BL
            0xBA, 0x99, 0x09,       // mov dx, ResultPort  
            0xEE,                   // out dx, al - write ASCII code
            0xF4                    // hlt
        };

        KeyboardTestHandler testHandler = RunKeyboardTest(program, setupKeys: (ps2kbd) => {
            // Simulate pressing and releasing the 'A' key
            ps2kbd.AddKey(KbdKey.A, isPressed: true);
            ps2kbd.AddKey(KbdKey.A, isPressed: false);
        });

        // The scan code for 'A' is 0x1E (from KeyboardScancodeConverter)
        // The ASCII code for lowercase 'a' is 0x61
        testHandler.Details.Should().Contain(0x1E,
            "INT 16h should return scan code 0x1E for 'A' key");
        testHandler.Results.Should().Contain(0x61,
            "INT 16h should return ASCII code 0x61 for lowercase 'a'");
    }

    /// <summary>
    /// Tests that various letter keys produce correct scan codes
    /// </summary>
    [Theory]
    [InlineData(KbdKey.A, 0x1E, 0x61)] // A key -> scan 0x1E, ASCII 'a'
    [InlineData(KbdKey.B, 0x30, 0x62)] // B key -> scan 0x30, ASCII 'b'
    [InlineData(KbdKey.Q, 0x10, 0x71)] // Q key -> scan 0x10, ASCII 'q'
    [InlineData(KbdKey.Z, 0x2C, 0x7A)] // Z key -> scan 0x2C, ASCII 'z'
    public void Int16H_ReadChar_ShouldProduceCorrectScancodeForLetters(
        KbdKey key, byte expectedScanCode, byte expectedAscii) {
        byte[] program = new byte[]
        {
            0xB4, 0x00,             // mov ah, 00h - Read character
            0xCD, 0x16,             // int 16h
            0x88, 0xC3,             // mov bl, al - save ASCII
            0x88, 0xE0,             // mov al, ah - copy scan code
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al - write scan code
            0x88, 0xD8,             // mov al, bl - restore ASCII
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al - write ASCII
            0xF4                    // hlt
        };

        KeyboardTestHandler testHandler = RunKeyboardTest(program, setupKeys: (ps2kbd) => {
            ps2kbd.AddKey(key, isPressed: true);
            ps2kbd.AddKey(key, isPressed: false);
        });

        testHandler.Details.Should().Contain(expectedScanCode,
            $"Scan code for {key} should be 0x{expectedScanCode:X2}");
        testHandler.Results.Should().Contain(expectedAscii,
            $"ASCII code for {key} should be 0x{expectedAscii:X2}");
    }

    /// <summary>
    /// Tests that number keys produce correct scan codes and ASCII codes
    /// </summary>
    [Theory]
    [InlineData(KbdKey.D1, 0x02, 0x31)] // 1 key -> scan 0x02, ASCII '1'
    [InlineData(KbdKey.D2, 0x03, 0x32)] // 2 key -> scan 0x03, ASCII '2'
    [InlineData(KbdKey.D5, 0x06, 0x35)] // 5 key -> scan 0x06, ASCII '5'
    [InlineData(KbdKey.D0, 0x0B, 0x30)] // 0 key -> scan 0x0B, ASCII '0'
    public void Int16H_ReadChar_ShouldProduceCorrectScancodeForNumbers(
        KbdKey key, byte expectedScanCode, byte expectedAscii) {
        byte[] program = new byte[]
        {
            0xB4, 0x00,             // mov ah, 00h
            0xCD, 0x16,             // int 16h
            0x88, 0xC3,             // mov bl, al - save ASCII
            0x88, 0xE0,             // mov al, ah - copy scan code
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al - write scan code
            0x88, 0xD8,             // mov al, bl - restore ASCII
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al - write ASCII
            0xF4                    // hlt
        };

        KeyboardTestHandler testHandler = RunKeyboardTest(program, setupKeys: (ps2kbd) => {
            ps2kbd.AddKey(key, isPressed: true);
            ps2kbd.AddKey(key, isPressed: false);
        });

        testHandler.Details.Should().Contain(expectedScanCode);
        testHandler.Results.Should().Contain(expectedAscii);
    }

    /// <summary>
    /// Tests that function keys produce correct scan codes
    /// </summary>
    [Theory]
    [InlineData(KbdKey.F1, 0x3B, 0x00)]  // F1 -> scan 0x3B, ASCII 0x00
    [InlineData(KbdKey.F2, 0x3C, 0x00)]  // F2 -> scan 0x3C, ASCII 0x00
    [InlineData(KbdKey.F10, 0x44, 0x00)] // F10 -> scan 0x44, ASCII 0x00
    public void Int16H_ReadChar_ShouldProduceCorrectScancodeForFunctionKeys(
        KbdKey key, byte expectedScanCode, byte expectedAscii) {
        byte[] program = new byte[]
        {
            0xB4, 0x00,             // mov ah, 00h
            0xCD, 0x16,             // int 16h
            0x88, 0xC3,             // mov bl, al - save ASCII
            0x88, 0xE0,             // mov al, ah - copy scan code
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al - write scan code
            0x88, 0xD8,             // mov al, bl - restore ASCII
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al - write ASCII
            0xF4                    // hlt
        };

        KeyboardTestHandler testHandler = RunKeyboardTest(program, setupKeys: (ps2kbd) => {
            ps2kbd.AddKey(key, isPressed: true);
            ps2kbd.AddKey(key, isPressed: false);
        });

        testHandler.Details.Should().Contain(expectedScanCode);
        testHandler.Results.Should().Contain(expectedAscii);
    }

    /// <summary>
    /// Tests INT16H function 01h - Check keyboard status (non-blocking)
    /// </summary>
    [Fact]
    public void Int16H_CheckStatus_ShouldIndicateKeyAvailable() {
        byte[] program = new byte[]
        {
            0xB4, 0x01,             // mov ah, 01h - Check keyboard status
            0xCD, 0x16,             // int 16h - ZF=0 if key available, ZF=1 if none
            0x74, 0x04,             // jz noKey (ZF set = no key)
            // Key available
            0xB0, 0x00,             // mov al, TestResult.Success
            0xEB, 0x02,             // jmp writeResult
            // noKey:
            0xB0, 0xFF,             // mov al, TestResult.Failure
            // writeResult:
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        KeyboardTestHandler testHandler = RunKeyboardTest(program, setupKeys: (ps2kbd) => {
            ps2kbd.AddKey(KbdKey.A, isPressed: true);
            ps2kbd.AddKey(KbdKey.A, isPressed: false);
        });

        testHandler.Results.Should().Contain((byte)TestResult.Success,
            "INT 16h function 01h should indicate key is available when buffer has a key");
    }

    /// <summary>
    /// Tests that special keys like Escape produce correct codes
    /// </summary>
    [Fact]
    public void Int16H_ReadChar_ShouldHandleEscapeKey() {
        byte[] program = new byte[]
        {
            0xB4, 0x00,             // mov ah, 00h
            0xCD, 0x16,             // int 16h
            0x88, 0xC3,             // mov bl, al - save ASCII
            0x88, 0xE0,             // mov al, ah - copy scan code
            0xBA, 0x98, 0x09,       // mov dx, DetailsPort
            0xEE,                   // out dx, al - write scan code
            0x88, 0xD8,             // mov al, bl - restore ASCII
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al - write ASCII
            0xF4                    // hlt
        };

        KeyboardTestHandler testHandler = RunKeyboardTest(program, setupKeys: (ps2kbd) => {
            ps2kbd.AddKey(KbdKey.Escape, isPressed: true);
            ps2kbd.AddKey(KbdKey.Escape, isPressed: false);
        });

        // Escape key: scan code 0x01, ASCII 0x1B (ESC character)
        testHandler.Details.Should().Contain(0x01, "Escape scan code should be 0x01");
        testHandler.Results.Should().Contain(0x1B, "Escape ASCII should be 0x1B");
    }

    /// <summary>
    /// Tests that INT 21h, AH=01h (character input with echo) reads a key and returns ASCII in AL.
    /// This function waits for keyboard input (polling INT 16h AH=01h) and echoes the character.
    /// </summary>
    [Fact]
    public void Int21H_CharacterInputWithEcho_ShouldReadKeyAndEcho() {
        // Program that reads one character using INT 21h, AH=01h
        // and reports the ASCII code via I/O port
        byte[] program = new byte[]
        {
            0xB4, 0x01,             // mov ah, 01h - Character input with echo
            0xCD, 0x21,             // int 21h - Returns: AL=ASCII code
            
            // Write ASCII code (AL) to result port
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };

        KeyboardTestHandler testHandler = RunKeyboardTest(program, setupKeys: (ps2kbd) => {
            // Simulate pressing and releasing the 'A' key
            ps2kbd.AddKey(KbdKey.A, isPressed: true);
            ps2kbd.AddKey(KbdKey.A, isPressed: false);
        });

        // The ASCII code for lowercase 'a' is 0x61
        testHandler.Results.Should().Contain(0x61,
            "INT 21h AH=01h should return ASCII code 0x61 for lowercase 'a'");
    }

    /// <summary>
    /// Tests that INT 21h, AH=01h properly handles various letter keys.
    /// </summary>
    [Theory]
    [InlineData(KbdKey.A, 0x61)] // A key -> ASCII 'a'
    [InlineData(KbdKey.Z, 0x7A)] // Z key -> ASCII 'z'
    [InlineData(KbdKey.D1, 0x31)] // 1 key -> ASCII '1'
    public void Int21H_CharacterInputWithEcho_ShouldProduceCorrectAscii(KbdKey key, byte expectedAscii) {
        byte[] program = new byte[]
        {
            0xB4, 0x01,             // mov ah, 01h - Character input with echo
            0xCD, 0x21,             // int 21h
            0xBA, 0x99, 0x09,       // mov dx, ResultPort
            0xEE,                   // out dx, al - write ASCII
            0xF4                    // hlt
        };

        KeyboardTestHandler testHandler = RunKeyboardTest(program, setupKeys: (ps2kbd) => {
            ps2kbd.AddKey(key, isPressed: true);
            ps2kbd.AddKey(key, isPressed: false);
        });

        testHandler.Results.Should().Contain(expectedAscii,
            $"ASCII code for {key} should be 0x{expectedAscii:X2}");
    }

    /// <summary>
    /// Runs keyboard test program and returns handler with results
    /// </summary>
    private KeyboardTestHandler RunKeyboardTest(
        byte[] program,
        Action<PS2Keyboard> setupKeys,
        [CallerMemberName] string unitTestName = "test") {

        // Write program to temp file
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        // Setup emulator
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enableCfgCpu: false,
            enablePit: true,
            recordData: false,
            maxCycles: 1000000L,
            installInterruptVectors: true,
            enableA20Gate: false,
            enableXms: false
        ).Create();

        KeyboardTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );

        // Setup keyboard events before running
        PS2Keyboard ps2kbd = spice86DependencyInjection.Machine.KeyboardController.KeyboardDevice;
        setupKeys(ps2kbd);

        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    /// <summary>
    /// Captures keyboard test results from designated I/O ports
    /// </summary>
    private class KeyboardTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();
        public List<byte> Details { get; } = new();

        public KeyboardTestHandler(State state, ILoggerService loggerService,
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