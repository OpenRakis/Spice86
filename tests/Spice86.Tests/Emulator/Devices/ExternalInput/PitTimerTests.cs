namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;
using Spice86.Shared.Interfaces;

using Xunit;

public sealed class PitTimerTests {
    private readonly PitFixture _fixture = new();

    [Fact]
    public void Channel0ProgrammingUpdatesSnapshot() {
        // Mode 2 with low/high byte writes.
        _fixture.Dispatcher.WriteByte(0x43, 0x34);
        _fixture.Dispatcher.WriteByte(0x40, 0x05);
        _fixture.Dispatcher.WriteByte(0x40, 0x00);

        PitChannelSnapshot snapshot = _fixture.PitTimer.GetChannelSnapshot(0);
        snapshot.Count.Should().Be(5);
        snapshot.Mode.Should().Be(PitMode.RateGenerator);
    }

    [Fact]
    public void Channel2ProgrammingNotifiesSpeaker() {
        _fixture.Dispatcher.WriteByte(0x43, 0xB6);

        _fixture.Speaker.LastControlMode.Should().Be(PitMode.SquareWave);
        _fixture.Speaker.ControlInvocationCount.Should().Be(1);
        _fixture.Speaker.CounterInvocationCount.Should().Be(1);
        _fixture.Speaker.LastCounterMode.Should().Be(PitMode.SquareWave);
        _fixture.Speaker.LastCount.Should().Be(0);

        _fixture.Dispatcher.WriteByte(0x42, 0x34);
        _fixture.Speaker.CounterInvocationCount.Should().Be(1);
        _fixture.Dispatcher.WriteByte(0x42, 0x12);

        _fixture.Speaker.LastCount.Should().Be(0x1234);
        _fixture.Speaker.LastCounterMode.Should().Be(PitMode.SquareWave);
        _fixture.Speaker.CounterInvocationCount.Should().Be(2);
    }

    [Theory]
    [InlineData(0, 0x40, 0x34, 0x0005)]
    [InlineData(0, 0x40, 0x34, 0x0000)]
    [InlineData(2, 0x42, 0xB6, 0x1234)]
    public void ProgramChannelMatchesThePortSequence(int channel, ushort dataPort, byte controlWord,
        ushort divisor) {
        // Arrange: two independent timers on the same frozen clock, one programmed through its
        // ports, one through the operation.
        IEmulatedClock frozen = Substitute.For<IEmulatedClock>();
        PitFixture viaPorts = new(frozen);
        PitFixture viaOperation = new(frozen);

        // Act
        viaPorts.Dispatcher.WriteByte(0x43, controlWord);
        viaPorts.Dispatcher.WriteByte(dataPort, (byte)divisor);
        viaPorts.Dispatcher.WriteByte(dataPort, (byte)(divisor >> 8));
        viaOperation.PitTimer.ProgramChannel(channel, controlWord, divisor);

        // Assert: the channel snapshot matches, and a following latch read goes through the same
        // staged state in both.
        viaOperation.PitTimer.GetChannelSnapshot(channel)
            .Should().BeEquivalentTo(viaPorts.PitTimer.GetChannelSnapshot(channel));
        viaOperation.Dispatcher.ReadByte(dataPort).Should().Be(viaPorts.Dispatcher.ReadByte(dataPort));
        viaOperation.Dispatcher.ReadByte(dataPort).Should().Be(viaPorts.Dispatcher.ReadByte(dataPort));
    }

    [Fact]
    public void ProgramChannelRefusesAMismatchedControlWordWithoutMutating() {
        Action mismatchedChannel = () => _fixture.PitTimer.ProgramChannel(2, 0x34, 0x0005);
        mismatchedChannel.Should().Throw<ArgumentException>();
        Action latchOnlyAccess = () => _fixture.PitTimer.ProgramChannel(0, 0x14, 0x0005);
        latchOnlyAccess.Should().Throw<ArgumentException>();
        Action unsupportedChannel = () => _fixture.PitTimer.ProgramChannel(1, 0x74, 0x0005);
        unsupportedChannel.Should().Throw<ArgumentOutOfRangeException>();

        // Assert: the refused calls wrote no control word - the channels keep the state a fresh
        // timer has.
        PitFixture fresh = new(Substitute.For<IEmulatedClock>());
        _fixture.PitTimer.GetChannelSnapshot(0).Should().BeEquivalentTo(fresh.PitTimer.GetChannelSnapshot(0));
        _fixture.PitTimer.GetChannelSnapshot(2).Should().BeEquivalentTo(fresh.PitTimer.GetChannelSnapshot(2));
    }

    private sealed class PitFixture {
        public PitFixture() : this(new EmulatedClock(null, DateTimeOffset.UnixEpoch)) {
        }

        public PitFixture(IEmulatedClock clock) {
            Logger = Substitute.For<ILogger>();
            State = new State(CpuModel.ZET_86);
            var breakpoints = new AddressReadWriteBreakpoints();
            Dispatcher = new IOPortDispatcher(breakpoints, State, Logger, false);
            DualPic = new DualPic(Dispatcher, State, Logger, false);
            Speaker = new StubPitSpeaker();
            DeviceScheduler emulationLoopScheduler = new(clock, Logger, "Emulation loop");
            PitTimer = new PitTimer(Dispatcher, State, DualPic, Speaker, emulationLoopScheduler, clock, Logger, false);
        }

        public ILogger Logger { get; }
        public State State { get; }
        public IOPortDispatcher Dispatcher { get; }
        public DualPic DualPic { get; }
        public StubPitSpeaker Speaker { get; }
        public PitTimer PitTimer { get; }
    }

    internal sealed class StubPitSpeaker : IPitSpeaker {
        public int CounterInvocationCount { get; private set; }
        public PitMode LastCounterMode { get; private set; } = PitMode.Inactive;
        public int LastCount { get; private set; }
        public PitMode LastControlMode { get; private set; } = PitMode.Inactive;
        public int ControlInvocationCount { get; private set; }

        public void SetCounter(int count, PitMode mode) {
            CounterInvocationCount++;
            LastCount = count;
            LastCounterMode = mode;
        }

        public void SetPitControl(PitMode mode) {
            ControlInvocationCount++;
            LastControlMode = mode;
        }
    }
}
