namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;
using Spice86.Libs.Sound.Common;
using Xunit;
using Xunit.Abstractions;
using global::Bufdio.Spice86;

/// <summary>
/// Diagnostic tests for the Speex resampler with OPL-specific rates.
/// Tests the exact scenario: 49716 Hz (OPL) → 48000 Hz (Mixer)
/// </summary>
public class ResamplingDiagnosticTests {
    private readonly ITestOutputHelper _output;
    private const int OplSampleRateHz = 49716;
    private const int MixerSampleRateHz = 48000;
    private const uint SpeexChannels = 2; // stereo
    private const int SpeexQuality = 5;
    
    public ResamplingDiagnosticTests(ITestOutputHelper output) {
        _output = output;
    }
    
    [Fact]
    public void Speex_Resampler_OPL_To_Mixer_Rates_Works() {
        // Arrange: Create Speex resampler with OPL → Mixer rates
        using SpeexResamplerCSharp resampler = new(
            SpeexChannels,
            (uint)OplSampleRateHz,
            (uint)MixerSampleRateHz,
            SpeexQuality);
        
        _output.WriteLine($"Resampler configured: {OplSampleRateHz} Hz → {MixerSampleRateHz} Hz");
        
        // Act: Generate a 440Hz sine wave at OPL rate
        int inputFrames = 4972; // ~100ms at 49716 Hz
        float[] inputBuffer = GenerateSineWave(440.0f, OplSampleRateHz, inputFrames);
        
        // Calculate expected output frames
        resampler.GetRatio(out uint ratioNum, out uint ratioDen);
        int expectedOutputFrames = (int)Math.Ceiling((double)inputFrames * ratioDen / ratioNum);
        _output.WriteLine($"Input: {inputFrames} frames, Expected output: ~{expectedOutputFrames} frames");
        _output.WriteLine($"Ratio: {ratioNum}/{ratioDen}");
        
        float[] outputBuffer = new float[expectedOutputFrames * 2 + 100]; // Extra space
        
        // Resample
        resampler.ProcessInterleavedFloat(
            inputBuffer.AsSpan(),
            outputBuffer.AsSpan(),
            out uint inFramesConsumed,
            out uint outFramesGenerated);
        
        _output.WriteLine($"Consumed: {inFramesConsumed} frames, Generated: {outFramesGenerated} frames");
        
        // Assert: Should generate frames
        outFramesGenerated.Should().BeGreaterThan(0, "Resampler should generate output frames");
        inFramesConsumed.Should().Be((uint)inputFrames, "Resampler should consume all input");
        
        // Check output is not silent
        int nonZeroCount = 0;
        float maxAmp = 0;
        for (int i = 0; i < (int)outFramesGenerated * 2; i++) {
            float sample = Math.Abs(outputBuffer[i]);
            if (sample > 0.0001f) {
                nonZeroCount++;
            }
            if (sample > maxAmp) {
                maxAmp = sample;
            }
        }
        
        float nonZeroPercent = outFramesGenerated > 0 ? (nonZeroCount * 100.0f / ((int)outFramesGenerated * 2)) : 0;
        _output.WriteLine($"Non-zero samples: {nonZeroCount}/{(int)outFramesGenerated * 2} ({nonZeroPercent:F1}%)");
        _output.WriteLine($"Max amplitude: {maxAmp:F6}");
        
        nonZeroCount.Should().BeGreaterThan(0, "Resampled audio should not be silent");
        maxAmp.Should().BeGreaterThan(0.1f, "Resampled audio should have reasonable amplitude");
        nonZeroPercent.Should().BeGreaterThan(90, "Most samples should be non-zero for sine wave");
        
        // Save output for manual inspection
        List<AudioFrame> frames = new();
        for (int i = 0; i < (int)outFramesGenerated; i++) {
            frames.Add(new AudioFrame(outputBuffer[i * 2], outputBuffer[i * 2 + 1]));
        }
        string wavPath = Path.Combine(Path.GetTempPath(), "speex_opl_rates_test.wav");
        WavFileFormat.WriteWavFile(wavPath, frames, MixerSampleRateHz);
        _output.WriteLine($"Saved resampled audio to: {wavPath}");
    }
    
    [Fact]
    public void Speex_Multiple_Small_Chunks_Like_Mixer() {
        // Arrange: Test resampling in small chunks like the mixer does
        using SpeexResamplerCSharp resampler = new(
            SpeexChannels,
            (uint)OplSampleRateHz,
            (uint)MixerSampleRateHz,
            SpeexQuality);
        
        List<AudioFrame> allOutputFrames = new();
        
        // Generate and resample in small chunks (512 frames at a time, like MaxSamplesPerGenerationBatch)
        int totalInputFrames = 0;
        int totalOutputFrames = 0;
        
        for (int chunk = 0; chunk < 10; chunk++) {
            int chunkFrames = 512;
            float[] inputBuffer = GenerateSineWave(440.0f, OplSampleRateHz, chunkFrames, chunk * chunkFrames);
            
            resampler.GetRatio(out uint ratioNum, out uint ratioDen);
            int estimatedOutputFrames = (int)Math.Ceiling((double)chunkFrames * ratioDen / ratioNum);
            float[] outputBuffer = new float[estimatedOutputFrames * 2 + 100];
            
            resampler.ProcessInterleavedFloat(
                inputBuffer.AsSpan(),
                outputBuffer.AsSpan(),
                out uint inFramesConsumed,
                out uint outFramesGenerated);
            
            totalInputFrames += (int)inFramesConsumed;
            totalOutputFrames += (int)outFramesGenerated;
            
            for (int i = 0; i < (int)outFramesGenerated; i++) {
                allOutputFrames.Add(new AudioFrame(outputBuffer[i * 2], outputBuffer[i * 2 + 1]));
            }
        }
        
        _output.WriteLine($"Total input: {totalInputFrames} frames, Total output: {totalOutputFrames} frames");
        
        // Assert: Should have accumulated frames
        totalOutputFrames.Should().BeGreaterThan(0, "Should generate output across chunks");
        allOutputFrames.Count.Should().Be(totalOutputFrames);
        
        // Check output is not silent
        int nonZeroCount = 0;
        float maxAmp = 0;
        for (int i = 0; i < allOutputFrames.Count; i++) {
            float left = Math.Abs(allOutputFrames[i].Left);
            float right = Math.Abs(allOutputFrames[i].Right);
            float amp = Math.Max(left, right);
            if (amp > 0.0001f) {
                nonZeroCount++;
            }
            if (amp > maxAmp) {
                maxAmp = amp;
            }
        }
        
        float nonZeroPercent = allOutputFrames.Count > 0 ? (nonZeroCount * 100.0f / allOutputFrames.Count) : 0;
        _output.WriteLine($"Non-zero frames: {nonZeroCount}/{allOutputFrames.Count} ({nonZeroPercent:F1}%)");
        _output.WriteLine($"Max amplitude: {maxAmp:F6}");
        
        nonZeroPercent.Should().BeGreaterThan(90, "Chunked resampling should preserve signal");
        
        // Save output
        string wavPath = Path.Combine(Path.GetTempPath(), "speex_chunked_test.wav");
        WavFileFormat.WriteWavFile(wavPath, allOutputFrames, MixerSampleRateHz);
        _output.WriteLine($"Saved chunked resampled audio to: {wavPath}");
    }
    
    /// <summary>
    /// Generates an interleaved stereo sine wave.
    /// </summary>
    private float[] GenerateSineWave(float frequencyHz, int sampleRateHz, int frames, int startFrame = 0) {
        float[] buffer = new float[frames * 2];
        
        for (int frame = 0; frame < frames; frame++) {
            int totalFrame = startFrame + frame;
            float t = (float)totalFrame / sampleRateHz;
            float sample = (float)Math.Sin(2.0 * Math.PI * frequencyHz * t);
            
            // Interleaved stereo
            buffer[frame * 2] = sample;
            buffer[frame * 2 + 1] = sample;
        }
        
        return buffer;
    }
}
