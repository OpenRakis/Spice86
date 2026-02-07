namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
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

    private sealed class PitFixture {
        public PitFixture() {
            Logger = Substitute.For<ILoggerService>();
            State = new State(CpuModel.ZET_86);
            var breakpoints = new AddressReadWriteBreakpoints();
            Dispatcher = new IOPortDispatcher(breakpoints, State, Logger, false, new NullCyclesLimiter());
            DualPic = new DualPic(Dispatcher, State, Logger, false);
            Speaker = new StubPitSpeaker();
            var emulatedClock = new EmulatedClock(new NullCyclesLimiter());
            var emulationLoopScheduler = new EmulationLoopScheduler(emulatedClock, State, Logger);
            PitTimer = new PitTimer(Dispatcher, State, DualPic, Speaker, emulationLoopScheduler, emulatedClock, Logger, false);
        }

        public ILoggerService Logger { get; }
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
