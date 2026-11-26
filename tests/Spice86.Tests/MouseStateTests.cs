namespace Spice86.Tests;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;
using Spice86.Shared.Emulator.Mouse;

using System.Text.Json;

using Xunit;

/// <summary>
/// Checks state capture and restore of the mouse device, on a clock the test sets: host input changes the
/// position, buttons and motion, a restore puts them back, and a pending sample is delivered as far after the
/// restore as it was after the capture.
/// </summary>
public sealed class MouseStateTests {
    private const byte MouseIrqVector = 0x74;
    private const int MouseIrq = 12;

    private readonly InputEventHub _hub = new();
    private readonly IEmulatedClock _clock = Substitute.For<IEmulatedClock>();
    private readonly DeviceScheduler _scheduler;
    private readonly Mouse _mouse;
    private readonly DualPic _pic;

    public MouseStateTests() {
        State state = new(CpuModel.INTEL_80286);
        IOPortDispatcher ports = new(new(), state, NullLogger.Instance, false);
        _pic = new(ports, state, NullLogger.Instance, false);
        _scheduler = new(_clock, NullLogger.Instance, "Mouse state test");
        _mouse = new(state, new SharedMouseData(), _pic, MouseType.Ps2, NullLogger.Instance, false, _clock, _scheduler, _hub);
        _mouse.SampleRate = 1;
    }

    [Fact]
    public void RestoreAfterHostInputGivesBackPositionButtonsAndMotion() {
        // Arrange
        PostMove(0.25, 0.5);
        PostButton(MouseButton.Right, true);
        MouseDeviceState captured = _mouse.CaptureState();

        // Act
        PostMove(0.9, 0.1);
        PostButton(MouseButton.Right, false);
        PostButton(MouseButton.Left, true);
        _mouse.HorizontalMickeysPerPixel = 3;
        _mouse.ResetDeltas();
        _mouse.RestoreState(captured);

        // Assert
        _mouse.MouseXRelative.Should().Be(0.25);
        _mouse.MouseYRelative.Should().Be(0.5);
        _mouse.IsRightButtonDown.Should().BeTrue();
        _mouse.IsLeftButtonDown.Should().BeFalse();
        _mouse.DeltaX.Should().Be(0.25);
        _mouse.DeltaY.Should().Be(0.5);
        _mouse.HorizontalMickeysPerPixel.Should().Be(8);
        _mouse.CaptureState().Should().Be(captured);
    }

    [Fact]
    public void PendingSampleIsDeliveredAsFarAfterTheRestoreAsItWasAfterTheCapture() {
        // Arrange: a first sample is delivered at time 0, so a second one waits until 1000 ms.
        PostMove(0.25, 0.25);
        ConsumeMouseIrq();
        PostMove(0.5, 0.5);
        MouseDeviceState captured = _mouse.CaptureState();
        _clock.ElapsedTimeMs.Returns(1000.0);
        _scheduler.ProcessEvents();
        ConsumeMouseIrq();

        // Act
        _clock.ElapsedTimeMs.Returns(5000.0);
        _mouse.RestoreState(captured);
        _clock.ElapsedTimeMs.Returns(5999.0);
        _scheduler.ProcessEvents();
        bool requestedBeforeDelay = _pic.IrqCheck;
        _clock.ElapsedTimeMs.Returns(6000.0);
        _scheduler.ProcessEvents();

        // Assert
        captured.PendingSampleDelayMs.Should().Be(1000);
        requestedBeforeDelay.Should().BeFalse();
        _pic.IrqCheck.Should().BeTrue();
        _mouse.LastTrigger.Should().Be(MouseEventMask.Movement);
        _scheduler.NextEventTime.Should().BeNull();
    }

    [Fact]
    public void RestoredLastSampleTimeKeepsTheNextSampleWaiting() {
        // Arrange: a sample is delivered at time 0 and nothing is pending.
        PostMove(0.25, 0.25);
        ConsumeMouseIrq();
        MouseDeviceState captured = _mouse.CaptureState();

        // Act: restored 3000 ms later, a move 500 ms after the restore must wait for the next sample.
        _clock.ElapsedTimeMs.Returns(3000.0);
        _mouse.RestoreState(captured);
        _clock.ElapsedTimeMs.Returns(3500.0);
        PostMove(0.75, 0.75);

        // Assert
        _pic.IrqCheck.Should().BeFalse();
        _scheduler.NextEventTime.Should().Be(4000);
    }

    [Fact]
    public void StateWithoutPendingSampleRemovesTheQueuedOne() {
        // Arrange
        MouseDeviceState idle = _mouse.CaptureState();
        PostMove(0.25, 0.25);
        ConsumeMouseIrq();
        PostMove(0.5, 0.5);
        _scheduler.NextEventTime.Should().Be(1000);

        // Act
        _mouse.RestoreState(idle);

        // Assert
        idle.LastSampleTimeMs.Should().BeNull();
        _scheduler.NextEventTime.Should().BeNull();
    }

    [Fact]
    public void StateSurvivesAJsonRoundTrip() {
        // Arrange
        PostMove(0.25, 0.25);
        ConsumeMouseIrq();
        PostMove(0.5, 0.5);
        MouseDeviceState captured = _mouse.CaptureState();

        // Act
        MouseDeviceState roundTripped = JsonSerializer.Deserialize<MouseDeviceState>(JsonSerializer.Serialize(captured));

        // Assert
        roundTripped.Should().Be(captured);
    }

    private void PostMove(double x, double y) {
        _hub.PostMouseMoveEvent(new(x, y));
        _hub.ProcessAllPendingInputEvents();
    }

    private void PostButton(MouseButton button, bool down) {
        _hub.PostMouseButtonEvent(new(button, down));
        _hub.ProcessAllPendingInputEvents();
    }

    private void ConsumeMouseIrq() {
        _pic.ComputeVectorNumber().Should().Be(MouseIrqVector);
        _pic.AcknowledgeInterrupt(MouseIrq);
    }
}
