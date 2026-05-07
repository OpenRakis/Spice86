namespace Spice86.Tests.Emulator.Devices.Input.Joystick.Mapping;

using FluentAssertions;

using NSubstitute;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.Input.Joystick.Mapping;
using Spice86.Shared.Emulator.Input.Joystick.Mapping;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;

using Xunit;

public sealed class MidiOnGameportRouterTests {
    private sealed class CapturingSink : IMpu401DataSink {
        public List<(int basePort, byte value)> Writes { get; } = new();

        public void WriteData(int basePort, byte value) {
            Writes.Add((basePort, value));
        }
    }

    private static (MidiOnGameportRouter router, CapturingSink sink, ILoggerService log) CreateRouter() {
        ILoggerService log = Substitute.For<ILoggerService>();
        log.IsEnabled(Arg.Any<LogEventLevel>()).Returns(true);
        CapturingSink sink = new();
        MidiOnGameportRouter router = new(sink, log);
        return (router, sink, log);
    }

    [Fact]
    public void DefaultsAreDisabled() {
        (MidiOnGameportRouter router, _, _) = CreateRouter();

        router.IsEnabled.Should().BeFalse();
        router.Mpu401BasePort.Should().Be(MidiOnGameportRouter.DefaultMpu401BasePort);
    }

    [Fact]
    public void OnGameportWrite_WhenDisabled_DoesNotForward() {
        (MidiOnGameportRouter router, CapturingSink sink, _) = CreateRouter();

        bool forwarded = router.OnGameportWrite(0x42);

        forwarded.Should().BeFalse();
        sink.Writes.Should().BeEmpty();
    }

    [Fact]
    public void Enable_ForwardsBytesToDefaultBasePort() {
        (MidiOnGameportRouter router, CapturingSink sink, _) = CreateRouter();
        router.Configure(new MidiOnGameportSettings { Enabled = true });

        router.OnGameportWrite(0x90).Should().BeTrue();
        router.OnGameportWrite(0x40).Should().BeTrue();

        sink.Writes.Should().Equal((0x330, (byte)0x90), (0x330, (byte)0x40));
    }

    [Fact]
    public void Enable_HonorsExplicitMpu401BasePort() {
        (MidiOnGameportRouter router, CapturingSink sink, _) = CreateRouter();
        router.Configure(new MidiOnGameportSettings { Enabled = true, Mpu401BasePort = 0x300 });

        router.OnGameportWrite(0x7F).Should().BeTrue();

        router.Mpu401BasePort.Should().Be(0x300);
        sink.Writes.Should().ContainSingle().Which.Should().Be((0x300, (byte)0x7F));
    }

    [Fact]
    public void Disable_StopsForwarding() {
        (MidiOnGameportRouter router, CapturingSink sink, _) = CreateRouter();
        router.Configure(new MidiOnGameportSettings { Enabled = true });
        router.OnGameportWrite(0x01);

        router.Configure(new MidiOnGameportSettings { Enabled = false });
        router.OnGameportWrite(0x02).Should().BeFalse();

        sink.Writes.Should().ContainSingle().Which.value.Should().Be(0x01);
        router.IsEnabled.Should().BeFalse();
        router.Mpu401BasePort.Should().Be(MidiOnGameportRouter.DefaultMpu401BasePort);
    }

    [Fact]
    public void ConfigureNull_DisablesAndResetsPort() {
        (MidiOnGameportRouter router, _, _) = CreateRouter();
        router.Configure(new MidiOnGameportSettings { Enabled = true, Mpu401BasePort = 0x320 });

        router.Configure(null);

        router.IsEnabled.Should().BeFalse();
        router.Mpu401BasePort.Should().Be(MidiOnGameportRouter.DefaultMpu401BasePort);
    }

    [Fact]
    public void Configure_LogsOnlyOnTransition() {
        (MidiOnGameportRouter router, _, ILoggerService log) = CreateRouter();
        MidiOnGameportSettings on = new() { Enabled = true };

        router.Configure(on);
        router.Configure(on);    // no transition
        router.Configure(on);    // no transition

        log.Received(1).Information(
            Arg.Is<string>(s => s.Contains("MIDI-on-gameport enabled")),
            Arg.Any<int>());
        log.DidNotReceive().Information(Arg.Is<string>(s => s.Contains("disabled")));
    }

    [Fact]
    public void Configure_LogsOnDisableTransition() {
        (MidiOnGameportRouter router, _, ILoggerService log) = CreateRouter();
        router.Configure(new MidiOnGameportSettings { Enabled = true });
        log.ClearReceivedCalls();

        router.Configure(new MidiOnGameportSettings { Enabled = false });

        log.Received(1).Information(Arg.Is<string>(s => s.Contains("disabled")));
    }

    [Fact]
    public void OnGameportWrite_WithNullSink_ReturnsFalseEvenWhenEnabled() {
        ILoggerService log = Substitute.For<ILoggerService>();
        log.IsEnabled(Arg.Any<LogEventLevel>()).Returns(true);
        MidiOnGameportRouter router = new(sink: null, log);
        router.Configure(new MidiOnGameportSettings { Enabled = true });

        router.OnGameportWrite(0xAA).Should().BeFalse();
    }

    [Fact]
    public void Reconfigure_UpdatesBasePortBetweenWrites() {
        (MidiOnGameportRouter router, CapturingSink sink, _) = CreateRouter();
        router.Configure(new MidiOnGameportSettings { Enabled = true, Mpu401BasePort = 0x330 });
        router.OnGameportWrite(0x11);

        router.Configure(new MidiOnGameportSettings { Enabled = true, Mpu401BasePort = 0x320 });
        router.OnGameportWrite(0x22);

        sink.Writes.Should().Equal(
            (0x330, (byte)0x11),
            (0x320, (byte)0x22));
    }
}
