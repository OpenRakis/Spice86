namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using Xunit;

public sealed class PicEventQueueTests {
    [Fact]
    public void RunQueueDoesNotProcessWhenNoCyclesRemain() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        var state = new State(CpuModel.INTEL_80286);
        var cpuState = new PicPitCpuState(state) {
            CyclesMax = 128,
            CyclesLeft = 0
        };
        var queue = new PicEventQueue(cpuState, logger);

        bool invoked = false;
        queue.AddEvent(_ => invoked = true, 0.25);

        bool result = queue.RunQueue();

        invoked.Should().BeFalse();
        result.Should().BeFalse();
    }
}