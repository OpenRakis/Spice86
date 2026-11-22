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
/// Checks <see cref="DualPic.HoldDelivery" /> on a controller pair with no machine around it.
/// </summary>
public sealed class DualPicDeliveryHoldTests {
    private const byte TimerVector = 0x08;
    private const byte KeyboardVector = 0x09;

    private readonly State _state;
    private readonly DualPic _pic;

    public DualPicDeliveryHoldTests() {
        ILogger logger = Substitute.For<ILogger>();
        _state = new State(CpuModel.INTEL_80286);
        IOPortDispatcher ioPortDispatcher = new(new AddressReadWriteBreakpoints(), _state, logger, false);
        _pic = new DualPic(ioPortDispatcher, _state, logger, false);
    }

    [Fact]
    public void DeliveryIsNotHeldByDefault() {
        // Act
        _pic.ActivateIrq(0);
        byte? vector = _pic.ComputeVectorNumber();

        // Assert
        _pic.IsDeliveryHeld.Should().BeFalse();
        vector.Should().Be(TimerVector);
    }

    [Fact]
    public void HeldRequestIsNotDeliveredAndStaysLatched() {
        // Arrange
        PicSnapshot before = _pic.GetPicSnapshot(DualPic.PicController.Primary);
        using InterruptDeliveryHold hold = _pic.HoldDelivery();
        _pic.ActivateIrq(0);

        // Act
        byte? vector = _pic.ComputeVectorNumber();

        // Assert: nothing was delivered, the request is still latched and nothing went in service
        vector.Should().BeNull();
        hold.IsReleased.Should().BeFalse();
        _pic.IsDeliveryHeld.Should().BeTrue();
        _pic.IrqCheck.Should().BeTrue();
        PicSnapshot after = _pic.GetPicSnapshot(DualPic.PicController.Primary);
        after.Should().Be(before with { InterruptRequestRegister = 0x01 });
    }

    [Fact]
    public void HeldRequestIsListedWithLineVectorCyclesAndAddress() {
        // Arrange
        _state.CS = 0x1234;
        _state.IP = 0x0056;
        _state.Cycles = 789;
        using InterruptDeliveryHold hold = _pic.HoldDelivery();
        _pic.ActivateIrq(0);

        // Act
        _pic.ComputeVectorNumber();

        // Assert
        hold.HeldDeliveries.Should().Equal(
            new HeldInterruptDelivery(0, TimerVector, 789, new SegmentedAddress(0x1234, 0x0056)));
    }

    [Fact]
    public void RequestIsListedOnceWhileItStaysLatched() {
        // Arrange
        using InterruptDeliveryHold hold = _pic.HoldDelivery();
        _pic.ActivateIrq(0);

        // Act: the CPU asks once per instruction
        for (int i = 0; i < 5; i++) {
            _state.Cycles++;
            _pic.ComputeVectorNumber();
        }

        // Assert: one entry, from the first time the CPU asked
        hold.HeldDeliveries.Should().ContainSingle();
        hold.HeldDeliveries[0].Cycles.Should().Be(1);
    }

    [Fact]
    public void LineLoweredAndRaisedAgainIsListedAgain() {
        // Arrange
        using InterruptDeliveryHold hold = _pic.HoldDelivery();
        _pic.ActivateIrq(0);
        _pic.ComputeVectorNumber();
        _pic.DeactivateIrq(0);
        _pic.ComputeVectorNumber();

        // Act
        _pic.ActivateIrq(0);
        _pic.ComputeVectorNumber();

        // Assert
        hold.HeldDeliveries.Should().HaveCount(2);
    }

    [Fact]
    public void NothingIsListedWhenNoRequestIsPending() {
        // Arrange
        using InterruptDeliveryHold hold = _pic.HoldDelivery();

        // Act
        byte? vector = _pic.ComputeVectorNumber();

        // Assert
        vector.Should().BeNull();
        hold.HeldDeliveries.Should().BeEmpty();
    }

    [Fact]
    public void MaskedRequestIsNotListed() {
        // Arrange
        using InterruptDeliveryHold hold = _pic.HoldDelivery();
        _pic.SetIrqMask(0, true);
        _pic.ActivateIrq(0);

        // Act
        _pic.ComputeVectorNumber();

        // Assert
        hold.HeldDeliveries.Should().BeEmpty();
    }

    [Fact]
    public void SecondaryControllerRequestIsListedWithItsOwnLine() {
        // Arrange
        _pic.SetIrqMask(10, false);
        using InterruptDeliveryHold hold = _pic.HoldDelivery();
        _pic.ActivateIrq(10);

        // Act
        _pic.ComputeVectorNumber();

        // Assert
        hold.HeldDeliveries.Should().ContainSingle();
        hold.HeldDeliveries[0].Irq.Should().Be(10);
        hold.HeldDeliveries[0].VectorNumber.Should().Be(0x72);
    }

    [Fact]
    public void HigherPriorityRequestRaisedLaterIsListedAfterTheFirst() {
        // Arrange
        using InterruptDeliveryHold hold = _pic.HoldDelivery();
        _pic.ActivateIrq(1);
        _pic.ComputeVectorNumber();

        // Act
        _pic.ActivateIrq(0);
        _pic.ComputeVectorNumber();

        // Assert
        hold.HeldDeliveries.Select(delivery => delivery.VectorNumber).Should().Equal(KeyboardVector, TimerVector);
    }

    [Fact]
    public void HeldRequestIsDeliveredAfterRelease() {
        // Arrange
        using InterruptDeliveryHold hold = _pic.HoldDelivery();
        _pic.ActivateIrq(0);
        _pic.ComputeVectorNumber();

        // Act
        hold.Dispose();
        byte? vector = _pic.ComputeVectorNumber();

        // Assert
        hold.IsReleased.Should().BeTrue();
        _pic.IsDeliveryHeld.Should().BeFalse();
        vector.Should().Be(TimerVector);
        hold.HeldDeliveries.Should().ContainSingle();
    }

    [Fact]
    public void DeliveryStaysHeldUntilTheLastHoldIsReleased() {
        // Arrange
        using InterruptDeliveryHold outer = _pic.HoldDelivery();
        using InterruptDeliveryHold inner = _pic.HoldDelivery();
        _pic.ActivateIrq(0);

        // Act
        inner.Dispose();
        inner.Dispose();
        byte? heldVector = _pic.ComputeVectorNumber();
        outer.Dispose();
        byte? deliveredVector = _pic.ComputeVectorNumber();

        // Assert: releasing the inner hold twice did not release the outer one
        heldVector.Should().BeNull();
        deliveredVector.Should().Be(TimerVector);
        outer.HeldDeliveries.Should().ContainSingle();
        inner.HeldDeliveries.Should().BeEmpty();
    }

    [Fact]
    public void HoldCreatedWhileAnOuterHoldListedTheRequestListsItToo() {
        // Arrange: the outer hold has listed a request that stays latched
        using InterruptDeliveryHold outer = _pic.HoldDelivery();
        _pic.ActivateIrq(0);
        _state.Cycles = 10;
        _pic.ComputeVectorNumber();

        // Act
        using InterruptDeliveryHold inner = _pic.HoldDelivery();
        _state.Cycles = 20;
        _pic.ComputeVectorNumber();

        // Assert: each hold lists the request once, from the first time the CPU asked under it
        outer.HeldDeliveries.Should().ContainSingle().Which.Cycles.Should().Be(10);
        inner.HeldDeliveries.Should().ContainSingle().Which.Cycles.Should().Be(20);
    }

    [Fact]
    public void EntriesPastTheLimitAreCounted() {
        // Arrange
        using InterruptDeliveryHold hold = _pic.HoldDelivery();

        // Act: each round lowers the line, lets the controller see it, and raises it again
        for (int i = 0; i < InterruptDeliveryHold.MaxRecordedDeliveries + 3; i++) {
            _pic.ActivateIrq(0);
            _pic.ComputeVectorNumber();
            _pic.DeactivateIrq(0);
            _pic.ComputeVectorNumber();
        }

        // Assert
        hold.HeldDeliveries.Should().HaveCount(InterruptDeliveryHold.MaxRecordedDeliveries);
        hold.DroppedDeliveryCount.Should().Be(3);
    }
}
