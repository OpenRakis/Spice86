namespace Spice86.Tests.Sound;

using FluentAssertions;

using NSubstitute;

using Spice86.Audio.Filters;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>Unit tests for <see cref="FloppySoundEmulator"/>.</summary>
public sealed class FloppySoundEmulatorTests {
    private static SoftwareMixer CreateMixer() {
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();
        return new SoftwareMixer(AudioEngine.Dummy, pauseHandler);
    }

    [Fact]
    public void PlaySeek_EnablesChannel() {
        // Arrange
        SoftwareMixer mixer = CreateMixer();
        FloppySoundEmulator emulator = new(mixer);

        // Act
        emulator.PlaySeek();

        // Assert
        emulator.Channel.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void StartMotor_EnablesChannel() {
        // Arrange
        SoftwareMixer mixer = CreateMixer();
        FloppySoundEmulator emulator = new(mixer);

        // Act
        emulator.StartMotor();

        // Assert
        emulator.Channel.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void StopMotor_WithNoSeekPending_DisablesChannel() {
        // Arrange
        SoftwareMixer mixer = CreateMixer();
        FloppySoundEmulator emulator = new(mixer);
        emulator.StartMotor();

        // Act
        emulator.StopMotor();

        // Assert
        emulator.Channel.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ChannelIsDisabled_AfterConstruction() {
        // Arrange
        SoftwareMixer mixer = CreateMixer();

        // Act
        FloppySoundEmulator emulator = new(mixer);

        // Assert
        emulator.Channel.IsEnabled.Should().BeFalse();
    }
}
