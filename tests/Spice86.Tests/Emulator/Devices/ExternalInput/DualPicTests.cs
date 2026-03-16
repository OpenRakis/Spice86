namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using Xunit;

public sealed class DualPicTests {
    private readonly DualPicFixture _fixture = new();

    [Fact]
    public void ActivateIrq0DeliversExpectedVector() {
        _fixture.DualPic.ActivateIrq(0);
        byte? vector = _fixture.DualPic.ComputeVectorNumber();

        vector.Should().Be(0x08);
    }

    [Fact]
    public void MaskedIrqDoesNotDeliverVector() {
        _fixture.DualPic.SetIrqMask(0, true);
        _fixture.DualPic.ActivateIrq(0);
        byte? vector = _fixture.DualPic.ComputeVectorNumber();

        vector.Should().BeNull();
        _fixture.DualPic.IrqCheck.Should().BeFalse();
    }

    [Fact]
    public void CascadeIrqRoutesThroughSecondaryController() {
        _fixture.DualPic.SetIrqMask(10, false);

        _fixture.DualPic.ActivateIrq(10);
        byte? vector = _fixture.DualPic.ComputeVectorNumber();

        vector.Should().Be(0x72);

        // Acknowledge the IRQ and ensure the in-service bit clears on the secondary channel.
        _fixture.DualPic.AcknowledgeInterrupt(10);
        PicSnapshot snapshot = _fixture.DualPic.GetPicSnapshot(DualPic.PicController.Secondary);
        snapshot.InServiceRegister.Should().Be(0);
    }

    private sealed class DualPicFixture {
        public DualPicFixture() {
            Logger = Substitute.For<ILoggerService>();
            State = new State(CpuModel.ZET_86);
            var breakpoints = new AddressReadWriteBreakpoints();
            Dispatcher = new IOPortDispatcher(breakpoints, State, Logger, false);
            DualPic = new DualPic(Dispatcher, State, Logger, false);
        }

        public ILoggerService Logger { get; }
        public State State { get; }
        public IOPortDispatcher Dispatcher { get; }
        public DualPic DualPic { get; }
    }
}