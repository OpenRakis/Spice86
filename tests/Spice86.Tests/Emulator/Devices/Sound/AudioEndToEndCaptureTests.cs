namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Libs.Sound.Common;
using Spice86.Libs.Sound.Devices.NukedOpl3;
using Spice86.Shared.Interfaces;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// End-to-end audio capture tests that validate the complete audio pipeline:
/// OPL/PCM → Mixer → AudioPlayer output
/// These tests capture actual mixed audio to diagnose static/silence issues.
/// 
/// NOTE: AudioFrame uses int16-ranged floats (not normalized [-1.0, 1.0]) throughout
/// the Spice86 audio pipeline, matching DOSBox Staging's architecture. Values are
/// only normalized to [-1.0, 1.0] at the final output step before PortAudio.
/// </summary>
public class AudioEndToEndCaptureTests {
    private readonly ITestOutputHelper _output;
    private const int OplSampleRateHz = 49716;
    private const int MixerSampleRateHz = 48000;
    private const int MixerBlocksize = 1024;
    
    public AudioEndToEndCaptureTests(ITestOutputHelper output) {
        _output = output;
    }

    /// <summary>
    /// Helper to create a complete audio system with real logging.
    /// </summary>
    private (Opl3Fm opl, Mixer mixer, IOPortDispatcher dispatcher, ILoggerService logger) CreateAudioSystem(
        bool useAdlibGold = false) {
        
        // Use REAL LoggerService so we can see logs
        ILoggerService loggerService = new Spice86.Logging.LoggerService();
        loggerService.LogLevelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Debug;
        
        AddressReadWriteBreakpoints breakpoints = new();
        State state = new(CpuModel.INTEL_80286);
        IOPortDispatcher dispatcher = new(breakpoints, state, loggerService, failOnUnhandledPort: false);
        
        // Create mixer with dummy audio (discards output)
        Mixer mixer = new(loggerService, AudioEngine.Dummy);
        
        EmulatedClock clock = new();
        EmulationLoopScheduler scheduler = new(clock, loggerService);
        DualPic dualPic = new(dispatcher, state, loggerService, false);
        
        Opl3Fm opl = new(mixer, state, dispatcher, false, loggerService, scheduler, clock, dualPic,
            useAdlibGold: useAdlibGold, enableOplIrq: false);
        
        return (opl, mixer, dispatcher, loggerService);
    }
    
    [Fact]
    public void OPL_Generates_NonSilent_Audio_When_Key_Pressed() {
        // Arrange
        (Opl3Fm opl, Mixer mixer, IOPortDispatcher dispatcher, ILoggerService logger) = CreateAudioSystem();
        
        _output.WriteLine("Configuring OPL to play 440Hz tone...");
        
        // Enable waveform selection
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x01);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x20);
        
        // Configure modulator (operator 0)
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x20); // Mult/KSR/EG/VIB/AM
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x21); // Multiple=1, Sustain On
        
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x40); // Key Scale Level / Output Level
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x10); // Output level 16 (loud)
        
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x60); // Attack Rate / Decay Rate
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0xF0); // Fast attack/decay
        
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x80); // Sustain Level / Release Rate
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x77); // Mid sustain, medium release
        
        // Configure carrier (operator 1)
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x23);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x21);
        
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x43);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x00); // Maximum volume
        
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x63);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0xF0);
        
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x83);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x77);
        
        // Configure channel 0
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0xC0); // Feedback / Algorithm
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x01); // FM synthesis, no feedback
        
        // Set frequency to 440Hz (A4)
        // F-Num for 440Hz is approximately 0x156 (342 decimal)
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0xA0); // Frequency low byte
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x56);
        
        // Key ON with octave 4
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0xB0); // Key On / Block / Frequency high
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x31); // Key On | Block 3 | F-Num high bits
        
        _output.WriteLine("Registers configured. Generating audio...");
        
        // Act: Generate audio frames
        int framesRequested = 4800; // 100ms at 48kHz
        opl.AudioCallback(framesRequested);
        
        MixerChannel channel = opl.MixerChannel;
        List<AudioFrame> frames = channel.AudioFrames;
        
        _output.WriteLine($"Generated {frames.Count} frames");
        
        // Assert: Should have non-zero audio
        frames.Should().NotBeEmpty("OPL should generate frames");
        
        // Check for non-silent output
        int nonZeroCount = 0;
        float maxAmplitude = 0;
        float sumAmplitude = 0;
        
        for (int i = 0; i < frames.Count; i++) {
            float left = Math.Abs(frames[i].Left);
            float right = Math.Abs(frames[i].Right);
            float amp = Math.Max(left, right);
            
            if (amp > 0.0001f) {
                nonZeroCount++;
            }
            
            if (amp > maxAmplitude) {
                maxAmplitude = amp;
            }
            
            sumAmplitude += amp;
        }
        
        float avgAmplitude = frames.Count > 0 ? sumAmplitude / frames.Count : 0;
        float nonZeroPercent = frames.Count > 0 ? (nonZeroCount * 100.0f / frames.Count) : 0;
        
        _output.WriteLine($"Non-zero frames: {nonZeroCount}/{frames.Count} ({nonZeroPercent:F1}%)");
        _output.WriteLine($"Max amplitude: {maxAmplitude:F6}");
        _output.WriteLine($"Avg amplitude: {avgAmplitude:F6}");
        
        // Save to WAV for manual inspection
        string wavPath = Path.Combine(Path.GetTempPath(), "opl_440hz_test.wav");
        WavFileFormat.WriteWavFile(wavPath, frames, OplSampleRateHz);
        _output.WriteLine($"Saved audio to: {wavPath}");
        
        // Validate - AudioFrame uses int16-ranged floats (like DOSBox Staging)
        // Valid range is approximately [-32768, 32767] for int16
        nonZeroCount.Should().BeGreaterThan(0, "OPL should produce non-zero audio when key is ON");
        maxAmplitude.Should().BeGreaterThan(100, "OPL audio should have measurable amplitude");
        maxAmplitude.Should().BeLessThan(32767, "OPL audio should not exceed int16 max range");
        
        // Clean up
        opl.Dispose();
        mixer.Dispose();
    }
    
    [Fact]
    public void OPL_Is_Silent_When_No_Keys_Pressed() {
        // Arrange
        (Opl3Fm opl, Mixer mixer, IOPortDispatcher dispatcher, ILoggerService logger) = CreateAudioSystem();
        
        _output.WriteLine("Generating audio with no key presses...");
        
        // Act: Generate audio without configuring any notes
        int framesRequested = 4800; // 100ms at 48kHz
        opl.AudioCallback(framesRequested);
        
        MixerChannel channel = opl.MixerChannel;
        List<AudioFrame> frames = channel.AudioFrames;
        
        _output.WriteLine($"Generated {frames.Count} frames");
        
        // Assert: Should be silence or near-silence
        int nonZeroCount = 0;
        float maxAmplitude = 0;
        
        for (int i = 0; i < frames.Count; i++) {
            float left = Math.Abs(frames[i].Left);
            float right = Math.Abs(frames[i].Right);
            float amp = Math.Max(left, right);
            
            if (amp > 0.0001f) {
                nonZeroCount++;
            }
            
            if (amp > maxAmplitude) {
                maxAmplitude = amp;
            }
        }
        
        float nonZeroPercent = frames.Count > 0 ? (nonZeroCount * 100.0f / frames.Count) : 0;
        
        _output.WriteLine($"Non-zero frames: {nonZeroCount}/{frames.Count} ({nonZeroPercent:F1}%)");
        _output.WriteLine($"Max amplitude: {maxAmplitude:F6}");
        
        // Allow for small noise gate residuals but should be mostly silent
        // AudioFrame uses int16-ranged floats, so noise should be minimal (< 100)
        nonZeroPercent.Should().BeLessThan(10, "OPL should be mostly silent with no keys pressed");
        maxAmplitude.Should().BeLessThan(100, "OPL noise should be very low with no keys pressed");
        
        // Clean up
        opl.Dispose();
        mixer.Dispose();
    }
    
    [Fact]
    public void Mixer_Calls_OPL_Callback_During_Mix() {
        // Arrange
        (Opl3Fm opl, Mixer mixer, IOPortDispatcher dispatcher, ILoggerService logger) = CreateAudioSystem();
        
        // Configure a tone
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x20);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x21);
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x43);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x00);
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0xA0);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x56);
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0xB0);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x31);
        
        // Make sure channel is enabled and awake
        MixerChannel channel = opl.MixerChannel;
        channel.Enable(true);
        channel.WakeUp();
        
        int initialFrameCount = channel.AudioFrames.Count;
        _output.WriteLine($"Initial frame count: {initialFrameCount}");
        
        // Act: Manually trigger mixer mix cycle (simulates what mixer thread does)
        // Note: This is internal to Mixer, so we test via channel.Mix()
        channel.Mix(480); // Request 10ms of audio
        
        // Assert: Channel should have frames
        int finalFrameCount = channel.AudioFrames.Count;
        _output.WriteLine($"Final frame count: {finalFrameCount}");
        _output.WriteLine($"Is channel enabled: {channel.IsEnabled}");
        
        finalFrameCount.Should().BeGreaterThan(initialFrameCount, "Mix should generate new frames");
        
        // Clean up
        opl.Dispose();
        mixer.Dispose();
    }
}
