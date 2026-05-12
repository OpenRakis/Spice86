namespace Spice86.Tests.CdRom;

using FluentAssertions;

using NSubstitute;

using Spice86.Audio.Filters;
using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.Devices.CdRom.Image;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

using Xunit;

/// <summary>Tests for <see cref="CdAudioPlayer"/> channel state management.</summary>
public sealed class CdAudioPlayerTests {
    private static SoftwareMixer CreateMixer() {
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();
        return new SoftwareMixer(AudioEngine.Dummy, pauseHandler);
    }

    /// <summary>A concrete CD-ROM image implementation for testing that avoids Span limitations.</summary>
    private sealed class FakeAudioImage : ICdRomImage {
        public string? UpcEan => null;
        public string ImagePath => string.Empty;
        public IReadOnlyList<CdTrack> Tracks => new List<CdTrack>();
        public int TotalSectors => 100_000;
        public IsoVolumeDescriptor PrimaryVolume => new(string.Empty, 0, 0, 2048, 100_000);

        public int Read(int lba, Span<byte> destination, CdSectorMode mode) {
            destination.Clear();
            return destination.Length;
        }

        public void Dispose() { }
    }

    [Fact]
    public void StartPlayback_EnablesChannel() {
        // Arrange
        SoftwareMixer mixer = CreateMixer();
        CdAudioPlayer player = new(mixer);

        // Act
        player.StartPlayback();

        // Assert
        player.Channel.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void StopPlayback_DisablesChannel() {
        // Arrange
        SoftwareMixer mixer = CreateMixer();
        CdAudioPlayer player = new(mixer);
        player.StartPlayback();

        // Act
        player.StopPlayback();

        // Assert
        player.Channel.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void PausePlayback_DisablesChannel() {
        // Arrange
        SoftwareMixer mixer = CreateMixer();
        CdAudioPlayer player = new(mixer);
        player.StartPlayback();

        // Act
        player.PausePlayback();

        // Assert
        player.Channel.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ResumePlayback_EnablesChannel() {
        // Arrange
        SoftwareMixer mixer = CreateMixer();
        CdAudioPlayer player = new(mixer);

        // Act
        player.ResumePlayback();

        // Assert
        player.Channel.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void PauseAudio_SetsStatusPaused() {
        // Arrange
        CdRomDrive drive = new(new FakeAudioImage());
        drive.PlayAudio(0, 50);

        // Act
        drive.PauseAudio();

        // Assert
        drive.GetAudioStatus().Status.Should().Be(CdAudioStatus.Paused);
    }

    [Fact]
    public void ResumeAudio_FromPaused_RestoresPlayingStatus() {
        // Arrange
        CdRomDrive drive = new(new FakeAudioImage());
        drive.PlayAudio(0, 50);
        drive.PauseAudio();

        // Act
        drive.ResumeAudio();

        // Assert
        drive.GetAudioStatus().Status.Should().Be(CdAudioStatus.Playing);
    }

    [Fact]
    public void PauseAudio_InvokesPausePlayback_WhenPlayerSet() {
        // Arrange
        SoftwareMixer mixer = CreateMixer();
        CdRomDrive drive = new(new FakeAudioImage());
        CdAudioPlayer player = new(mixer);
        player.SetDrive(drive);
        drive.SetAudioPlayer(player);
        drive.PlayAudio(0, 100_000);

        // Act
        drive.PauseAudio();

        // Assert
        player.Channel.IsEnabled.Should().BeFalse();
        drive.GetAudioStatus().Status.Should().Be(CdAudioStatus.Paused);
    }

    [Fact]
    public void ResumeAudio_InvokesResumePlayback_WhenPlayerSet() {
        // Arrange
        SoftwareMixer mixer = CreateMixer();
        CdRomDrive drive = new(new FakeAudioImage());
        CdAudioPlayer player = new(mixer);
        player.SetDrive(drive);
        drive.SetAudioPlayer(player);
        drive.PlayAudio(0, 100_000);
        drive.PauseAudio();

        // Act
        drive.ResumeAudio();

        // Assert
        player.Channel.IsEnabled.Should().BeTrue();
        drive.GetAudioStatus().Status.Should().Be(CdAudioStatus.Playing);
    }
}

