namespace Spice86.Tests.Emulator.Devices.Input.Joystick.Mapping;

using FluentAssertions;

using NSubstitute;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.Input.Joystick.Mapping;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick.Mapping;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

using Xunit;

public sealed class RumbleRouterTests {
    private sealed class CapturingSink : IRumbleSink {
        public bool IsSupported { get; set; } = true;
        public List<(int stickIndex, RumbleEffect effect)> Plays { get; } = new();

        public void Play(int stickIndex, RumbleEffect effect) {
            Plays.Add((stickIndex, effect));
        }
    }

    private static (RumbleRouter router, CapturingSink sink, ILoggerService log) CreateRouter() {
        ILoggerService log = Substitute.For<ILoggerService>();
        log.IsEnabled(Arg.Any<LogEventLevel>()).Returns(true);
        CapturingSink sink = new();
        RumbleRouter router = new(sink, log);
        return (router, sink, log);
    }

    [Fact]
    public void DefaultsAreEnabledWithUnitScale() {
        (RumbleRouter router, _, _) = CreateRouter();

        router.IsEnabled.Should().BeTrue();
        router.AmplitudeScale.Should().Be(1.0);
        router.IsSinkSupported.Should().BeTrue();
    }

    [Fact]
    public void Play_ForwardsEffectAtUnitScale() {
        (RumbleRouter router, CapturingSink sink, _) = CreateRouter();
        RumbleEffect effect = new(0.4f, 0.6f, 250);

        router.Play(1, effect).Should().BeTrue();

        sink.Plays.Should().ContainSingle()
            .Which.Should().Be((1, effect));
    }

    [Fact]
    public void Play_ScalesAmplitudesAndPreservesDuration() {
        (RumbleRouter router, CapturingSink sink, _) = CreateRouter();
        router.Configure(new RumbleMapping { Enabled = true, AmplitudeScale = 0.5 });

        router.Play(0, new RumbleEffect(0.8f, 1.0f, 200)).Should().BeTrue();

        (int stick, RumbleEffect effect) = sink.Plays[0];
        stick.Should().Be(0);
        effect.LowFrequencyAmplitude.Should().BeApproximately(0.4f, 1e-6f);
        effect.HighFrequencyAmplitude.Should().BeApproximately(0.5f, 1e-6f);
        effect.DurationMilliseconds.Should().Be(200);
    }

    [Fact]
    public void Play_ClampsAmplitudesAndDuration() {
        (RumbleRouter router, CapturingSink sink, _) = CreateRouter();
        router.Configure(new RumbleMapping { AmplitudeScale = 5.0 });

        router.Play(0, new RumbleEffect(2.0f, -0.5f, -10)).Should().BeTrue();

        (_, RumbleEffect effect) = sink.Plays[0];
        effect.LowFrequencyAmplitude.Should().Be(1.0f);
        effect.HighFrequencyAmplitude.Should().Be(0.0f);
        effect.DurationMilliseconds.Should().Be(0);
    }

    [Fact]
    public void Disable_DropsEffectsWithoutForwarding() {
        (RumbleRouter router, CapturingSink sink, _) = CreateRouter();
        router.Configure(new RumbleMapping { Enabled = false });

        router.Play(0, new RumbleEffect(0.5f, 0.5f, 100)).Should().BeFalse();

        sink.Plays.Should().BeEmpty();
    }

    [Fact]
    public void Stop_ForwardsRumbleStopEvenWhenDisabled() {
        (RumbleRouter router, CapturingSink sink, _) = CreateRouter();
        router.Configure(new RumbleMapping { Enabled = false });

        router.Stop(1).Should().BeTrue();

        sink.Plays.Should().ContainSingle()
            .Which.Should().Be((1, RumbleEffect.Stop));
    }

    [Fact]
    public void Play_WithUnsupportedSink_ReturnsFalse() {
        (RumbleRouter router, CapturingSink sink, _) = CreateRouter();
        sink.IsSupported = false;

        router.Play(0, new RumbleEffect(0.5f, 0.5f, 50)).Should().BeFalse();
        router.Stop(0).Should().BeFalse();
        sink.Plays.Should().BeEmpty();
        router.IsSinkSupported.Should().BeFalse();
    }

    [Fact]
    public void Play_WithNullSink_ReturnsFalse() {
        ILoggerService log = Substitute.For<ILoggerService>();
        log.IsEnabled(Arg.Any<LogEventLevel>()).Returns(true);
        RumbleRouter router = new(sink: null, log);

        router.Play(0, new RumbleEffect(1f, 1f, 100)).Should().BeFalse();
        router.Stop(0).Should().BeFalse();
        router.IsSinkSupported.Should().BeFalse();
    }

    [Fact]
    public void Configure_LogsOnlyOnTransition() {
        (RumbleRouter router, _, ILoggerService log) = CreateRouter();
        log.ClearReceivedCalls();

        router.Configure(new RumbleMapping { Enabled = false });
        router.Configure(new RumbleMapping { Enabled = false }); // no transition

        log.Received(1).Information(Arg.Is<string>(s => s.Contains("disabled")));
        log.DidNotReceive().Information(
            Arg.Is<string>(s => s.Contains("enabled")),
            Arg.Any<double>());
    }

    [Fact]
    public void Configure_LogsOnEnableTransitionWithScale() {
        (RumbleRouter router, _, ILoggerService log) = CreateRouter();
        router.Configure(new RumbleMapping { Enabled = false });
        log.ClearReceivedCalls();

        router.Configure(new RumbleMapping { Enabled = true, AmplitudeScale = 0.75 });

        log.Received(1).Information(
            Arg.Is<string>(s => s.Contains("rumble enabled")),
            Arg.Is<double>(d => Math.Abs(d - 0.75) < 1e-6));
    }

    [Fact]
    public void ConfigureNull_RestoresDefaults() {
        (RumbleRouter router, _, _) = CreateRouter();
        router.Configure(new RumbleMapping { Enabled = false, AmplitudeScale = 0.25 });

        router.Configure(null);

        router.IsEnabled.Should().BeTrue();
        router.AmplitudeScale.Should().Be(1.0);
    }

    [Fact]
    public void Configure_ClampsScaleToUnit() {
        (RumbleRouter router, _, _) = CreateRouter();

        router.Configure(new RumbleMapping { AmplitudeScale = 5.0 });
        router.AmplitudeScale.Should().Be(1.0);

        router.Configure(new RumbleMapping { AmplitudeScale = -1.0 });
        router.AmplitudeScale.Should().Be(0.0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Play_RejectsInvalidStickIndex(int stickIndex) {
        (RumbleRouter router, _, _) = CreateRouter();

        Action act = () => router.Play(stickIndex, RumbleEffect.Stop);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("stickIndex");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Stop_RejectsInvalidStickIndex(int stickIndex) {
        (RumbleRouter router, _, _) = CreateRouter();

        Action act = () => router.Stop(stickIndex);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("stickIndex");
    }
}
