namespace Spice86.Tests;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using Xunit;

public class IOPortDispatcherTests {
    [Fact]
    public void UnhandledDWordReadShouldFloatHigh() {
        // Arrange
        IOPortDispatcher dispatcher = CreateDispatcher(false);

        // Act
        uint value = dispatcher.ReadDWord(0x1F);

        // Assert
        value.Should().Be(uint.MaxValue);
    }

    private static IOPortDispatcher CreateDispatcher(bool failOnUnhandledPort) {
        State state = new(CpuModel.INTEL_80386);
        ILoggerService logger = Substitute.For<ILoggerService>();
        return new IOPortDispatcher(new AddressReadWriteBreakpoints(), state, logger, failOnUnhandledPort);
    }
}