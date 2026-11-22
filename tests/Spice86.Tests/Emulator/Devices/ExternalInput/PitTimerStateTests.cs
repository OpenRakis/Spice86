namespace Spice86.Tests.Emulator.Devices.ExternalInput;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;

using Xunit;

/// <summary>
/// Checks <see cref="PitTimer.CaptureState" /> and <see cref="PitTimer.RestoreState" /> on a timer whose
/// clock advances one millisecond per CPU cycle, so the test sets the time by setting the cycle count.
/// </summary>
public sealed class PitTimerStateTests {
    private const ushort Channel0Port = 0x40;
    private const ushort Channel2Port = 0x42;
    private const ushort ControlPort = 0x43;

    private const byte Channel0RateGenerator = 0x34;
    private const byte Channel0SquareWave = 0x36;
    private const byte Channel0InterruptOnTerminalCount = 0x30;
    private const byte Channel2SquareWave = 0xB6;
    private const byte LatchChannel0Counter = 0x00;
    private const byte LatchChannel0Status = 0xE2;

    // 11932 PIT ticks last 10.00015 ms.
    private const ushort TenMillisecondDivisor = 11932;
    private const double TenMillisecondDelay = 1000.0 * TenMillisecondDivisor / PitTimer.PitTickRate;
    private const double TimeTolerance = 1e-6;

    private readonly State _state;
    private readonly IOPortDispatcher _ioPortDispatcher;
    private readonly DualPic _pic;
    private readonly DeviceScheduler _scheduler;
    private readonly PitTimer _pit;

    public PitTimerStateTests() {
        ILogger logger = Substitute.For<ILogger>();
        _state = new State(CpuModel.INTEL_80286);
        CyclesClock clock = new(_state, 1000, null, DateTimeOffset.UnixEpoch);
        _ioPortDispatcher = new IOPortDispatcher(new AddressReadWriteBreakpoints(), _state, logger, false);
        _pic = new DualPic(_ioPortDispatcher, _state, logger, false);
        _scheduler = new DeviceScheduler(clock, logger, "Emulation loop");
        _pit = new PitTimer(_ioPortDispatcher, _state, _pic, new PitTimerTests.StubPitSpeaker(), _scheduler, clock,
            logger, false);
    }

    [Fact]
    public void CaptureChangesNothing() {
        // Arrange
        ProgramChannel0(Channel0RateGenerator, TenMillisecondDivisor);
        _state.Cycles = 4;
        double? nextEventTime = _scheduler.NextEventTime;

        // Act
        PitTimerState first = _pit.CaptureState();
        PitTimerState second = _pit.CaptureState();

        // Assert
        second.Should().Be(first);
        _scheduler.NextEventTime.Should().Be(nextEventTime);
    }

    [Fact]
    public void CapturedStateHoldsTheProgrammedChannelAndItsEvent() {
        // Arrange
        ProgramChannel0(Channel0RateGenerator, TenMillisecondDivisor);
        _state.Cycles = 4;

        // Act
        PitTimerState state = _pit.CaptureState();

        // Assert
        state.Channel0.Count.Should().Be(TenMillisecondDivisor);
        state.Channel0.Mode.Should().Be(PitMode.RateGenerator);
        state.Channel0.Start.Should().Be(0);
        state.CapturedAtMs.Should().Be(4);
        state.Channel0EventDelayMs.Should().NotBeNull();
        state.Channel0EventDelayMs!.Value.Should().BeApproximately(TenMillisecondDelay - 4, TimeTolerance);
    }

    [Fact]
    public void RestoreAfterChangesThroughPortsGivesTheCapturedState() {
        // Arrange: channel 0 programmed and its status latched, channel 2 with half of its divisor written
        _state.Cycles = 7;
        ProgramChannel0(Channel0RateGenerator, TenMillisecondDivisor);
        _ioPortDispatcher.WriteByte(ControlPort, Channel2SquareWave);
        _ioPortDispatcher.WriteByte(Channel2Port, 0x34);
        _ioPortDispatcher.WriteByte(ControlPort, LatchChannel0Status);
        PitTimerState captured = _pit.CaptureState();

        // Act: change the channels through the ports, reset the timer, then restore
        ProgramChannel0(Channel0SquareWave, 0x1000);
        _ioPortDispatcher.WriteByte(Channel2Port, 0x12);
        _ioPortDispatcher.ReadByte(Channel0Port);
        _pit.Reset();
        _pit.RestoreState(captured);

        // Assert: everything is equal. The event delay goes through the scheduler's absolute time, so it is
        // compared with a tolerance.
        PitTimerState restored = _pit.CaptureState();
        restored.Should().Be(captured with { Channel0EventDelayMs = restored.Channel0EventDelayMs });
        restored.Channel0EventDelayMs.Should().NotBeNull();
        restored.Channel0EventDelayMs!.Value.Should().BeApproximately(captured.Channel0EventDelayMs!.Value,
            TimeTolerance);
        captured.Channel0.CounterStatusSet.Should().BeTrue();
        captured.LatchedTimerStatusLocked.Should().BeTrue();
        captured.Channel2.WriteLatch.Should().Be(0x0034);
    }

    [Fact]
    public void RestoreAtALaterTimeMovesPeriodStartsAndTheEvent() {
        // Arrange
        ProgramChannel0(Channel0RateGenerator, TenMillisecondDivisor);
        _state.Cycles = 3;
        PitTimerState captured = _pit.CaptureState();

        // Act
        _state.Cycles = 253;
        _pit.RestoreState(captured);

        // Assert: 250 ms passed, so every period start is 250 ms later and the event is as far ahead as it was
        PitTimerState restored = _pit.CaptureState();
        restored.CapturedAtMs.Should().Be(253);
        restored.Channel0.Start.Should().Be(captured.Channel0.Start + 250);
        restored.Channel1.Start.Should().Be(captured.Channel1.Start + 250);
        restored.Channel2.Start.Should().Be(captured.Channel2.Start + 250);
        _scheduler.NextEventTime.Should().NotBeNull();
        _scheduler.NextEventTime!.Value.Should().BeApproximately(250 + TenMillisecondDelay, TimeTolerance);
    }

    [Fact]
    public void CounterReadAfterRestoreAtALaterTimeEqualsTheReadAfterCapture() {
        // Arrange
        ProgramChannel0(Channel0RateGenerator, TenMillisecondDivisor);
        _state.Cycles = 3;
        PitTimerState captured = _pit.CaptureState();
        ushort countAfterCapture = ReadChannel0Counter();

        // Act
        _state.Cycles = 253;
        _pit.RestoreState(captured);
        ushort countAfterRestore = ReadChannel0Counter();

        // Assert
        countAfterRestore.Should().Be(countAfterCapture);
        countAfterCapture.Should().BeLessThan(TenMillisecondDivisor);
    }

    [Fact]
    public void RestoredEventRaisesIrq0WhenItsDelayHasPassed() {
        // Arrange: captured 4 ms into a 10 ms period, then the period ended and the timer ticked
        ProgramChannel0(Channel0RateGenerator, TenMillisecondDivisor);
        _state.Cycles = 4;
        PitTimerState captured = _pit.CaptureState();
        _state.Cycles = 20;
        _scheduler.ProcessEvents();

        // Act: restored at 20 ms, the event is due about 6 ms later
        _pit.RestoreState(captured);
        _pic.DeactivateIrq(0);
        _state.Cycles = 25;
        _scheduler.ProcessEvents();
        bool raisedBeforeTheDelay = _pic.IrqCheck;
        _state.Cycles = 27;
        _scheduler.ProcessEvents();
        bool raisedAfterTheDelay = _pic.IrqCheck;

        // Assert
        raisedBeforeTheDelay.Should().BeFalse();
        raisedAfterTheDelay.Should().BeTrue();
    }

    [Fact]
    public void StateWithoutAnEventRemovesTheQueuedEvent() {
        // Arrange: in mode 0 the event fires once and is not queued again
        ProgramChannel0(Channel0InterruptOnTerminalCount, TenMillisecondDivisor);
        _state.Cycles = 20;
        _scheduler.ProcessEvents();
        PitTimerState captured = _pit.CaptureState();
        ProgramChannel0(Channel0RateGenerator, TenMillisecondDivisor);
        double? eventTimeBeforeRestore = _scheduler.NextEventTime;

        // Act
        _pit.RestoreState(captured);

        // Assert
        captured.Channel0EventDelayMs.Should().BeNull();
        eventTimeBeforeRestore.Should().NotBeNull();
        _scheduler.NextEventTime.Should().BeNull();
        _pit.CaptureState().Channel0.Mode.Should().Be(PitMode.InterruptOnTerminalCount);
    }

    private void ProgramChannel0(byte controlWord, ushort divisor) {
        _ioPortDispatcher.WriteByte(ControlPort, controlWord);
        _ioPortDispatcher.WriteByte(Channel0Port, (byte)divisor);
        _ioPortDispatcher.WriteByte(Channel0Port, (byte)(divisor >> 8));
    }

    private ushort ReadChannel0Counter() {
        _ioPortDispatcher.WriteByte(ControlPort, LatchChannel0Counter);
        byte low = _ioPortDispatcher.ReadByte(Channel0Port);
        byte high = _ioPortDispatcher.ReadByte(Channel0Port);
        return (ushort)(low | (high << 8));
    }
}
