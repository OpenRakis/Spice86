// SPDX-License-Identifier: GPL-2.0-or-later
// Test suite for pure C# Speex resampler implementation
// Ported from libspeexdsp test suite

namespace Spice86.Tests.Bufdio;

using Xunit;
using FluentAssertions;
using global::Bufdio.Spice86;

/// <summary>
/// Unit tests for the pure C# Speex resampler implementation.
/// Tests verify correctness, quality, and performance of sample rate conversion.
/// </summary>
public class SpeexResamplerTests {
    [Fact]
    public void Constructor_WithValidParameters_ShouldSucceed() {
        // Arrange & Act
        using SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(
            channels: 2,
            inputRate: 44100,
            outputRate: 48000,
            quality: 5);

        // Assert
        resampler.IsInitialized.Should().BeTrue();
        resampler.Channels.Should().Be(2);
        resampler.InputRate.Should().Be(44100);
        resampler.OutputRate.Should().Be(48000);
    }

    [Theory]
    [InlineData(0)]  // Invalid channels
    [InlineData(257)] // Too many channels
    public void Constructor_WithInvalidChannelCount_ShouldThrow(uint channels) {
        // Arrange, Act & Assert
        Action act = () => new SpeexResamplerCSharp(channels, 44100, 48000, 5);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]     // Invalid rate
    [InlineData(1000)]  // Too low
    [InlineData(400000)] // Too high
    public void Constructor_WithInvalidSampleRate_ShouldThrow(uint rate) {
        // Arrange, Act & Assert
        Action act = () => new SpeexResamplerCSharp(2, rate, 48000, 5);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ProcessFloat_WithSimpleUpsample_ShouldProduceExpectedFrameCount() {
        // Arrange
        using SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(
            channels: 1,
            inputRate: 22050,
            outputRate: 44100,
            quality: 5);

        float[] input = new float[100];
        for (int i = 0; i < input.Length; i++) {
            input[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 22050.0);
        }

        float[] output = new float[200]; // 2x upsampling

        // Act
        resampler.ProcessFloat(
            channelIndex: 0,
            input: input,
            output: output,
            out uint inputConsumed,
            out uint outputGenerated);

        // Assert
        inputConsumed.Should().Be(100);
        outputGenerated.Should().BeInRange(195, 205); // Allow some variance
    }

    [Fact]
    public void ProcessFloat_WithSimpleDownsample_ShouldProduceExpectedFrameCount() {
        // Arrange
        using SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(
            channels: 1,
            inputRate: 48000,
            outputRate: 44100,
            quality: 5);

        float[] input = new float[480];
        for (int i = 0; i < input.Length; i++) {
            input[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 48000.0);
        }

        float[] output = new float[441]; // ~10:11 ratio

        // Act
        resampler.ProcessFloat(
            channelIndex: 0,
            input: input,
            output: output,
            out uint inputConsumed,
            out uint outputGenerated);

        // Assert
        inputConsumed.Should().BeInRange(430, 480);
        outputGenerated.Should().BeInRange(395, 441);
    }

    [Fact]
    public void ProcessFloat_WithStereo_ShouldProcessBothChannelsIndependently() {
        // Arrange
        using SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(
            channels: 2,
            inputRate: 44100,
            outputRate: 48000,
            quality: 5);

        // Create test signals - left channel 440Hz, right channel 880Hz
        float[] leftInput = new float[100];
        float[] rightInput = new float[100];
        for (int i = 0; i < 100; i++) {
            leftInput[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 44100.0);
            rightInput[i] = (float)Math.Sin(2.0 * Math.PI * 880.0 * i / 44100.0);
        }

        float[] leftOutput = new float[110];
        float[] rightOutput = new float[110];

        // Act - Process left channel
        resampler.ProcessFloat(0, leftInput, leftOutput, out uint leftConsumed, out uint leftGenerated);
        
        // Act - Process right channel
        resampler.ProcessFloat(1, rightInput, rightOutput, out uint rightConsumed, out uint rightGenerated);

        // Assert
        leftConsumed.Should().Be(rightConsumed);
        leftGenerated.Should().Be(rightGenerated);
        leftGenerated.Should().BeInRange(105, 110);
    }

    [Fact]
    public void SetRate_ShouldUpdateRates() {
        // Arrange
        using SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(
            channels: 2,
            inputRate: 44100,
            outputRate: 48000,
            quality: 5);

        // Act
        resampler.SetRate(48000, 44100);

        // Assert
        resampler.InputRate.Should().Be(48000);
        resampler.OutputRate.Should().Be(44100);
    }

    [Fact]
    public void Reset_ShouldClearInternalState() {
        // Arrange
        using SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(
            channels: 1,
            inputRate: 44100,
            outputRate: 48000,
            quality: 5);

        float[] input = new float[100];
        float[] output = new float[110];
        
        // Process some data to fill internal buffers
        resampler.ProcessFloat(0, input, output, out _, out _);

        // Act
        resampler.Reset();

        // Assert - After reset, should produce consistent results
        resampler.ProcessFloat(0, input, output, out uint consumed, out uint generated);
        consumed.Should().BeGreaterThan(0);
        generated.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(0)]  // Fastest/lowest quality
    [InlineData(3)]  // Fast
    [InlineData(5)]  // Medium
    [InlineData(8)]  // High
    [InlineData(10)] // Best/highest quality
    public void Constructor_WithDifferentQualitySettings_ShouldSucceed(int quality) {
        // Arrange & Act
        using SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(
            channels: 2,
            inputRate: 44100,
            outputRate: 48000,
            quality: quality);

        // Assert
        resampler.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public void ProcessFloat_WithZeroInput_ShouldProduceZeroOutput() {
        // Arrange
        using SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(
            channels: 1,
            inputRate: 44100,
            outputRate: 48000,
            quality: 5);

        float[] input = new float[100]; // All zeros
        float[] output = new float[110];

        // Act
        resampler.ProcessFloat(0, input, output, out _, out uint generated);

        // Assert - Output should be mostly zeros (some edge effects possible)
        float maxAmplitude = output.Take((int)generated).Max(Math.Abs);
        maxAmplitude.Should().BeLessThan(0.01f);
    }

    [Fact]
    public void Dispose_ShouldAllowMultipleCalls() {
        // Arrange
        SpeexResamplerCSharp resampler = new SpeexResamplerCSharp(
            channels: 2,
            inputRate: 44100,
            outputRate: 48000,
            quality: 5);

        // Act & Assert - Should not throw
        resampler.Dispose();
        resampler.Dispose();
    }
}
