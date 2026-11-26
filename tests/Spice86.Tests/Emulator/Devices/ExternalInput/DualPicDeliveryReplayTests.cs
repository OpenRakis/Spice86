namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Checks <see cref="InterruptDeliveryHold.Deliver" />, the delivery point observer and the delivery observer on a
/// controller pair with no machine around it, for the cases the <c>tickwait</c> replay does not reach.
/// </summary>
public sealed class DualPicDeliveryReplayTests {
    private const byte TimerVector = 0x08;
    private const byte KeyboardVector = 0x09;

    private readonly State _state;
    private readonly DualPic _pic;
    private readonly IInterruptDeliveryObserver _deliveryObserver = Substitute.For<IInterruptDeliveryObserver>();

    public DualPicDeliveryReplayTests() {
        ILogger logger = Substitute.For<ILogger>();
        _state = new State(CpuModel.INTEL_80286);
        IOPortDispatcher ioPortDispatcher = new(new AddressReadWriteBreakpoints(), _state, logger, false);
        _pic = new DualPic(ioPortDispatcher, _state, logger, false) { DeliveryObserver = _deliveryObserver };
    }

    [Fact]
    public void DeliveryWithoutHoldIsReportedWithLineVectorCyclesAndAddress() {
        // Arrange
        _state.CS = 0x1234;
        _state.IP = 0x0056;
        _state.Cycles = 789;
        _pic.ActivateIrq(0);

        // Act
        byte? vector = _pic.ComputeVectorNumber();

        // Assert
        vector.Should().Be(TimerVector);
        _deliveryObserver.Received(1).OnInterruptDelivered(0, TimerVector,
            new InterruptDeliveryPoint(789, new SegmentedAddress(0x1234, 0x0056)));
    }

    [Fact]
    public void DeliverOutsideTheObserverIsTakenAtTheNextPoint() {
        // Arrange
        using InterruptDeliveryHold hold = _pic.HoldDelivery();
        hold.Deliver(0);

        // Act
        byte? vector = _pic.ComputeVectorNumber(new SegmentedAddress(0x1000, 0x0020));

        // Assert: the address given by generated code is reported, and the line delivers once
        vector.Should().Be(TimerVector);
        _deliveryObserver.Received(1).OnInterruptDelivered(0, TimerVector,
            new InterruptDeliveryPoint(0, new SegmentedAddress(0x1000, 0x0020)));
        _pic.AcknowledgeInterrupt(0);
        _pic.ActivateIrq(0);
        _pic.ComputeVectorNumber().Should().BeNull();
        hold.HeldDeliveries.Should().ContainSingle();
    }

    [Fact]
    public void MaskedLineLetThroughWaitsUntilUnmasked() {
        // Arrange
        _pic.SetIrqMask(0, true);
        using InterruptDeliveryHold hold = _pic.HoldDelivery();
        hold.Deliver(0);

        // Act
        byte? whileMasked = _pic.ComputeVectorNumber();
        _pic.SetIrqMask(0, false);
        byte? afterUnmask = _pic.ComputeVectorNumber();

        // Assert
        whileMasked.Should().BeNull();
        afterUnmask.Should().Be(TimerVector);
    }

    [Fact]
    public void HeldHigherPriorityRequestComesBeforeALowerLineLetThrough() {
        // Arrange: IRQ 0 is latched and held, IRQ 1 is let through
        using InterruptDeliveryHold hold = _pic.HoldDelivery();
        _pic.ActivateIrq(0);
        hold.Deliver(1);

        // Act
        byte? blocked = _pic.ComputeVectorNumber();
        hold.Deliver(0);
        byte? first = _pic.ComputeVectorNumber();
        _pic.AcknowledgeInterrupt(0);
        byte? second = _pic.ComputeVectorNumber();

        // Assert: the held IRQ 0 is listed and blocks IRQ 1 until it is let through and served
        blocked.Should().BeNull();
        hold.HeldDeliveries.Select(delivery => delivery.Irq).Should().Equal((byte)0);
        first.Should().Be(TimerVector);
        second.Should().Be(KeyboardVector);
    }

    [Fact]
    public void SecondaryLineLetThroughIsReportedWithItsOwnLine() {
        // Arrange
        _pic.SetIrqMask(10, false);
        using InterruptDeliveryHold hold = _pic.HoldDelivery();
        hold.Deliver(10);

        // Act
        byte? vector = _pic.ComputeVectorNumber();

        // Assert
        vector.Should().Be(0x72);
        _deliveryObserver.Received(1).OnInterruptDelivered(10, 0x72, Arg.Any<InterruptDeliveryPoint>());
    }

    [Fact]
    public void PointObserverIsAskedWithNothingLatchedUntilItsHoldEnds() {
        // Arrange
        IInterruptDeliveryPointObserver pointObserver = Substitute.For<IInterruptDeliveryPointObserver>();
        InterruptDeliveryHold hold = _pic.HoldDelivery(pointObserver);

        // Act
        byte? whileHeld = _pic.ComputeVectorNumber(new SegmentedAddress(0x1000, 0x0020));
        hold.Dispose();
        byte? afterRelease = _pic.ComputeVectorNumber(new SegmentedAddress(0x1000, 0x0020));

        // Assert
        whileHeld.Should().BeNull();
        afterRelease.Should().BeNull();
        pointObserver.Received(1).OnDeliveryPoint(hold,
            new InterruptDeliveryPoint(0, new SegmentedAddress(0x1000, 0x0020)));
    }

    [Fact]
    public void LineLetThroughAndNotDeliveredIsForgottenWhenTheLastHoldEnds() {
        // Arrange: IRQ 1 is let through while masked, and the hold ends before it is unmasked
        _pic.SetIrqMask(1, true);
        InterruptDeliveryHold hold = _pic.HoldDelivery();
        hold.Deliver(1);
        hold.Dispose();
        using InterruptDeliveryHold nextHold = _pic.HoldDelivery();
        _pic.SetIrqMask(1, false);

        // Act
        byte? vector = _pic.ComputeVectorNumber();

        // Assert: the next hold holds the request IRQ 1 still has latched
        vector.Should().BeNull();
        nextHold.HeldDeliveries.Should().ContainSingle();
    }

    [Fact]
    public void DeliverOnAReleasedHoldThrows() {
        // Arrange
        InterruptDeliveryHold hold = _pic.HoldDelivery();
        hold.Dispose();

        // Act
        Action deliver = () => hold.Deliver(0);

        // Assert
        deliver.Should().Throw<ObjectDisposedException>();
    }
}
