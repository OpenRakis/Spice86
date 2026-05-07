namespace Spice86.Tests.Emulator.Devices.Input.Joystick.Mapping;

using FluentAssertions;

using NSubstitute;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.Input.Joystick.Mapping;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Input.Joystick.Mapping;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;

using Xunit;

public sealed class JoystickProfileActivatorTests {
    private static (FakeJoystickEventSource events,
                    MidiOnGameportRouter midi,
                    RumbleRouter rumble,
                    JoystickProfileActivator activator)
        BuildActivator(IReadOnlyList<JoystickProfile> profiles, string defaultProfileName = "") {
        ILoggerService log = Substitute.For<ILoggerService>();
        log.IsEnabled(Arg.Any<LogEventLevel>()).Returns(true);
        IJoystickMappingStore store = Substitute.For<IJoystickMappingStore>();
        JoystickProfileAutoLoader loader = new(store, log);
        LoadedProfiles loaded = new(profiles, defaultProfileName);
        MidiOnGameportRouter midi = new(sink: null, log);
        RumbleRouter rumble = new(sink: null, log);
        FakeJoystickEventSource events = new();
        JoystickProfileActivator activator = new(events, loader, loaded, midi, rumble, log);
        return (events, midi, rumble, activator);
    }

    [Fact]
    public void Connect_AppliesMatchingProfileSettingsToBothRouters() {
        JoystickProfile profile = new() {
            Name = "Xbox Profile",
            DeviceName = "Xbox",
            MidiOnGameport = new MidiOnGameportSettings { Enabled = true, Mpu401BasePort = 0x300 },
            Rumble = new RumbleMapping { Enabled = false, AmplitudeScale = 0.25 },
        };
        (FakeJoystickEventSource events, MidiOnGameportRouter midi,
            RumbleRouter rumble, _) = BuildActivator(new[] { profile });

        events.RaiseConnect(0, "Xbox 360 Controller");

        midi.IsEnabled.Should().BeTrue();
        midi.Mpu401BasePort.Should().Be(0x300);
        rumble.IsEnabled.Should().BeFalse();
        rumble.AmplitudeScale.Should().Be(0.25);
    }

    [Fact]
    public void Connect_FallsBackToEmbeddedDefaultWhenNoMatch() {
        (FakeJoystickEventSource events, MidiOnGameportRouter midi,
            RumbleRouter rumble, _) = BuildActivator(System.Array.Empty<JoystickProfile>());

        events.RaiseConnect(0, "Unknown Pad");

        // Embedded default disables MIDI-on-gameport and enables rumble at scale 1.0.
        midi.IsEnabled.Should().BeFalse();
        rumble.IsEnabled.Should().BeTrue();
        rumble.AmplitudeScale.Should().Be(1.0);
    }

    [Fact]
    public void Disconnect_ResetsBothRouters() {
        JoystickProfile profile = new() {
            Name = "Xbox Profile",
            DeviceName = "Xbox",
            MidiOnGameport = new MidiOnGameportSettings { Enabled = true, Mpu401BasePort = 0x300 },
            Rumble = new RumbleMapping { Enabled = true, AmplitudeScale = 0.5 },
        };
        (FakeJoystickEventSource events, MidiOnGameportRouter midi,
            RumbleRouter rumble, _) = BuildActivator(new[] { profile });
        events.RaiseConnect(0, "Xbox 360 Controller");

        events.RaiseDisconnect(0);

        midi.IsEnabled.Should().BeFalse();
        midi.Mpu401BasePort.Should().Be(MidiOnGameportRouter.DefaultMpu401BasePort);
        rumble.IsEnabled.Should().BeTrue();
        rumble.AmplitudeScale.Should().Be(1.0);
    }

    [Fact]
    public void Dispose_StopsReactingToConnectionEvents() {
        (FakeJoystickEventSource events, MidiOnGameportRouter midi,
            _, JoystickProfileActivator activator) =
            BuildActivator(System.Array.Empty<JoystickProfile>());

        activator.Dispose();
        events.RaiseConnect(0, "Whatever");

        // Embedded default would have left MIDI disabled anyway, so flip it
        // first to an enabled state by directly configuring, then verify the
        // disposed activator does not reset it on subsequent connects.
        midi.Configure(new MidiOnGameportSettings { Enabled = true });
        events.RaiseConnect(1, "Another");

        midi.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Dispose_IsIdempotent() {
        (_, _, _, JoystickProfileActivator activator) =
            BuildActivator(System.Array.Empty<JoystickProfile>());

        activator.Dispose();
        activator.Dispose();
    }

    [Fact]
    public void Connect_PrefersGuidMatchOverDeviceName() {
        JoystickProfile guidProfile = new() {
            Name = "Guid Profile",
            DeviceGuid = "030000005e040000130b000017050000",
            DeviceName = "WrongName",
            MidiOnGameport = new MidiOnGameportSettings { Enabled = true, Mpu401BasePort = 0x310 },
            Rumble = new RumbleMapping { Enabled = false, AmplitudeScale = 0.5 },
        };
        JoystickProfile nameProfile = new() {
            Name = "Name Profile",
            DeviceGuid = string.Empty,
            DeviceName = "Xbox",
            MidiOnGameport = new MidiOnGameportSettings { Enabled = true, Mpu401BasePort = 0x320 },
            Rumble = new RumbleMapping { Enabled = true, AmplitudeScale = 1.0 },
        };
        (FakeJoystickEventSource events, MidiOnGameportRouter midi,
            RumbleRouter rumble, _) = BuildActivator(new[] { guidProfile, nameProfile });

        events.RaiseConnect(0, "Xbox 360 Controller", "030000005e040000130b000017050000");

        midi.Mpu401BasePort.Should().Be(0x310);
        rumble.AmplitudeScale.Should().Be(0.5);
    }

    [Fact]
    public void Connect_FallsBackToDeviceNameWhenGuidIsEmpty() {
        JoystickProfile nameProfile = new() {
            Name = "Name Profile",
            DeviceGuid = string.Empty,
            DeviceName = "Xbox",
            MidiOnGameport = new MidiOnGameportSettings { Enabled = true, Mpu401BasePort = 0x340 },
        };
        (FakeJoystickEventSource events, MidiOnGameportRouter midi,
            _, _) = BuildActivator(new[] { nameProfile });

        events.RaiseConnect(0, "Xbox 360 Controller", string.Empty);

        midi.Mpu401BasePort.Should().Be(0x340);
    }
}
