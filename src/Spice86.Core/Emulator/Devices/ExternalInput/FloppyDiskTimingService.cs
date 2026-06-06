namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;

using System;
using System.Threading;

/// <summary>
/// Applies floppy I/O timing to image-backed transfers.
/// </summary>
public sealed class FloppyDiskTimingService {
    private const int BytesPerKilobyte = 1024;
    private const int BytesPerSector = 512;
    private const int FastKilobytesPerSecond = 120;
    private const int MediumKilobytesPerSecond = 60;
    private const int SlowKilobytesPerSecond = 30;

    private readonly State _state;
    private readonly IEmulatedClock _clock;
    private readonly DeviceScheduler _scheduler;
    private readonly FloppyDiskSpeed _speed;

    /// <summary>
    /// Initialises a new <see cref="FloppyDiskTimingService"/>.
    /// </summary>
    /// <param name="state">The CPU state used when the emulated clock is cycle-based.</param>
    /// <param name="clock">The emulated clock that defines the active timing model.</param>
    /// <param name="scheduler">The shared device scheduler that must continue running during disk waits.</param>
    /// <param name="speed">The floppy controller speed preset to apply.</param>
    public FloppyDiskTimingService(State state, IEmulatedClock clock, DeviceScheduler scheduler, FloppyDiskSpeed speed) {
        _state = state;
        _clock = clock;
        _scheduler = scheduler;
        _speed = speed;
    }

    /// <summary>
    /// Schedules the floppy I/O delay for the specified number of transferred sectors.
    /// </summary>
    /// <param name="sectorCount">The number of 512-byte sectors involved in the transfer.</param>
    /// <returns>The applied busy-wait delay, in milliseconds.</returns>
    public double ScheduleFloppyIoDelay(int sectorCount) {
        _scheduler.ProcessEvents();

        if (sectorCount <= 0) {
            return 0;
        }

        double delayMs = CalculateDelayMs(sectorCount);
        if (delayMs <= 0) {
            return 0;
        }

        if (_clock is CyclesClock cyclesClock) {
            DelayWithCyclesClock(delayMs, cyclesClock);
        } else {
            DelayWithElapsedClock(delayMs);
        }

        return delayMs;
    }

    private double CalculateDelayMs(int sectorCount) {
        int kilobytesPerSecond;
        switch (_speed) {
            case FloppyDiskSpeed.Maximum:
                return 0;
            case FloppyDiskSpeed.Fast:
                kilobytesPerSecond = FastKilobytesPerSecond;
                break;
            case FloppyDiskSpeed.Medium:
                kilobytesPerSecond = MediumKilobytesPerSecond;
                break;
            case FloppyDiskSpeed.Slow:
                kilobytesPerSecond = SlowKilobytesPerSecond;
                break;
            default:
                throw new InvalidOperationException($"Unsupported floppy disk speed {_speed}.");
        }

        double bytesTransferred = sectorCount * BytesPerSector;
        return bytesTransferred * 1000.0 / (kilobytesPerSecond * BytesPerKilobyte);
    }

    private void DelayWithCyclesClock(double delayMs, CyclesClock cyclesClock) {
        double targetTimeMs = GetCurrentCyclesTimeMs(cyclesClock.CyclesPerSecond) + delayMs;
        double? nextEventTime = _scheduler.NextEventTime;
        while (nextEventTime != null && nextEventTime.Value <= targetTimeMs) {
            AdvanceCyclesToTime(nextEventTime.Value, cyclesClock.CyclesPerSecond);
            _scheduler.ProcessEvents();
            nextEventTime = _scheduler.NextEventTime;
        }

        AdvanceCyclesToTime(targetTimeMs, cyclesClock.CyclesPerSecond);
        _scheduler.ProcessEvents();
    }

    private void DelayWithElapsedClock(double delayMs) {
        double endTimeMs = _clock.ElapsedTimeMs + delayMs;
        while (_clock.ElapsedTimeMs < endTimeMs) {
            _scheduler.ProcessEvents();
            Thread.Yield();
        }

        _scheduler.ProcessEvents();
    }

    private double GetCurrentCyclesTimeMs(long cyclesPerSecond) {
        return (double)_state.Cycles * 1000 / cyclesPerSecond;
    }

    private void AdvanceCyclesToTime(double targetTimeMs, long cyclesPerSecond) {
        long targetCycles = (long)Math.Ceiling(targetTimeMs * cyclesPerSecond / 1000.0);
        if (targetCycles > _state.Cycles) {
            _state.Cycles = targetCycles;
        }
    }
}