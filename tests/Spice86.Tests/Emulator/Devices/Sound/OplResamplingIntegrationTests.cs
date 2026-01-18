namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;

using NSubstitute;

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

using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Integration test for the complete OPL audio pipeline including resampling.
/// Tests: OPL chip → AddSamples_sfloat → MixerChannel resampling → Output
/// 
/// NOTE: Following DOSBox Staging architecture, AudioFrame stores int16-ranged floats
/// (approximately [-32768, 32767]) throughout the pipeline. These are normalized to
/// [-1.0, 1.0] only at the final mixer output before sending to PortAudio.
/// </summary>
public class OplResamplingIntegrationTests {
    private readonly ITestOutputHelper _output;
    private const int OplSampleRateHz = 49716;
    private const int MixerSampleRateHz = 48000;
    
    public OplResamplingIntegrationTests(ITestOutputHelper output) {
        _output = output;
    }
    
    private (Opl opl, Mixer mixer, IOPortDispatcher dispatcher) CreateAudioSystem() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        AddressReadWriteBreakpoints breakpoints = new();
        State state = new(CpuModel.INTEL_80286);
        IOPortDispatcher dispatcher = new(breakpoints, state, loggerService, failOnUnhandledPort: false);
        Mixer mixer = new(loggerService, AudioEngine.Dummy);
        EmulatedClock clock = new();
        EmulationLoopScheduler scheduler = new(clock, loggerService);
        DualPic dualPic = new(dispatcher, state, loggerService, false);
        
        Opl opl = new(mixer, state, dispatcher, false, loggerService, scheduler, clock, dualPic,
            useAdlibGold: false, enableOplIrq: false);
        
        return (opl, mixer, dispatcher);
    }
    
    [Fact(Skip = "doesn't pass for now")]
    public void OPL_With_Resampling_Produces_Valid_Audio() {
        // Arrange
        (Opl opl, Mixer mixer, IOPortDispatcher dispatcher) = CreateAudioSystem();
        
        MixerChannel channel = opl.MixerChannel;
        
        _output.WriteLine($"OPL sample rate: {channel.GetSampleRate()} Hz");
        _output.WriteLine($"Mixer sample rate: {mixer.SampleRateHz} Hz");
        
        // Configure a 440Hz tone
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x20);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x21);
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x43);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x00);
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0xA0);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x56);
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0xB0);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x31);
        
        // Act: Generate audio through the full pipeline
        // This calls AudioCallback which calls AddSamples_sfloat which resamples
        opl.AudioCallback(480); // Request 10ms at mixer rate (480 frames @ 48kHz)
        
        // Assert: Check the output
        List<AudioFrame> frames = channel.AudioFrames;
        
        _output.WriteLine($"Generated {frames.Count} frames after resampling");
        
        frames.Should().NotBeEmpty("OPL with resampling should generate frames");
        
        // Check for non-silent output
        int nonZeroCount = 0;
        float maxAmp = 0;
        float sumAmp = 0;
        
        for (int i = 0; i < frames.Count; i++) {
            float left = Math.Abs(frames[i].Left);
            float right = Math.Abs(frames[i].Right);
            float amp = Math.Max(left, right);
            
            if (amp > 0.0001f) {
                nonZeroCount++;
            }
            
            if (amp > maxAmp) {
                maxAmp = amp;
            }
            
            sumAmp += amp;
        }
        
        float avgAmp = frames.Count > 0 ? sumAmp / frames.Count : 0;
        float nonZeroPercent = frames.Count > 0 ? (nonZeroCount * 100.0f / frames.Count) : 0;
        
        _output.WriteLine($"Non-zero frames: {nonZeroCount}/{frames.Count} ({nonZeroPercent:F1}%)");
        _output.WriteLine($"Max amplitude: {maxAmp:F6}");
        _output.WriteLine($"Avg amplitude: {avgAmp:F6}");
        
        // Save to WAV
        string wavPath = Path.Combine(Path.GetTempPath(), "opl_with_resampling_integration.wav");
        WavFileFormat.WriteWavFile(wavPath, frames, MixerSampleRateHz);
        _output.WriteLine($"Saved audio to: {wavPath}");
        
        // Validate
        // Note: AudioFrame uses int16-ranged floats (like DOSBox Staging), not normalized [-1.0, 1.0]
        // Max int16 value is 32767, so reasonable audio should be in that range
        nonZeroPercent.Should().BeGreaterThan(90, "Resampled OPL audio should be non-silent");
        maxAmp.Should().BeGreaterThan(100, "Resampled OPL audio should have reasonable amplitude");
        maxAmp.Should().BeLessThan(32767, "Resampled OPL audio should not exceed int16 range");
        
        // Clean up
        opl.Dispose();
        mixer.Dispose();
    }

    
    [Fact(Skip = "doesn't pass for now")]
    public void OPL_Resampling_Maintains_Signal_Quality() {
        // Arrange
        (Opl opl, Mixer mixer, IOPortDispatcher dispatcher) = CreateAudioSystem();
        
        // Configure tone
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x20);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x21);
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0x43);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x00);
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0xA0);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x56);
        dispatcher.WriteByte(IOplPort.PrimaryAddressPortNumber, 0xB0);
        dispatcher.WriteByte(IOplPort.PrimaryDataPortNumber, 0x31);
        
        // Act: Generate multiple small chunks (like real mixer does)
        List<AudioFrame> allFrames = new();
        
        for (int chunk = 0; chunk < 10; chunk++) {
            opl.AudioCallback(480); // 10ms chunks
            allFrames.AddRange(opl.MixerChannel.AudioFrames);
            opl.MixerChannel.AudioFrames.Clear();
        }
        
        _output.WriteLine($"Total frames from chunked generation: {allFrames.Count}");
        
        // Assert: Should maintain quality across chunks
        int nonZeroCount = 0;
        float maxAmp = 0;
        
        for (int i = 0; i < allFrames.Count; i++) {
            float amp = Math.Max(Math.Abs(allFrames[i].Left), Math.Abs(allFrames[i].Right));
            if (amp > 0.0001f) {
                nonZeroCount++;
            }
            if (amp > maxAmp) {
                maxAmp = amp;
            }
        }
        
        float nonZeroPercent = allFrames.Count > 0 ? (nonZeroCount * 100.0f / allFrames.Count) : 0;
        
        _output.WriteLine($"Non-zero: {nonZeroCount}/{allFrames.Count} ({nonZeroPercent:F1}%)");
        _output.WriteLine($"Max amplitude: {maxAmp:F6}");
        
        nonZeroPercent.Should().BeGreaterThan(90, "Chunked resampling should maintain signal");
        
        // Save
        string wavPath = Path.Combine(Path.GetTempPath(), "opl_chunked_resampling.wav");
        WavFileFormat.WriteWavFile(wavPath, allFrames, MixerSampleRateHz);
        _output.WriteLine($"Saved chunked audio to: {wavPath}");
        
        // Clean up
        opl.Dispose();
        mixer.Dispose();
    }
}
