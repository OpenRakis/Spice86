namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;

using Spice86.Audio.Filters;
using Spice86.Core.Emulator.Devices.Sound;

using Xunit;

/// <summary>
/// Verifies sound-channel processing contracts independently from a host audio device.
/// </summary>
[Trait("Category", "Sound")]
public sealed class SoundChannelTests {
    /// <summary>
    /// Variable producer block sizes must not expose retained resampling-buffer capacity as active input.
    /// </summary>
    [Fact]
    public void AddSamplesFloat_AfterLargerBlock_ProcessesOnlyCurrentFrames() {
        // Arrange
        const int MixerRate = 48000;
        const int ChannelRate = 96000;
        const int StereoChannels = 2;
        const int LargeFrameCount = 64;
        const int SmallFrameCount = 1;
        SoundChannel channel = new(_ => { }, nameof(SoundChannelTests), []);
        channel.SetMixerSampleRate(MixerRate);
        channel.SampleRate = ChannelRate;
        channel.SetResampleMethod(ResampleMethod.Resample);
        float[] largeBlock = new float[LargeFrameCount * StereoChannels];
        float[] smallBlock = new float[SmallFrameCount * StereoChannels];
        channel.AddSamplesFloat(LargeFrameCount, largeBlock);
        int framesAfterLargeBlock = channel.AudioFrames.Count;

        // Act
        Action act = () => channel.AddSamplesFloat(SmallFrameCount, smallBlock);

        // Assert
        act.Should().NotThrow();
        channel.AudioFrames.Count.Should().BeInRange(framesAfterLargeBlock, framesAfterLargeBlock + SmallFrameCount);
    }
}
