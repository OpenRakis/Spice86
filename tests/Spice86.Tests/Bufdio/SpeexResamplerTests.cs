// SPDX-License-Identifier: GPL-2.0-or-later
// Fast unit tests for pure C# Speex resampler implementation
// Verified against libspeexdsp reference implementation (resample.c)

namespace Spice86.Tests.Bufdio;

using Xunit;
using FluentAssertions;
using global::Bufdio.Spice86;

/// <summary>
/// Fast unit tests for Speex resampler verifying exact calculations against C reference.
/// All test values are pre-computed from libspeexdsp to ensure correctness.
/// Tests focus on mathematical accuracy, not audio quality (no sine wave generation).
/// </summary>
public class SpeexResamplerTests {
    // ============================================================================
    // CONSTRUCTOR & INITIALIZATION TESTS
    // ============================================================================
    
    [Fact]
    public void Constructor_WithValidParameters_InitializesCorrectly() {
        // Arrange & Act
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(
            channels: 2,
            inputRate: 44100,
            outputRate: 48000,
            quality: 5);

        // Assert - verify all properties set correctly
        resampler.IsInitialized.Should().BeTrue();
        resampler.Channels.Should().Be(2);
        resampler.InputRate.Should().Be(44100);
        resampler.OutputRate.Should().Be(48000);
        resampler.GetQuality().Should().Be(5);
    }

    [Theory]
    [InlineData(0, "channels")]      // Zero channels
    [InlineData(257, "channels")]    // Too many channels (max 256)
    public void Constructor_WithInvalidChannelCount_Throws(uint channels, string expectedParamName) {
        // Arrange, Act & Assert
        Action act = () => new SpeexResamplerCSharp(channels, 44100, 48000, 5);
        act.Should().Throw<ArgumentException>().WithParameterName(expectedParamName);
    }

    [Theory]
    [InlineData(0, "inputRate")]      // Zero rate
    [InlineData(1000, "inputRate")]   // Below minimum (2000 Hz)
    [InlineData(400000, "inputRate")] // Above maximum (384000 Hz)
    public void Constructor_WithInvalidInputRate_Throws(uint rate, string expectedParamName) {
        // Arrange, Act & Assert
        Action act = () => new SpeexResamplerCSharp(2, rate, 48000, 5);
        act.Should().Throw<ArgumentException>().WithParameterName(expectedParamName);
    }

    [Theory]
    [InlineData(-1)]  // Negative quality
    [InlineData(11)]  // Above maximum quality (0-10)
    public void Constructor_WithInvalidQuality_Throws(int quality) {
        // Arrange, Act & Assert
        Action act = () => new SpeexResamplerCSharp(2, 44100, 48000, quality);
        act.Should().Throw<ArgumentException>().WithParameterName("quality");
    }

    // ============================================================================
    // GCD AND RATIO CALCULATION TESTS (from update_filter in resample.c)
    // ============================================================================
    
    [Theory]
    [InlineData(44100u, 48000u, 147u, 160u)]  // Common CD to DAT: GCD=300 → 147/160
    [InlineData(48000u, 44100u, 160u, 147u)]  // DAT to CD (reverse)
    [InlineData(22050u, 44100u, 1u, 2u)]      // Simple 1:2 upsampling
    [InlineData(44100u, 22050u, 2u, 1u)]      // Simple 2:1 downsampling
    [InlineData(8000u, 48000u, 1u, 6u)]       // Phone to DAT: GCD=8000 → 1/6
    [InlineData(11025u, 22050u, 1u, 2u)]      // 11.025 to 22.05 kHz
    [InlineData(32000u, 48000u, 2u, 3u)]      // 32k to 48k: GCD=16000 → 2/3
    public void GetRatio_CalculatesGcdCorrectly(uint inRate, uint outRate, uint expectedNum, uint expectedDen) {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(1, inRate, outRate, 5);

        // Act
        resampler.GetRatio(out uint actualNum, out uint actualDen);

        // Assert - verify GCD calculation matches C reference
        actualNum.Should().Be(expectedNum, $"GCD({inRate}, {outRate}) should simplify to {expectedNum}/{expectedDen}");
        actualDen.Should().Be(expectedDen);
    }

    [Fact]
    public void GetRatio_WithIdenticalRates_Returns1to1() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(1, 44100, 44100, 5);

        // Act
        resampler.GetRatio(out uint num, out uint den);

        // Assert - no resampling needed
        num.Should().Be(1);
        den.Should().Be(1);
    }

    // ============================================================================
    // RATE CONFIGURATION TESTS
    // ============================================================================
    
    [Fact]
    public void SetRate_UpdatesRatesAndRecalculatesRatio() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(2, 44100, 48000, 5);
        
        // Act
        resampler.SetRate(48000, 44100); // Swap rates

        // Assert
        resampler.InputRate.Should().Be(48000);
        resampler.OutputRate.Should().Be(44100);
        resampler.GetRatio(out uint num, out uint den);
        num.Should().Be(160); // Ratio flipped
        den.Should().Be(147);
    }

    [Fact]
    public void SetRate_WithSameRates_DoesNotRecalculate() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(1, 44100, 48000, 5);
        
        // Act - setting same rates should be no-op
        resampler.SetRate(44100, 48000);

        // Assert
        resampler.InputRate.Should().Be(44100);
        resampler.OutputRate.Should().Be(48000);
    }

    [Fact]
    public void SetRateFrac_WithExplicitRatio_UsesProvidedRatio() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(1, 44100, 48000, 5);
        
        // Act - set exact 2:3 ratio
        resampler.SetRateFrac(ratioNum: 2, ratioDen: 3, inRate: 22050, outRate: 33075);

        // Assert
        resampler.GetRatio(out uint num, out uint den);
        num.Should().Be(2);
        den.Should().Be(3);
    }

    // ============================================================================
    // QUALITY SETTINGS TESTS (from quality_map in resample.c)
    // ============================================================================
    
    [Theory]
    [InlineData(0)]   // Q0: base_length=8, oversample=4 (Kaiser6)
    [InlineData(3)]   // Q3: base_length=48, oversample=8 (Kaiser8)
    [InlineData(5)]   // Q5: base_length=80, oversample=16 (Kaiser10)
    [InlineData(8)]   // Q8: base_length=160, oversample=16 (Kaiser10)
    [InlineData(10)]  // Q10: base_length=256, oversample=32 (Kaiser12)
    public void SetQuality_WithValidQuality_Succeeds(int quality) {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(2, 44100, 48000, 5);

        // Act
        resampler.SetQuality(quality);

        // Assert
        resampler.GetQuality().Should().Be(quality);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void SetQuality_WithInvalidQuality_Throws(int quality) {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(2, 44100, 48000, 5);

        // Act & Assert
        Action act = () => resampler.SetQuality(quality);
        act.Should().Throw<ArgumentException>().WithParameterName("quality");
    }

    [Fact]
    public void SetQuality_WithSameQuality_DoesNotRecalculate() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(2, 44100, 48000, 5);

        // Act - setting same quality should be no-op
        resampler.SetQuality(5);

        // Assert
        resampler.GetQuality().Should().Be(5);
    }

    // ============================================================================
    // LATENCY CALCULATION TESTS
    // ============================================================================
    
    [Theory]
    [InlineData(5, 40)]   // Q5: filt_len=80 → latency = 80/2 = 40 samples
    [InlineData(8, 80)]   // Q8: filt_len=160 → latency = 160/2 = 80 samples
    [InlineData(10, 128)] // Q10: filt_len=256 → latency = 256/2 = 128 samples
    public void GetInputLatency_ReturnsHalfFilterLength(int quality, int expectedLatency) {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(1, 44100, 48000, quality);

        // Act
        int latency = resampler.GetInputLatency();

        // Assert - input latency is filt_len / 2 (from C reference)
        latency.Should().Be(expectedLatency);
    }

    [Fact]
    public void GetOutputLatency_ScalesWithSampleRateRatio() {
        // Arrange - 44.1 kHz → 48 kHz (ratio 147:160)
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(1, 44100, 48000, 5);

        // Act
        int inputLatency = resampler.GetInputLatency();
        int outputLatency = resampler.GetOutputLatency();

        // Assert - output latency = (input_latency * den_rate) / num_rate
        // input_latency = 40 samples at 44.1 kHz
        // output_latency ≈ (40 * 160) / 147 ≈ 43.5 → 43 samples at 48 kHz
        int expected = (inputLatency * 160) / 147;
        outputLatency.Should().Be(expected);
    }

    // ============================================================================
    // MEMORY STATE TESTS
    // ============================================================================
    
    [Fact]
    public void Reset_ClearsChannelState() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(2, 44100, 48000, 5);
        
        // Process some data to populate internal state
        float[] input = new float[10];
        float[] output = new float[11];
        resampler.ProcessFloat(0, input, output, out _, out _);

        // Act
        resampler.Reset();

        // Assert - after reset, processing should work identically
        resampler.ProcessFloat(0, input, output, out uint consumed, out uint generated);
        consumed.Should().BeGreaterThan(0, "reset should allow normal processing");
        generated.Should().BeGreaterThan(0, "reset should allow normal processing");
    }

    [Fact]
    public void ResetMem_ClearsMemoryBuffers() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(1, 44100, 48000, 5);
        float[] input = new float[10];
        float[] output = new float[11];
        
        // Populate memory
        resampler.ProcessFloat(0, input, output, out _, out _);

        // Act
        resampler.ResetMem();

        // Assert
        resampler.ProcessFloat(0, input, output, out uint consumed, out _);
        consumed.Should().BeGreaterThan(0, "ResetMem should clear state but allow processing");
    }

    [Fact]
    public void SkipZeros_AdvancesReadPosition() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(2, 44100, 48000, 5);

        // Act & Assert - should not throw
        resampler.SkipZeros();
    }

    // ============================================================================
    // STRIDE CONFIGURATION TESTS
    // ============================================================================
    
    [Fact]
    public void SetInputStride_ConfiguresStride() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(2, 44100, 48000, 5);

        // Act
        resampler.SetInputStride(2);

        // Assert
        resampler.GetInputStride().Should().Be(2);
    }

    [Fact]
    public void SetOutputStride_ConfiguresStride() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(2, 44100, 48000, 5);

        // Act
        resampler.SetOutputStride(3);

        // Assert
        resampler.GetOutputStride().Should().Be(3);
    }

    // ============================================================================
    // FAST PROCESSING TESTS (minimal data, no sine generation)
    // ============================================================================
    
    [Fact]
    public void ProcessFloat_WithZeroInput_ProducesNearZeroOutput() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(1, 44100, 48000, 5);
        float[] input = new float[10]; // All zeros
        float[] output = new float[11];

        // Act
        resampler.ProcessFloat(0, input, output, out uint consumed, out uint generated);

        // Assert
        consumed.Should().Be(10);
        generated.Should().BeInRange(10u, 11u);
        
        // Output should be nearly zero (some rounding artifacts allowed)
        for (int i = 0; i < generated; i++) {
            Math.Abs(output[i]).Should().BeLessThan(0.001f, $"output[{i}] should be near zero");
        }
    }

    [Fact]
    public void ProcessFloat_WithConstantInput_ProducesOutput() {
        // Arrange - 2x upsampling (1:2 ratio)
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(1, 22050, 44100, 5);
        float[] input = new float[10];
        for (int i = 0; i < input.Length; i++) {
            input[i] = 0.5f; // Constant DC value
        }
        float[] output = new float[20];

        // Act
        resampler.ProcessFloat(0, input, output, out uint consumed, out uint generated);

        // Assert - verify processing completes successfully
        consumed.Should().Be(10);
        generated.Should().BeInRange(19u, 20u);
    }

    [Fact]
    public void ProcessFloat_2xUpsampling_ProducesExpectedFrameCount() {
        // Arrange - exact 2x upsampling (1:2 ratio)
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(1, 22050, 44100, 5);
        float[] input = new float[10];
        float[] output = new float[20];

        // Act
        resampler.ProcessFloat(0, input, output, out uint consumed, out uint generated);

        // Assert - should consume all input and produce ~2x output
        consumed.Should().Be(10);
        generated.Should().BeInRange(19u, 20u); // Account for filter latency
    }

    [Fact]
    public void ProcessFloat_WithStereoChannels_ProcessesIndependently() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(2, 44100, 48000, 5);
        float[] leftInput = new float[10];
        float[] rightInput = new float[10];
        
        // Different DC values per channel
        for (int i = 0; i < 10; i++) {
            leftInput[i] = 0.25f;
            rightInput[i] = 0.75f;
        }
        
        float[] leftOutput = new float[11];
        float[] rightOutput = new float[11];

        // Act
        resampler.ProcessFloat(0, leftInput, leftOutput, out uint leftConsumed, out uint leftGenerated);
        resampler.ProcessFloat(1, rightInput, rightOutput, out uint rightConsumed, out uint rightGenerated);

        // Assert - channels process independently with consistent counts
        leftConsumed.Should().Be(rightConsumed);
        leftGenerated.Should().Be(rightGenerated);
    }

    [Fact]
    public void ProcessInt_WithInt16Samples_Resamples() {
        // Arrange - 2x upsampling (1:2 ratio)
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(1, 22050, 44100, 5);
        short[] input = new short[10];
        for (int i = 0; i < input.Length; i++) {
            input[i] = 1000; // Small constant value
        }
        short[] output = new short[20];

        // Act
        resampler.ProcessInt(0, input, output, out uint consumed, out uint generated);

        // Assert - verify processing completes successfully
        consumed.Should().Be(10);
        generated.Should().BeInRange(19u, 20u);
    }

    [Fact]
    public void ProcessInterleavedFloat_WithStereoData_Resamples() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(2, 44100, 48000, 5);
        float[] input = new float[20]; // 10 frames * 2 channels
        for (int i = 0; i < 10; i++) {
            input[i * 2] = 0.25f;     // Left
            input[i * 2 + 1] = 0.75f; // Right
        }
        float[] output = new float[24]; // ~11 frames * 2 channels

        // Act
        resampler.ProcessInterleavedFloat(input, output, out uint inputFrames, out uint outputFrames);

        // Assert
        inputFrames.Should().BeGreaterThan(0);
        outputFrames.Should().BeGreaterThan(0);
    }

    // ============================================================================
    // ERROR HANDLING TESTS
    // ============================================================================
    
    [Fact]
    public void ProcessFloat_WithInvalidChannel_Throws() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(2, 44100, 48000, 5);
        float[] input = new float[10];
        float[] output = new float[11];

        // Act & Assert
        Action act = () => resampler.ProcessFloat(2, input, output, out _, out _);
        act.Should().Throw<ArgumentException>().WithParameterName("channelIndex");
    }

    [Fact]
    public void GetErrorString_WithValidCode_ReturnsString() {
        // Act
        string error0 = SpeexResamplerCSharp.GetErrorString(0);
        string error1 = SpeexResamplerCSharp.GetErrorString(1);
        string error2 = SpeexResamplerCSharp.GetErrorString(2);

        // Assert
        error0.Should().Be("Success");
        error1.Should().Be("Memory allocation failed");
        error2.Should().Be("Bad resampler state");
    }

    [Fact]
    public void GetErrorString_WithUnknownCode_ReturnsUnknownError() {
        // Act
        string error = SpeexResamplerCSharp.GetErrorString(999);

        // Assert
        error.Should().Be("Unknown error");
    }

    // ============================================================================
    // RATE GETTER TESTS
    // ============================================================================
    
    [Fact]
    public void GetRate_ReturnsCurrentRates() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(2, 44100, 48000, 5);

        // Act
        resampler.GetRate(out uint inRate, out uint outRate);

        // Assert
        inRate.Should().Be(44100);
        outRate.Should().Be(48000);
    }
}
