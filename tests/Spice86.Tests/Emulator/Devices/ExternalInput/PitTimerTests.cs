namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Interfaces;

using Xunit;

public sealed class PitTimerTests : IDisposable {
    private readonly PitFixture _fixture = new();

    public void Dispose() {
        _fixture.Dispose();
    }

    [Fact]
    public void Channel0ProgrammingUpdatesSnapshot() {
        // Mode 2 with low/high byte writes.
        _fixture.IoSystem.Write(0x43, 0x34);
        _fixture.IoSystem.Write(0x40, 0x05);
        _fixture.IoSystem.Write(0x40, 0x00);

        PitChannelSnapshot snapshot = _fixture.PitTimer.GetChannelSnapshot(0);
        snapshot.Count.Should().Be(5);
        snapshot.Mode.Should().Be(PitMode.RateGenerator);
    }

    [Fact]
    public void Channel2ProgrammingNotifiesSpeaker() {
        _fixture.IoSystem.Write(0x43, 0xB6);

        _fixture.Speaker.LastControlMode.Should().Be(PitMode.SquareWave);
        _fixture.Speaker.ControlInvocationCount.Should().Be(1);
        _fixture.Speaker.CounterInvocationCount.Should().Be(1);
        _fixture.Speaker.LastCounterMode.Should().Be(PitMode.SquareWave);
        _fixture.Speaker.LastCount.Should().Be(0);

        _fixture.IoSystem.Write(0x42, 0x34);
        _fixture.Speaker.CounterInvocationCount.Should().Be(1);
        _fixture.IoSystem.Write(0x42, 0x12);

        _fixture.Speaker.LastCount.Should().Be(0x1234);
        _fixture.Speaker.LastCounterMode.Should().Be(PitMode.SquareWave);
        _fixture.Speaker.CounterInvocationCount.Should().Be(2);
    }

    private sealed class PitFixture : IDisposable {
        public PitFixture() {
            Logger = Substitute.For<ILoggerService>();
            State = new State(CpuModel.ZET_86);
            CpuState = new PicPitCpuState(State) {
                InterruptFlag = true,
                CyclesMax = 256,
                CyclesLeft = 256
            };
            var breakpoints = new AddressReadWriteBreakpoints();
            Dispatcher = new IOPortDispatcher(breakpoints, State, Logger, false);
            IoSystem = new IoSystem(Dispatcher, State, Logger, false);
            DualPic = new DualPic(IoSystem, CpuState, Logger);
            Speaker = new StubPitSpeaker();
            PitTimer = new PitTimer(IoSystem, DualPic, Speaker, Logger);
        }

        public ILoggerService Logger { get; }
        public State State { get; }
        public PicPitCpuState CpuState { get; }
        public IOPortDispatcher Dispatcher { get; }
        public IoSystem IoSystem { get; }
        public DualPic DualPic { get; }
        public StubPitSpeaker Speaker { get; }
        public PitTimer PitTimer { get; }

        public void Dispose() {
            PitTimer.Dispose();
            DualPic.Dispose();
            IoSystem.Reset();
        }
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