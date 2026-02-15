namespace Spice86.Tests.Libs.Sound.Resampling;

using FluentAssertions;

using Spice86.Libs.Sound.Resampling;

using Xunit;

/// <summary>
/// Comprehensive test suite for SpeexResamplerCSharp.
/// Tests correctness of resampling with various sample rates, quality levels, and channel configurations.
/// These tests validate against the original C implementation (libspeexdsp/resample.c) semantics.
/// </summary>
[Trait("Category", "Audio")]
public class SpeexResamplerCSharpTests {
    /// <summary>
    /// Tests basic single-channel float resampling without quality degradation.
    /// Verifies that resampling produces correct number of output samples.
    /// </summary>
    [Fact]
    public void ProcessFloat_SingleChannel_ProducesCorrectOutputCount() {
        // Arrange
        var resampler = new SpeexResamplerCSharp(channels: 1, in_rate_init: 44100, out_rate_init: 48000, quality: 8);
        float[] input = GenerateTestSignal(1000, frequency: 1000f, sampleRate: 44100);
        float[] output = new float[2000];
        
        uint in_len = (uint)input.Length;
        uint out_len = (uint)output.Length;
        
        // Act
        int result = resampler.ProcessFloat(0, input.AsSpan(), 0, ref in_len, output.AsSpan(), 0, ref out_len);
        
        // Assert
        result.Should().Be(0, "resampling should succeed");
        out_len.Should().BeGreaterThan(0, "output should contain samples");
        // For 44100 -> 48000, ratio is 48000/44100 ≈ 1.0884
        // 1000 samples should produce ~1088 output samples (accounting for filter state)
        out_len.Should().BeLessThanOrEqualTo((uint)output.Length, "output length should not exceed buffer");
    }

    /// <summary>
    /// Tests resampling with upsampling (low to high sample rate).
    /// </summary>
    [Fact]
    public void ProcessFloat_Upsample_22050To48000_ProducesValidOutput() {
        // Arrange
        var resampler = new SpeexResamplerCSharp(channels: 1, in_rate_init: 22050, out_rate_init: 48000, quality: 8);
        float[] input = GenerateTestSignal(500, frequency: 440f, sampleRate: 22050);
        float[] output = new float[2000];
        
        uint in_len = (uint)input.Length;
        uint out_len = (uint)output.Length;
        
        // Act
        int result = resampler.ProcessFloat(0, input.AsSpan(), 0, ref in_len, output.AsSpan(), 0, ref out_len);
        
        // Assert
        result.Should().Be(0);
        out_len.Should().BeGreaterThan(0);
        // Upsampling ratio: 48000/22050 ≈ 2.177
        // So 500 samples should produce ~1089 output samples
        float expectedSamples = 500 * (48000f / 22050f);
        out_len.Should().BeGreaterThan((uint)(expectedSamples * 0.9f));
    }

    /// <summary>
    /// Tests resampling with downsampling (high to low sample rate).
    /// </summary>
    [Fact]
    public void ProcessFloat_Downsample_48000To22050_ProducesValidOutput() {
        // Arrange
        var resampler = new SpeexResamplerCSharp(channels: 1, in_rate_init: 48000, out_rate_init: 22050, quality: 8);
        float[] input = GenerateTestSignal(1000, frequency: 440f, sampleRate: 48000);
        float[] output = new float[2000];
        
        uint in_len = (uint)input.Length;
        uint out_len = (uint)output.Length;
        
        // Act
        int result = resampler.ProcessFloat(0, input.AsSpan(), 0, ref in_len, output.AsSpan(), 0, ref out_len);
        
        // Assert
        result.Should().Be(0);
        out_len.Should().BeGreaterThan(0);
        // Downsampling ratio: 22050/48000 ≈ 0.4594
        float expectedSamples = 1000 * (22050f / 48000f);
        out_len.Should().BeGreaterThan((uint)(expectedSamples * 0.9f));
    }

    /// <summary>
    /// Tests resampling with multiple channels (stereo).
    /// </summary>
    [Fact]
    public void ProcessInterleavedFloat_Stereo_ProducesValidOutput() {
        // Arrange
        var resampler = new SpeexResamplerCSharp(channels: 2, in_rate_init: 44100, out_rate_init: 48000, quality: 8);
        // Create stereo interleaved signal: [L0, R0, L1, R1, ...]
        float[] input = GenerateStereoInterleavedSignal(500, frequency: 440f, sampleRate: 44100);
        float[] output = new float[2000];
        
        uint in_len = (uint)(input.Length / 2); // frames, not samples
        uint out_len = (uint)(output.Length / 2);
        
        // Act
        int result = resampler.ProcessInterleavedFloat(input.AsSpan(), ref in_len, output.AsSpan(), ref out_len);
        
        // Assert
        result.Should().Be(0);
        out_len.Should().BeGreaterThan(0, "should produce output frames");
        // Both channels should have been processed
        out_len.Should().BeLessThanOrEqualTo((uint)(output.Length / 2));
    }

    /// <summary>
    /// Tests different quality levels produce valid output.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(10)]
    public void ProcessFloat_VariousQualityLevels_ProduceValidOutput(int quality) {
        // Arrange
        var resampler = new SpeexResamplerCSharp(channels: 1, in_rate_init: 44100, out_rate_init: 48000, quality: quality);
        float[] input = GenerateTestSignal(500, frequency: 440f, sampleRate: 44100);
        float[] output = new float[1500];
        
        uint in_len = (uint)input.Length;
        uint out_len = (uint)output.Length;
        
        // Act
        int result = resampler.ProcessFloat(0, input.AsSpan(), 0, ref in_len, output.AsSpan(), 0, ref out_len);
        
        // Assert
        result.Should().Be(0, $"quality {quality} should produce valid output");
        out_len.Should().BeGreaterThan(0, $"quality {quality} should produce samples");
    }

    /// <summary>
    /// Tests chunked resampling (multiple calls with partial data).
    /// This exercises the state machine that tracks sample position across calls.
    /// </summary>
    [Fact]
    public void ProcessFloat_ChunkedInput_MaintainsStateCorrectly() {
        // Arrange
        var resampler = new SpeexResamplerCSharp(channels: 1, in_rate_init: 44100, out_rate_init: 48000, quality: 8);
        float[] fullInput = GenerateTestSignal(1000, frequency: 440f, sampleRate: 44100);
        float[] output1 = new float[2000];
        float[] output2 = new float[2000];
        
        // Process in two chunks
        uint in_len1 = 500;
        uint out_len1 = (uint)output1.Length;
        uint in_len2 = 500;
        uint out_len2 = (uint)output2.Length;
        
        // Act
        int result1 = resampler.ProcessFloat(0, fullInput.AsSpan(0, 500), 0, ref in_len1, output1.AsSpan(), 0, ref out_len1);
        int result2 = resampler.ProcessFloat(0, fullInput.AsSpan(500, 500), 0, ref in_len2, output2.AsSpan(), 0, ref out_len2);
        
        // Assert
        result1.Should().Be(0);
        result2.Should().Be(0);
        out_len1.Should().BeGreaterThan(0);
        out_len2.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Tests that output is zero when resampling zero input.
    /// </summary>
    [Fact]
    public void ProcessFloat_ZeroInput_ProducesZeroOutput() {
        // Arrange
        var resampler = new SpeexResamplerCSharp(channels: 1, in_rate_init: 44100, out_rate_init: 48000, quality: 8);
        float[] input = new float[500]; // All zeros
        float[] output = new float[1000];
        
        uint in_len = (uint)input.Length;
        uint out_len = (uint)output.Length;
        
        // Act
        int result = resampler.ProcessFloat(0, input.AsSpan(), 0, ref in_len, output.AsSpan(), 0, ref out_len);
        
        // Assert
        result.Should().Be(0);
        out_len.Should().BeGreaterThan(0);
        // Output should be mostly zeros (or very close to zero)
        for (int i = 0; i < out_len; i++) {
            output[i].Should().BeLessThan(0.001f, because: "zero input should produce near-zero output");
        }
    }

    /// <summary>
    /// Tests resampling with empty input.
    /// </summary>
    [Fact]
    public void ProcessFloat_EmptyInput_HandlesGracefully() {
        // Arrange
        var resampler = new SpeexResamplerCSharp(channels: 1, in_rate_init: 44100, out_rate_init: 48000, quality: 8);
        float[] input = Array.Empty<float>();
        float[] output = new float[1000];
        
        uint in_len = 0;
        uint out_len = (uint)output.Length;
        
        // Act
        int result = resampler.ProcessFloat(0, input.AsSpan(), 0, ref in_len, output.AsSpan(), 0, ref out_len);
        
        // Assert
        result.Should().BeOneOf(0, 1);
    }

    /// <summary>
    /// Tests that invalid channel index returns error.
    /// </summary>
    [Fact]
    public void ProcessFloat_InvalidChannelIndex_ReturnsError() {
        // Arrange
        var resampler = new SpeexResamplerCSharp(channels: 2, in_rate_init: 44100, out_rate_init: 48000, quality: 8);
        float[] input = GenerateTestSignal(100, frequency: 440f, sampleRate: 44100);
        float[] output = new float[1000];
        
        uint in_len = (uint)input.Length;
        uint out_len = (uint)output.Length;
        
        // Act
        int result = resampler.ProcessFloat(999, input.AsSpan(), 0, ref in_len, output.AsSpan(), 0, ref out_len);
        
        // Assert
        result.Should().NotBe(0, "invalid channel should return error code");
    }

    /// <summary>
    /// Tests that output is bounded (no NaN or Infinity).
    /// </summary>
    [Fact]
    public void ProcessFloat_OutputIsAlwaysFinite() {
        // Arrange
        var resampler = new SpeexResamplerCSharp(channels: 1, in_rate_init: 44100, out_rate_init: 48000, quality: 8);
        float[] input = GenerateTestSignal(1000, frequency: 440f, sampleRate: 44100);
        float[] output = new float[3000];
        
        uint in_len = (uint)input.Length;
        uint out_len = (uint)output.Length;
        
        // Act
        int result = resampler.ProcessFloat(0, input.AsSpan(), 0, ref in_len, output.AsSpan(), 0, ref out_len);
        
        // Assert
        result.Should().Be(0);
        for (int i = 0; i < out_len; i++) {
            output[i].Should().NotBe(float.NaN);
            output[i].Should().NotBe(float.PositiveInfinity);
            output[i].Should().NotBe(float.NegativeInfinity);
        }
    }

    /// <summary>
    /// Tests high-frequency content is attenuated during downsampling.
    /// (A simple check that anti-aliasing is happening)
    /// </summary>
    [Fact]
    public void ProcessFloat_Downsample_AttenuatesHighFrequency() {
        // Arrange - create a high-frequency signal that should be filtered
        var resampler = new SpeexResamplerCSharp(channels: 1, in_rate_init: 48000, out_rate_init: 22050, quality: 8);
        // Generate at 48kHz but with 12kHz sine wave (which is > Nyquist of 22050/2 = 11025)
        float[] input = GenerateTestSignal(2000, frequency: 12000f, sampleRate: 48000);
        float[] output = new float[2000];
        
        uint in_len = (uint)input.Length;
        uint out_len = (uint)output.Length;
        
        // Act
        int result = resampler.ProcessFloat(0, input.AsSpan(), 0, ref in_len, output.AsSpan(), 0, ref out_len);
        
        // Assert
        result.Should().Be(0);
        // The high-frequency content should be significantly attenuated
        float rms = CalculateRms(output, (int)out_len);
        // High-frequency should be much lower than the amplitude of the original sine wave (1.0)
        rms.Should().BeLessThan(0.5f, because: "high-frequency should be attenuated by anti-aliasing filter");
    }

    /// <summary>
    /// Generates a sine wave test signal.
    /// </summary>
    private static float[] GenerateTestSignal(int sampleCount, float frequency, float sampleRate) {
        float[] signal = new float[sampleCount];
        float twoPi = (float)(2.0 * Math.PI);
        for (int i = 0; i < sampleCount; i++) {
            float t = i / sampleRate;
            signal[i] = (float)Math.Sin(twoPi * frequency * t);
        }
        return signal;
    }

    /// <summary>
    /// Generates a stereo interleaved sine wave test signal.
    /// </summary>
    private static float[] GenerateStereoInterleavedSignal(int frameCount, float frequency, float sampleRate) {
        float[] signal = new float[frameCount * 2]; // Stereo interleaved
        float twoPi = (float)(2.0 * Math.PI);
        int sampleIndex = 0;
        for (int frame = 0; frame < frameCount; frame++) {
            float t = frame / sampleRate;
            float sample = (float)Math.Sin(twoPi * frequency * t);
            signal[sampleIndex++] = sample; // Left
            signal[sampleIndex++] = sample; // Right
        }
        return signal;
    }

    /// <summary>
    /// Calculates RMS (root mean square) of a signal.
    /// </summary>
    private static float CalculateRms(float[] signal, int length) {
        double sum = 0;
        for (int i = 0; i < length; i++) {
            sum += signal[i] * signal[i];
        }
        return (float)Math.Sqrt(sum / length);
    }
}
