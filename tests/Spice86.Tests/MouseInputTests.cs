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

using Xunit;

public sealed class MouseInputTests {
    private readonly InputEventHub _hub = new();
    private readonly IEmulatedClock _clock = Substitute.For<IEmulatedClock>();
    private readonly DeviceScheduler _scheduler;
    private readonly Mouse _mouse;
    private readonly DualPic _pic;

    public MouseInputTests() {
        State state = new(CpuModel.INTEL_80286);
        IOPortDispatcher ports = new(new(), state, NullLogger.Instance, false);
        _pic = new(ports, state, NullLogger.Instance, false);
        _scheduler = new(_clock, NullLogger.Instance, "Mouse input test");
        _mouse = new(state, new SharedMouseData(), _pic, MouseType.Ps2, NullLogger.Instance, false, _clock, _scheduler, _hub);
        _mouse.SampleRate = 1;
    }

    [Fact]
    public void FinalBatchedMoveIsDeliveredWithoutAnotherHostEvent() {
        // Arrange
        PostMove(0.25, 0.25);
        ConsumeMouseIrq();
        PostMove(0.75, 0.5);

        // Act
        _clock.ElapsedTimeMs.Returns(1000.0);
        _scheduler.ProcessEvents();

        // Assert
        _mouse.DeltaX.Should().Be(0.75);
        _mouse.DeltaY.Should().Be(0.5);
        _mouse.LastTrigger.Should().Be(MouseEventMask.Movement);
        _pic.IrqCheck.Should().BeTrue();
        _scheduler.NextEventTime.Should().BeNull();
    }

    [Fact]
    public void BatchedPressAndReleasePreserveBothTransitionMasks() {
        // Arrange
        PostMove(0.25, 0.25);
        ConsumeMouseIrq();
        _hub.PostMouseButtonEvent(new(MouseButton.Left, true));
        _hub.PostMouseButtonEvent(new(MouseButton.Left, false));
        _hub.ProcessAllPendingInputEvents();

        // Act
        _clock.ElapsedTimeMs.Returns(1000.0);
        _scheduler.ProcessEvents();

        // Assert
        _mouse.LastTrigger.Should().Be(MouseEventMask.LeftButtonDown | MouseEventMask.LeftButtonUp);
        _mouse.IsLeftButtonDown.Should().BeFalse();
        _pic.IrqCheck.Should().BeTrue();
    }

    [Fact]
    public void DeferredMouseDeliveryWaitsUntilSampleDeadline() {
        // Arrange
        PostMove(0.25, 0.25);
        ConsumeMouseIrq();
        PostMove(0.5, 0.5);
        _clock.ElapsedTimeMs.Returns(999.0);

        // Act
        _scheduler.ProcessEvents();

        // Assert
        _pic.IrqCheck.Should().BeFalse();
        _scheduler.NextEventTime.Should().Be(1000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidSampleRateIsRejected(int sampleRate) {
        // Arrange
        Action setRate = () => _mouse.SampleRate = sampleRate;

        // Act / Assert
        setRate.Should().Throw<ArgumentOutOfRangeException>();
        _mouse.SampleRate.Should().Be(1);
    }

    private void PostMove(double x, double y) {
        _hub.PostMouseMoveEvent(new(x, y));
        _hub.ProcessAllPendingInputEvents();
    }

    private void ConsumeMouseIrq() {
        _pic.ComputeVectorNumber().Should().Be(0x74);
        _pic.AcknowledgeInterrupt(12);
    }
}
