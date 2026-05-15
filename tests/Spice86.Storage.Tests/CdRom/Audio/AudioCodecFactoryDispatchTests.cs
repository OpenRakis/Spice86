namespace Spice86.Storage.Tests.CdRom.Audio;

using FluentAssertions;

using NSubstitute;

using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Shared.Emulator.Storage.CdRom.Audio;

using System;

using Xunit;

/// <summary>
/// TDD tests for the <see cref="IAudioCodecFactory"/> dispatch surface
/// (Phase 4, atom 2). Verifies <see cref="CompositeAudioCodecFactory"/>
/// chains factories in order and that <see cref="WavAudioCodecFactory"/> /
/// <see cref="LibVlcAudioCodecFactory"/> claim the correct CUE file types.
/// </summary>
public sealed class AudioCodecFactoryDispatchTests {
    [Fact]
    public void WavAudioCodecFactory_CanHandle_Wave_ReturnsTrue() {
        // Arrange
        WavAudioCodecFactory factory = new();

        // Act
        bool wave = factory.CanHandle(CueFileType.Wave, "x.wav");
        bool mp3 = factory.CanHandle(CueFileType.Mp3, "x.mp3");

        // Assert
        wave.Should().BeTrue();
        mp3.Should().BeFalse();
    }

    [Fact]
    public void LibVlcAudioCodecFactory_CanHandle_CompressedTypes_ReturnsTrue() {
        // Arrange
        LibVlcAudioCodecFactory factory = new();

        // Act + Assert
        factory.CanHandle(CueFileType.Mp3, "a.mp3").Should().BeTrue();
        factory.CanHandle(CueFileType.Flac, "a.flac").Should().BeTrue();
        factory.CanHandle(CueFileType.Ogg, "a.ogg").Should().BeTrue();
        factory.CanHandle(CueFileType.Opus, "a.opus").Should().BeTrue();
        factory.CanHandle(CueFileType.Aiff, "a.aif").Should().BeTrue();
        factory.CanHandle(CueFileType.Motorola, "a.bin").Should().BeTrue();
        factory.CanHandle(CueFileType.Binary, "a.bin").Should().BeFalse();
        factory.CanHandle(CueFileType.Wave, "a.wav").Should().BeFalse();
    }

    [Fact]
    public void CompositeAudioCodecFactory_CreateFor_DispatchesToFirstMatchingFactory() {
        // Arrange
        IAudioCodec wavCodec = Substitute.For<IAudioCodec>();
        IAudioCodec mp3Codec = Substitute.For<IAudioCodec>();
        IAudioCodecFactory wavFactory = Substitute.For<IAudioCodecFactory>();
        wavFactory.CanHandle(CueFileType.Wave, Arg.Any<string>()).Returns(true);
        wavFactory.Create().Returns(wavCodec);
        IAudioCodecFactory mp3Factory = Substitute.For<IAudioCodecFactory>();
        mp3Factory.CanHandle(CueFileType.Mp3, Arg.Any<string>()).Returns(true);
        mp3Factory.Create().Returns(mp3Codec);
        CompositeAudioCodecFactory composite = new(wavFactory, mp3Factory);

        // Act
        IAudioCodec resolved = composite.CreateFor(CueFileType.Mp3, "track.mp3");

        // Assert
        resolved.Should().BeSameAs(mp3Codec);
        wavFactory.DidNotReceive().Create();
    }

    [Fact]
    public void CompositeAudioCodecFactory_CreateFor_NoMatch_ThrowsNotSupportedException() {
        // Arrange
        IAudioCodecFactory wavFactory = Substitute.For<IAudioCodecFactory>();
        wavFactory.CanHandle(Arg.Any<CueFileType>(), Arg.Any<string>()).Returns(false);
        CompositeAudioCodecFactory composite = new(wavFactory);

        // Act
        Action act = () => composite.CreateFor(CueFileType.Mp3, "a.mp3");

        // Assert
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void DefaultAudioCodecFactory_Create_HandlesWavAndLibVlcSupportedTypes() {
        // Arrange
        CompositeAudioCodecFactory factory = DefaultAudioCodecFactory.Create();

        // Act + Assert
        factory.CanHandle(CueFileType.Wave, "a.wav").Should().BeTrue();
        factory.CanHandle(CueFileType.Mp3, "a.mp3").Should().BeTrue();
        factory.CanHandle(CueFileType.Flac, "a.flac").Should().BeTrue();
        factory.CanHandle(CueFileType.Binary, "a.bin").Should().BeFalse();
    }
}
