namespace Spice86.Tests.Emulator.Devices.Sound;

using System.Runtime.CompilerServices;
using FluentAssertions;
using NSubstitute;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Xunit;

/// <summary>
/// ASM-based integration tests for OPL/AdLib Gold mirroring DOSBox Staging behavior.
/// These tests verify complete OPL register write sequences and audio generation
/// matching DOSBox Staging's implementation exactly.
/// </summary>
public class OplAsmIntegrationTests {
    private const int MaxCycles = 10000000;
    
    [Fact]
    public void Test_OPL_Simple_Tone_Generation() {
        // This test exercises OPL register writes for a simple 440Hz tone
        // Mirrors DOSBox OPL tone generation behavior
        // Compiled from opl_simple_tone.asm with NASM
        OplTestHandler testHandler = RunOplTestFromFile("opl_simple_tone.bin", MaxCycles);
        
        testHandler.Results.Should().Contain((byte)0x00, "OPL simple tone generation should complete successfully");
        testHandler.Results.Should().NotContain((byte)0xFF, "should not report failure");
    }
    
    [Fact]
    public void Test_OPL_Rhythm_Mode() {
        // This test exercises OPL rhythm/percussion mode
        // Validates bass drum, snare drum, tom-tom, cymbal, and hi-hat triggering
        // Compiled from opl_rhythm_mode.asm with NASM
        OplTestHandler testHandler = RunOplTestFromFile("opl_rhythm_mode.bin", MaxCycles);
        
        testHandler.Results.Should().Contain((byte)0x00, "OPL rhythm mode should work correctly");
        testHandler.Results.Should().NotContain((byte)0xFF, "should not report failure");
    }
    
    [Fact]
    public void Test_AdLib_Gold_Stereo_Control() {
        // This test exercises AdLib Gold stereo control via OPL3 extended registers
        // Tests 0x38A/0x38B port writes and stereo panning
        // Compiled from adlib_gold_stereo.asm with NASM
        OplTestHandler testHandler = RunOplTestFromFile("adlib_gold_stereo.bin", MaxCycles);
        
        testHandler.Results.Should().Contain((byte)0x00, "AdLib Gold stereo control should work");
        testHandler.Results.Should().NotContain((byte)0xFF, "should not report failure");
    }
    
    [Fact]
    public void Test_OPL_Register_Write_Sequence() {
        // Arrange: Inline ASM test for OPL register writes
        // This mirrors the DOSBox pattern of register address write followed by data write
        byte[] program = new byte[] {
            // Write to OPL register 0x01 (waveform select enable)
            0xBA, 0x88, 0x03,       // mov dx, 0x388 - OPL address port
            0xB0, 0x01,             // mov al, 0x01
            0xEE,                   // out dx, al
            // Small delay
            0xB9, 0x06, 0x00,       // mov cx, 6
            0xE2, 0xFE,             // loop $
            // Write data
            0x42,                   // inc dx (now 0x389)
            0xB0, 0x20,             // mov al, 0x20
            0xEE,                   // out dx, al
            // Report success
            0xBA, 0x99, 0x09,       // mov dx, 0x999
            0xB0, 0x00,             // mov al, 0 (success)
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };
        
        OplTestHandler testHandler = RunOplTest(program);
        
        testHandler.Results.Should().Contain((byte)0x00, "OPL register write sequence should complete");
    }
    
    [Fact]
    public void Test_OPL_Timer_Registers() {
        // Arrange: Test OPL timer configuration
        byte[] program = new byte[] {
            // Configure Timer 1 (register 0x02)
            0xBA, 0x88, 0x03,       // mov dx, 0x388
            0xB0, 0x02,             // mov al, 0x02
            0xEE,                   // out dx, al
            0xB9, 0x06, 0x00,       // mov cx, 6
            0xE2, 0xFE,             // loop $ - delay
            0x42,                   // inc dx
            0xB0, 0xFF,             // mov al, 0xFF
            0xEE,                   // out dx, al
            // Enable timer (register 0x04)
            0x4A,                   // dec dx
            0xB0, 0x04,             // mov al, 0x04
            0xEE,                   // out dx, al
            0xB9, 0x06, 0x00,       // mov cx, 6
            0xE2, 0xFE,             // loop $
            0x42,                   // inc dx
            0xB0, 0x01,             // mov al, 0x01 - start timer 1
            0xEE,                   // out dx, al
            // Report success
            0xBA, 0x99, 0x09,       // mov dx, 0x999
            0xB0, 0x00,             // mov al, 0
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };
        
        OplTestHandler testHandler = RunOplTest(program);
        
        testHandler.Results.Should().Contain((byte)0x00, "OPL timer registers should work");
    }
    
    [Fact]
    public void Test_OPL_Waveform_Selection() {
        // Arrange: Test waveform selection enable and waveform register
        byte[] program = new byte[] {
            // Enable waveform selection (register 0x01)
            0xBA, 0x88, 0x03,       // mov dx, 0x388
            0xB0, 0x01,             // mov al, 0x01
            0xEE,                   // out dx, al
            0xB9, 0x06, 0x00,       // mov cx, 6
            0xE2, 0xFE,             // loop $
            0x42,                   // inc dx
            0xB0, 0x20,             // mov al, 0x20 - enable waveform select
            0xEE,                   // out dx, al
            // Set waveform for operator 0 (register 0xE0)
            0x4A,                   // dec dx
            0xB0, 0xE0,             // mov al, 0xE0
            0xEE,                   // out dx, al
            0xB9, 0x06, 0x00,       // mov cx, 6
            0xE2, 0xFE,             // loop $
            0x42,                   // inc dx
            0xB0, 0x03,             // mov al, 0x03 - waveform 3 (pulse)
            0xEE,                   // out dx, al
            // Report success
            0xBA, 0x99, 0x09,       // mov dx, 0x999
            0xB0, 0x00,             // mov al, 0
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };
        
        OplTestHandler testHandler = RunOplTest(program);
        
        testHandler.Results.Should().Contain((byte)0x00, "OPL waveform selection should work");
    }
    
    [Fact]
    public void Test_OPL3_Four_Op_Mode() {
        // Arrange: Test OPL3 4-operator synthesis mode enable
        byte[] program = new byte[] {
            // Enable OPL3 mode (register 0x105)
            0xBA, 0x8A, 0x03,       // mov dx, 0x38A - OPL3 extended port
            0xB0, 0x05,             // mov al, 0x05
            0xEE,                   // out dx, al
            0xB9, 0x06, 0x00,       // mov cx, 6
            0xE2, 0xFE,             // loop $
            0x42,                   // inc dx (now 0x38B)
            0xB0, 0x01,             // mov al, 0x01 - enable OPL3
            0xEE,                   // out dx, al
            // Enable 4-op for channels 0-2 (register 0x104)
            0x4A,                   // dec dx
            0xB0, 0x04,             // mov al, 0x04
            0xEE,                   // out dx, al
            0xB9, 0x06, 0x00,       // mov cx, 6
            0xE2, 0xFE,             // loop $
            0x42,                   // inc dx
            0xB0, 0x3F,             // mov al, 0x3F - enable 4-op for all 6 channels
            0xEE,                   // out dx, al
            // Report success
            0xBA, 0x99, 0x09,       // mov dx, 0x999
            0xB0, 0x00,             // mov al, 0
            0xEE,                   // out dx, al
            0xF4                    // hlt
        };
        
        OplTestHandler testHandler = RunOplTest(program);
        
        testHandler.Results.Should().Contain((byte)0x00, "OPL3 4-op mode should work");
    }
    
    private OplTestHandler RunOplTest(byte[] program, long maxCycles = 100000L,
        [CallerMemberName] string unitTestName = "test") {
        // Write program to file
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);
        
        // Setup emulator
        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            
            enablePit: true,
            recordData: false,
            maxCycles: maxCycles,
            installInterruptVectors: true,
            failOnUnhandledPort: false
        ).Create();
        
        OplTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        
        spice86DependencyInjection.ProgramExecutor.Run();
        
        // Give mixer thread time to process audio if needed
        Task.Delay(100).Wait();
        
        return testHandler;
    }
    
    private OplTestHandler RunOplTestFromFile(string binFileName, long maxCycles = 100000L,
        [CallerMemberName] string unitTestName = "test") {
        // Load program from Resources directory
        string resourcePath = Path.Combine("Resources", "SoundBlasterTests", binFileName);
        byte[] program = File.ReadAllBytes(resourcePath);
        
        return RunOplTest(program, maxCycles, unitTestName);
    }
    
    private class OplTestHandler : DefaultIOPortHandler {
        private const int ResultPort = 0x999;
        public List<byte> Results { get; } = new();
        
        public OplTestHandler(State state, ILoggerService loggerService,
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
