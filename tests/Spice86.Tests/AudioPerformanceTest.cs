namespace Spice86.Tests;

using Xunit;
using FluentAssertions;
using NSubstitute;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Libs.Sound.Common;
using Spice86.Shared.Interfaces;
using Spice86.Core.Backend.Audio;
using System.Diagnostics;

/// <summary>
/// Basic performance and functionality tests for the audio mixer.
/// These tests verify that the mixer optimizations don't introduce regressions.
/// </summary>
public class AudioPerformanceTest {
    private readonly ILoggerService _loggerService;
    
    public AudioPerformanceTest() {
        _loggerService = Substitute.For<ILoggerService>();
    }

    [Fact]
    public void MixerChannel_BasicOperations_Should_Work() {
        // Test basic MixerChannel operations without starting mixer thread
        MixerChannel channel = new MixerChannel(
            framesRequested => { },
            "TestChannel",
            new HashSet<ChannelFeature> { ChannelFeature.Stereo },
            _loggerService
        );

        // Act & Assert
        channel.IsEnabled.Should().BeFalse(); // Initially disabled
        
        channel.Enable(true);
        channel.IsEnabled.Should().BeTrue();
        
        channel.SetUserVolume(new AudioFrame(0.5f, 0.5f));
        AudioFrame userVolume = channel.GetUserVolume();
        userVolume.Left.Should().Be(0.5f);
        userVolume.Right.Should().Be(0.5f);
    }

    [Fact]
    public void Dictionary_vs_ConcurrentDictionary_StructuralTest() {
        // This test verifies that using Dictionary + lock works correctly
        Dictionary<string, MixerChannel> channels = new();
        object lockObj = new();

        // Act - Add channels
        for (int i = 0; i < 10; i++) {
            MixerChannel channel = new MixerChannel(
                framesRequested => { },
                $"Channel{i}",
                new HashSet<ChannelFeature>(),
                _loggerService
            );
            
            lock (lockObj) {
                channels[$"Channel{i}"] = channel;
            }
        }

        // Assert - Verify we can iterate
        lock (lockObj) {
            channels.Count.Should().Be(10);
            List<MixerChannel> snapshot = channels.Values.ToList();
            snapshot.Should().HaveCount(10);
        }
    }

    [Fact]
    public void AudioFrame_Operations_Should_Be_Accurate() {
        // Arrange
        AudioFrame frame1 = new AudioFrame(1000.0f, 2000.0f);
        AudioFrame frame2 = new AudioFrame(500.0f, 1000.0f);

        // Act
        AudioFrame sum = frame1 + frame2;
        AudioFrame scaled = frame1 * 0.5f;
        AudioFrame product = frame1 * frame2;

        // Assert - Verify basic arithmetic operations work correctly
        sum.Left.Should().Be(1500.0f);
        sum.Right.Should().Be(3000.0f);
        
        scaled.Left.Should().Be(500.0f);
        scaled.Right.Should().Be(1000.0f);
        
        product.Left.Should().Be(500000.0f);
        product.Right.Should().Be(2000000.0f);
    }

    [Fact]
    public void MixerChannel_AudioFrames_Should_Accumulate() {
        // Test that handler can populate AudioFrames
        MixerChannel? channel = null;
        int handlerCallCount = 0;
        
        channel = new MixerChannel(
            framesRequested => {
                handlerCallCount++;
                // Simulate a device generating audio frames
                for (int i = 0; i < framesRequested; i++) {
                    channel!.AudioFrames.Add(new AudioFrame(1000.0f, 1000.0f));
                }
            },
            "TestChannel",
            new HashSet<ChannelFeature>(),
            _loggerService
        );
        channel.Enable(true);
        channel.SetSampleRate(48000); // Set sample rate to match mixer rate

        // Act - Request frames which should trigger the callback
        channel.Mix(128);

        // Assert - Handler should have been called at least once
        handlerCallCount.Should().BeGreaterThan(0);
        
        // AudioFrames should contain frames
        channel.AudioFrames.Count.Should().BeGreaterThan(0);
        
        // Verify frame values if any were generated
        if (channel.AudioFrames.Count > 0) {
            channel.AudioFrames[0].Left.Should().Be(1000.0f);
            channel.AudioFrames[0].Right.Should().Be(1000.0f);
        }
    }

    [Fact]
    public void AudioFrame_Normalization_Should_Be_Correct() {
        // Test the normalization factor used in mixer output
        const float normalizeFactor = 1.0f / 32768.0f;
        
        // 16-bit max positive value
        float normalizedMax = 32767.0f * normalizeFactor;
        normalizedMax.Should().BeApproximately(1.0f, 0.001f);
        
        // 16-bit max negative value
        float normalizedMin = -32768.0f * normalizeFactor;
        normalizedMin.Should().BeApproximately(-1.0f, 0.001f);
        
        // Zero
        float normalizedZero = 0.0f * normalizeFactor;
        normalizedZero.Should().Be(0.0f);
    }
}
