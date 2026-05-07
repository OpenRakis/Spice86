namespace Spice86.Tests.Emulator.Devices.Input.Joystick.Keyboard;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.Input.Joystick.Keyboard;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;

using Xunit;

/// <summary>
/// End-to-end tests: physical key pressed on the hub turns into
/// joystick events posted back through the same hub. Each test
/// uses two <see cref="InputEventHub.ProcessAllPendingInputEvents"/>
/// pumps -- one to fire the mapper's KeyDown/KeyUp handler, a
/// second to flush the joystick event the mapper just posted back
/// into the queue.
/// </summary>
public sealed class KeyboardJoystickMapperTests {
    private readonly ILoggerService _logger = Substitute.For<ILoggerService>();
    private readonly InputEventHub _hub;
    private readonly KeyboardJoystickMapper _mapper;
    private readonly List<JoystickAxisEventArgs> _axes = new();
    private readonly List<JoystickButtonEventArgs> _buttons = new();

    public KeyboardJoystickMapperTests() {
        _hub = new InputEventHub(keyboardEvents: null, mouseEvents: null,
            joystickEvents: null);
        _mapper = new KeyboardJoystickMapper(_hub, _logger);
        _hub.JoystickAxisChanged += (_, e) => _axes.Add(e);
        _hub.JoystickButtonChanged += (_, e) => _buttons.Add(e);
    }

    private void PressKey(PhysicalKey key) {
        _hub.PostKeyboardEvent(new KeyboardEventArgs(key, true));
        Pump();
    }

    private void ReleaseKey(PhysicalKey key) {
        _hub.PostKeyboardEvent(new KeyboardEventArgs(key, false));
        Pump();
    }

    private void Pump() {
        // First pump fires KeyDown/KeyUp -> mapper enqueues
        // joystick events. Second pump drains them to listeners.
        _hub.ProcessAllPendingInputEvents();
        _hub.ProcessAllPendingInputEvents();
    }

    [Fact]
    public void Default_Disabled_KeyDown_PostsNothing() {
        PressKey(PhysicalKey.ArrowRight);
        PressKey(PhysicalKey.Space);

        _axes.Should().BeEmpty();
        _buttons.Should().BeEmpty();
    }

    [Fact]
    public void Enabled_ArrowRight_PostsPositiveXAxis() {
        _mapper.Enabled = true;
        PressKey(PhysicalKey.ArrowRight);

        _axes.Should().ContainSingle();
        _axes[0].Should().Be(new JoystickAxisEventArgs(0, JoystickAxis.X, +1f));
    }

    [Fact]
    public void Enabled_ArrowLeft_PostsNegativeXAxis() {
        _mapper.Enabled = true;
        PressKey(PhysicalKey.ArrowLeft);

        _axes.Should().ContainSingle();
        _axes[0].Should().Be(new JoystickAxisEventArgs(0, JoystickAxis.X, -1f));
    }

    [Fact]
    public void Enabled_ArrowRightThenRelease_RecentresXAxis() {
        _mapper.Enabled = true;
        PressKey(PhysicalKey.ArrowRight);
        ReleaseKey(PhysicalKey.ArrowRight);

        _axes.Should().HaveCount(2);
        _axes[0].Value.Should().Be(+1f);
        _axes[1].Value.Should().Be(0f);
    }

    [Fact]
    public void Enabled_BothHorizontalKeysPressed_AxisCancelsToZero() {
        _mapper.Enabled = true;
        PressKey(PhysicalKey.ArrowLeft);
        PressKey(PhysicalKey.ArrowRight);

        _axes.Should().HaveCount(2);
        _axes[0].Value.Should().Be(-1f);
        _axes[1].Value.Should().Be(0f);
    }

    [Fact]
    public void Enabled_VerticalKeysAreIndependentOfHorizontal() {
        _mapper.Enabled = true;
        PressKey(PhysicalKey.ArrowRight);
        PressKey(PhysicalKey.ArrowDown);

        _axes.Should().HaveCount(2);
        _axes[0].Should().Be(new JoystickAxisEventArgs(0, JoystickAxis.X, +1f));
        _axes[1].Should().Be(new JoystickAxisEventArgs(0, JoystickAxis.Y, +1f));
    }

    [Fact]
    public void Enabled_Space_PostsButtonZeroPressedThenReleased() {
        _mapper.Enabled = true;
        PressKey(PhysicalKey.Space);
        ReleaseKey(PhysicalKey.Space);

        _buttons.Should().HaveCount(2);
        _buttons[0].Should().Be(new JoystickButtonEventArgs(0, 0, true));
        _buttons[1].Should().Be(new JoystickButtonEventArgs(0, 0, false));
    }

    [Fact]
    public void Enabled_Enter_PostsButtonOne() {
        _mapper.Enabled = true;
        PressKey(PhysicalKey.Enter);

        _buttons.Should().ContainSingle();
        _buttons[0].Should().Be(new JoystickButtonEventArgs(0, 1, true));
    }

    [Fact]
    public void Enabled_UnmappedKey_PostsNothing() {
        _mapper.Enabled = true;
        PressKey(PhysicalKey.F12);

        _axes.Should().BeEmpty();
        _buttons.Should().BeEmpty();
    }

    [Fact]
    public void Enabled_DuplicateAxisValueNotRePosted() {
        _mapper.Enabled = true;
        PressKey(PhysicalKey.ArrowRight);
        PressKey(PhysicalKey.ArrowRight);

        _axes.Should().ContainSingle(
            because: "the axis value did not change between the two key downs");
    }

    [Fact]
    public void Enabled_StickIndexHonoured() {
        _mapper.Enabled = true;
        _mapper.StickIndex = 1;
        PressKey(PhysicalKey.ArrowRight);
        PressKey(PhysicalKey.Space);

        _axes[0].StickIndex.Should().Be(1);
        _buttons[0].StickIndex.Should().Be(1);
    }

    [Fact]
    public void Disable_WithAxisOffCentre_RecentresAxisToZero() {
        _mapper.Enabled = true;
        PressKey(PhysicalKey.ArrowRight);
        _axes.Clear();

        _mapper.Enabled = false;
        Pump();

        _axes.Should().ContainSingle();
        _axes[0].Should().Be(new JoystickAxisEventArgs(0, JoystickAxis.X, 0f));
    }

    [Fact]
    public void DisableWhileCentred_DoesNotPostExtraEvents() {
        _mapper.Enabled = true;
        _mapper.Enabled = false;

        Pump();

        _axes.Should().BeEmpty();
    }

    [Fact]
    public void SetBindings_ReplacesDefaults() {
        Dictionary<PhysicalKey, KeyboardJoystickBinding> custom = new() {
            [PhysicalKey.A] = KeyboardJoystickBinding.ForAxis(JoystickAxis.X, -1),
            [PhysicalKey.D] = KeyboardJoystickBinding.ForAxis(JoystickAxis.X, +1),
            [PhysicalKey.J] = KeyboardJoystickBinding.ForButton(0),
        };
        _mapper.SetBindings(custom);
        _mapper.Enabled = true;

        PressKey(PhysicalKey.ArrowRight);
        PressKey(PhysicalKey.D);
        PressKey(PhysicalKey.J);

        _axes.Should().ContainSingle(
            because: "ArrowRight is no longer bound after SetBindings");
        _axes[0].Should().Be(new JoystickAxisEventArgs(0, JoystickAxis.X, +1f));
        _buttons.Should().ContainSingle();
        _buttons[0].Should().Be(new JoystickButtonEventArgs(0, 0, true));
    }

    [Fact]
    public void Dispose_StopsReactingToFurtherKeyEvents() {
        _mapper.Enabled = true;
        _mapper.Dispose();

        PressKey(PhysicalKey.ArrowRight);
        PressKey(PhysicalKey.Space);

        _axes.Should().BeEmpty();
        _buttons.Should().BeEmpty();
    }

    [Fact]
    public void Dispose_IsIdempotent() {
        _mapper.Dispose();
        _mapper.Dispose();
    }
}
