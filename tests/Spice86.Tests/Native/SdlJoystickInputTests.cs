namespace Spice86.Tests.Native;

using FluentAssertions;

using Spice86.Native;
using Spice86.Shared.Emulator.Input.Joystick;

using System.Collections.Generic;

using Xunit;

/// <summary>
/// Smoke tests for the SDL joystick adapter. The CI environment has no real
/// controllers attached so we cannot validate event fan-out end-to-end, but
/// we do verify the adapter survives Init/Poll/Rescan/Dispose with no devices
/// present, never raises spurious events, and that Dispose is idempotent.
/// </summary>
public sealed class SdlJoystickInputTests {
    [Fact]
    public void Poll_WhenUninitialized_DoesNothing() {
        using SdlJoystickInput input = new SdlJoystickInput();
        List<JoystickAxisEventArgs> axes = new();
        input.JoystickAxisChanged += (_, e) => axes.Add(e);

        input.Poll();
        input.RescanDevices();

        axes.Should().BeEmpty();
        input.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public void Dispose_IsIdempotent() {
        SdlJoystickInput input = new SdlJoystickInput();
        input.Dispose();
        input.Dispose();
    }

    [Fact]
    public void TryInitialize_WithoutAttachedDevices_DoesNotRaiseSpuriousAxisEvents() {
        using SdlJoystickInput input = new SdlJoystickInput();
        List<JoystickAxisEventArgs> axes = new();
        List<JoystickButtonEventArgs> buttons = new();
        List<JoystickHatEventArgs> hats = new();
        input.JoystickAxisChanged += (_, e) => axes.Add(e);
        input.JoystickButtonChanged += (_, e) => buttons.Add(e);
        input.JoystickHatChanged += (_, e) => hats.Add(e);

        // The CI runner has no joystick attached. TryInitialize may return
        // false if SDL is unavailable, or true if SDL is available but no
        // devices are present. Either way no axis/button/hat events should
        // fire purely from initialization and a poll cycle.
        if (input.TryInitialize()) {
            input.Poll();
            input.RescanDevices();
            input.Poll();
        }

        axes.Should().BeEmpty();
        buttons.Should().BeEmpty();
        hats.Should().BeEmpty();
    }
}
