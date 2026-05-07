namespace Spice86.Tests.Emulator.IOPorts;

using System;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using Xunit;

public class IOPortDispatcherOverrideTests {
    private const ushort TestPort = 0x1234;

    private readonly IOPortDispatcher _dispatcher;

    public IOPortDispatcherOverrideTests() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        State state = new(CpuModel.INTEL_80286);
        _dispatcher = new IOPortDispatcher(new AddressReadWriteBreakpoints(), state, logger, false);
    }

    [Fact]
    public void AddIOPortHandlerThrowsOnDuplicatePort() {
        // Arrange
        StubHandler first = new(0x11);
        StubHandler second = new(0x22);
        _dispatcher.AddIOPortHandler(TestPort, first);

        // Act
        Action act = () => _dispatcher.AddIOPortHandler(TestPort, second);

        // Assert
        act.Should().Throw<System.ArgumentException>();
    }

    [Fact]
    public void OverrideIOPortHandlerReplacesExistingHandler() {
        // Arrange
        StubHandler builtIn = new(0x11);
        StubHandler plugin = new(0x22);
        _dispatcher.AddIOPortHandler(TestPort, builtIn);

        // Act
        _dispatcher.OverrideIOPortHandler(TestPort, plugin);
        byte read = _dispatcher.ReadByte(TestPort);

        // Assert
        read.Should().Be(0x22);
    }

    [Fact]
    public void OverrideIOPortHandlerRegistersWhenNoBuiltInHandlerExists() {
        // Arrange
        StubHandler plugin = new(0x42);

        // Act
        _dispatcher.OverrideIOPortHandler(TestPort, plugin);
        byte read = _dispatcher.ReadByte(TestPort);

        // Assert
        read.Should().Be(0x42);
    }

    private sealed class StubHandler : DefaultIOPortHandler {
        private readonly byte _readValue;

        public StubHandler(byte readValue)
            : base(new State(CpuModel.INTEL_80286), false, Substitute.For<ILoggerService>()) {
            _readValue = readValue;
        }

        public override byte ReadByte(ushort port) {
            return _readValue;
        }
    }
}
