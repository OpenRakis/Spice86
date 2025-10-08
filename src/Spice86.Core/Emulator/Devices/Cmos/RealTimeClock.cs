namespace Spice86.Core.Emulator.Devices.Cmos;

using System;
using System.Diagnostics;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Serilog.Events;

/// <summary>
/// TODO: unit tests of events times
/// TODO: integrate into INT21H
/// TODO: integrate into BIOS / BIOS_DISK
/// TODO: integrate into DI (along with shared instance of CmosRegisters)
/// TODO: double-check with online sources and integrate online documentation into XML summaries.
/// Emulates the MC146818 Real Time Clock (RTC) and CMOS RAM.
/// Handles register semantics, BCD handling, and periodic/status register behavior.
/// Periodic events are processed lazily on port access; paused time (via IPauseHandler) does not advance RTC timing.
/// </summary>
public class RealTimeClock : DefaultIOPortHandler, IDisposable {
    private const ushort AddressPort = 0x70;
    private const ushort DataPort = 0x71;
    private const byte RegisterA = 0x0A;
    private const byte RegisterB = 0x0B;
    private const byte RegisterC = 0x0C;
    private const byte RegisterD = 0x0D;
    private const byte RegisterStatusShutdown = 0x0F;
    private const byte RegisterCentury = 0x32;

    private readonly DualPic _dualPic;
    private readonly CmosRegisters _cmosRegisters = new();
    private readonly IPauseHandler _pauseHandler;

    // High resolution baseline timestamp (Stopwatch ticks)
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();

    // Pause accounting (Stopwatch ticks excluded from elapsed time)
    private long _pausedAccumulatedTicks;
    private long _pauseStartedTicks;
    private bool _isPaused; // reflects current pause state (set on Paused, cleared on Resumed)
    private bool _disposed;

    private double _nextPeriodicTriggerMs;

    public RealTimeClock(State state, IOPortDispatcher ioPortDispatcher, DualPic dualPic,
        IPauseHandler pauseHandler, bool failOnUnhandledPort, ILoggerService loggerService)
        : base(state, failOnUnhandledPort, loggerService) {
        _dualPic = dualPic;
        _pauseHandler = pauseHandler;

        // Subscribe to pause lifecycle events to keep timing exact.
        _pauseHandler.Pausing += OnPausing;
        _pauseHandler.Paused += OnPaused;
        _pauseHandler.Resumed += OnResumed;

        _cmosRegisters[RegisterA] = 0x26; // default rate/divider
        _cmosRegisters[RegisterB] = 0x02; // 24h mode
        _cmosRegisters[RegisterD] = 0x80; // power good

        _cmosRegisters[0x15] = 0x80; // 640KB low
        _cmosRegisters[0x16] = 0x02;

        ioPortDispatcher.AddIOPortHandler(AddressPort, this);
        ioPortDispatcher.AddIOPortHandler(DataPort, this);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("CMOS/RTC initialized");
        }
    }

    public override void WriteByte(ushort port, byte value) {
        if (port == AddressPort || port == DataPort) {
            ProcessPendingPeriodicEvents();
        }
        switch (port) {
            case AddressPort:
                HandleAddressPortWrite(value);
                break;
            case DataPort:
                HandleDataPortWrite(value);
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }

    public override byte ReadByte(ushort port) {
        if (port == DataPort) {
            ProcessPendingPeriodicEvents();
            return HandleDataPortRead();
        }
        return base.ReadByte(port);
    }

    private void HandleAddressPortWrite(byte value) {
        _cmosRegisters.CurrentRegister = (byte)(value & 0x3F);
        _cmosRegisters.NmiEnabled = (value & 0x80) == 0;
    }

    private void HandleDataPortWrite(byte value) {
        byte reg = _cmosRegisters.CurrentRegister;
        switch (reg) {
            case 0x00:
            case 0x02:
            case 0x04:
            case 0x06:
            case 0x07:
            case 0x08:
            case 0x09:
            case RegisterCentury:
                return;

            case 0x01:
            case 0x03:
            case 0x05:
                _cmosRegisters[reg] = value;
                if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                    _loggerService.Information("Alarm register {Reg:X2} set to {Val:X2}", reg, value);
                }
                return;

            case RegisterA:
                _cmosRegisters[reg] = (byte)(value & 0x7F);
                byte newDiv = (byte)(value & 0x0F);
                if (newDiv <= 2) {
                    newDiv += 7;
                }
                _cmosRegisters.Timer.Divider = newDiv;
                RecalculatePeriodicDelay();
                ValidateDivider(value);
                return;

            case RegisterB:
                _cmosRegisters.IsBcdMode = (value & 0x04) == 0;
                _cmosRegisters[reg] = (byte)(value & 0x7F);
                bool prevEnabled = _cmosRegisters.Timer.Enabled;
                _cmosRegisters.Timer.Enabled = (value & 0x40) != 0;
                if ((value & 0x10) != 0 && _loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("CMOS: Update-ended interrupt not supported (bit 4 set in Register B).");
                }
                if (_cmosRegisters.Timer.Enabled && !prevEnabled) {
                    ScheduleNextPeriodic();
                }
                return;

            case RegisterD:
                _cmosRegisters[reg] = (byte)(value & 0x80);
                return;

            case RegisterStatusShutdown:
                _cmosRegisters[reg] = (byte)(value & 0x7F);
                return;

            default:
                _cmosRegisters[reg] = (byte)(value & 0x7F);
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("CMOS: Write to unhandled register {Reg:X2} value {Val:X2}", reg, value);
                }
                return;
        }
    }

    private byte HandleDataPortRead() {
        byte reg = _cmosRegisters.CurrentRegister;
        if (reg > 0x3F) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("CMOS: Read from illegal register {Reg:X2}", reg);
            }
            return 0xFF;
        }

        DateTime now = DateTime.Now;
        switch (reg) {
            case 0x00: return EncodeTimeComponent(now.Second);
            case 0x02: return EncodeTimeComponent(now.Minute);
            case 0x04: return EncodeTimeComponent(now.Hour);
            case 0x06: return EncodeTimeComponent(((int)now.DayOfWeek + 1));
            case 0x07: return EncodeTimeComponent(now.Day);
            case 0x08: return EncodeTimeComponent(now.Month);
            case 0x09: return EncodeTimeComponent(now.Year % 100);
            case RegisterCentury: return EncodeTimeComponent(now.Year / 100);
            case 0x01:
            case 0x03:
            case 0x05:
                return _cmosRegisters[reg];
            case RegisterA: {
                byte baseA = (byte)(_cmosRegisters[reg] & 0x7F);
                return IsUpdateInProgress(now) ? (byte)(baseA | 0x80) : baseA;
            }
            case RegisterC:
                return ReadStatusC();
            case RegisterB:
            case RegisterD:
            case RegisterStatusShutdown:
            case 0x14:
            case 0x15:
            case 0x16:
            case 0x17:
            case 0x18:
            case 0x30:
            case 0x31:
                return _cmosRegisters[reg];
            default:
                return _cmosRegisters[reg];
        }
    }

    private byte ReadStatusC() {
        _cmosRegisters.Timer.Acknowledged = true;

        if (_cmosRegisters.Timer.Enabled) {
            byte latched = _cmosRegisters[RegisterC];
            _cmosRegisters[RegisterC] = 0;
            return latched;
        }

        double nowMs = GetElapsedMilliseconds();
        byte value = 0;

        if (nowMs >= (_cmosRegisters.Last.Timer + _cmosRegisters.Timer.Delay)) {
            _cmosRegisters.Last.Timer = nowMs;
            value |= 0x40;
        }
        if (nowMs >= (_cmosRegisters.Last.Ended + 1000.0)) {
            _cmosRegisters.Last.Ended = nowMs;
            value |= 0x10;
        }
        return value;
    }

    private void RecalculatePeriodicDelay() {
        byte div = _cmosRegisters.Timer.Divider;
        if (div == 0) {
            _cmosRegisters.Timer.Delay = 0;
            return;
        }
        double hz = 32768.0 / (1 << (div - 1));
        if (hz <= 0) {
            _cmosRegisters.Timer.Delay = 0;
            return;
        }
        _cmosRegisters.Timer.Delay = 1000.0 / hz;
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("RTC periodic timer configured: divider={Div} frequency={Freq:F2}Hz period={Period:F3}ms",
                div, hz, _cmosRegisters.Timer.Delay);
        }
    }

    private void ScheduleNextPeriodic() {
        if (_cmosRegisters.Timer.Delay <= 0) {
            return;
        }
        double nowMs = GetElapsedMilliseconds();
        double rem = nowMs % _cmosRegisters.Timer.Delay;
        _nextPeriodicTriggerMs = nowMs + (_cmosRegisters.Timer.Delay - rem);
    }

    private void ProcessPendingPeriodicEvents() {
        // Pause-aware: events do not fire while paused.
        if (_isPaused) {
            return;
        }
        if (!_cmosRegisters.Timer.Enabled || _cmosRegisters.Timer.Delay <= 0) {
            return;
        }
        double nowMs = GetElapsedMilliseconds();
        if (nowMs >= _nextPeriodicTriggerMs) {
            _cmosRegisters[RegisterC] |= 0xC0; // Contraption Zack (music)
            _cmosRegisters.Timer.Acknowledged = false;
            _cmosRegisters.Last.Timer = nowMs;
            while (nowMs >= _nextPeriodicTriggerMs) {
                _nextPeriodicTriggerMs += _cmosRegisters.Timer.Delay;
            }
            _dualPic.ProcessInterruptRequest(8);
        }
    }

    private bool IsUpdateInProgress(DateTime now) {
        double fractional = (now.TimeOfDay.TotalMilliseconds % 1000.0) / 1000.0;
        return fractional < 0.002;
    }

    private byte EncodeTimeComponent(int value) =>
        _cmosRegisters.IsBcdMode ? ToBcd((byte)value) : (byte)value;

    private static byte ToBcd(byte binary) {
        int tens = binary / 10;
        int ones = binary % 10;
        return (byte)((tens << 4) | ones);
    }

    private double GetElapsedMilliseconds() {
        long now = Stopwatch.GetTimestamp();
        long effectiveTicks = now - _startTimestamp - _pausedAccumulatedTicks;
        if (_isPaused) {
            // Exclude time elapsed since pause started
            effectiveTicks -= (now - _pauseStartedTicks);
        }
        if (effectiveTicks < 0) {
            effectiveTicks = 0;
        }
        return effectiveTicks * (1000.0 / Stopwatch.Frequency);
    }

    private void ValidateDivider(byte written) {
        if ((written & 0x70) != 0x20 && _loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("CMOS: Illegal 22-stage divider value in Register A: {Val:X2}", written);
        }
    }

    // Pause event handlers
    private void OnPausing() {
        if (_isPaused) {
            return; // already paused
        }
        _pauseStartedTicks = Stopwatch.GetTimestamp();
    }

    private void OnPaused() {
        // mark state paused (elapsed time will exclude ticks from now on)
        _isPaused = true;
    }

    private void OnResumed() {
        if (!_isPaused) {
            return;
        }
        long now = Stopwatch.GetTimestamp();
        _pausedAccumulatedTicks += (now - _pauseStartedTicks);
        _isPaused = false;
        // Re-align next periodic trigger so it doesn't instantly fire after a long pause
        if (_cmosRegisters.Timer.Enabled && _cmosRegisters.Timer.Delay > 0) {
            double nowMs = GetElapsedMilliseconds();
            double period = _cmosRegisters.Timer.Delay;
            double rem = nowMs % period;
            _nextPeriodicTriggerMs = nowMs + (period - rem);
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        // Unsubscribe to avoid leaks
        _pauseHandler.Pausing -= OnPausing;
        _pauseHandler.Paused -= OnPaused;
        _pauseHandler.Resumed -= OnResumed;
        GC.SuppressFinalize(this);
    }
}