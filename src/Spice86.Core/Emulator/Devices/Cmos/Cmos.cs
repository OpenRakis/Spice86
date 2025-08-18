namespace Spice86.Core.Emulator.Devices.Cmos;

using System;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;
using Serilog.Events;
using Spice86.Core.Emulator.Devices.ExternalInput;
using System.Diagnostics;

/// <summary>
/// Emulates the MC146818 Real Time Clock (RTC) and CMOS RAM (64 bytes) similar to DOSBox's implementation,
/// translated from cmos.cpp. Focuses on register semantics, BCD handling, and periodic/status register behavior.
/// </summary>
public class Cmos : DefaultIOPortHandler {
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

    // Time base for emulation (milliseconds since start)
    private readonly DateTime _start = DateTime.UtcNow;

    // For approximating the periodic behavior when not using an event scheduler
    private double _nextPeriodicTriggerMs;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cmos"/> class.
    /// </summary>
    public Cmos(State state, IOPortDispatcher ioPortDispatcher, DualPic dualPic,
        bool failOnUnhandledPort, ILoggerService loggerService)
        : base(state, failOnUnhandledPort, loggerService) {
        _dualPic = dualPic;

        // Initialize core registers to IBM PC defaults
        _cmosRegisters[RegisterA] = 0x26; // 32.768 kHz base, divider select (will be masked on write logic)
        _cmosRegisters[RegisterB] = 0x02; // 24-hour mode (bit 1 set)
        _cmosRegisters[RegisterD] = 0x80; // RTC power-on (bit 7)

        // Conventional memory size (0x0280 = 640KB)
        _cmosRegisters[0x15] = 0x80;
        _cmosRegisters[0x16] = 0x02;

        // Basic extended memory mirrors (can be refined if memory manager available)
        // (Leaving zero unless a memory subsystem provides total size.)

        ioPortDispatcher.AddIOPortHandler(AddressPort, this);
        ioPortDispatcher.AddIOPortHandler(DataPort, this);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("CMOS/RTC initialized");
        }
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
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

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        if (port == DataPort) {
            return HandleDataPortRead();
        }
        return base.ReadByte(port);
    }

    private void HandleAddressPortWrite(byte value) {
        // Bits 0–5: register index, Bit 7: NMI disable (1 = disable), we expose NmiEnabled
        _cmosRegisters.CurrentRegister = (byte)(value & 0x3F);
        _cmosRegisters.NmiEnabled = (value & 0x80) == 0;
    }

    private void HandleDataPortWrite(byte value) {
        byte reg = _cmosRegisters.CurrentRegister;

        switch (reg) {
            // Time/date main counters: writes ignored (RTC updates them internally)
            case 0x00: // Seconds
            case 0x02: // Minutes
            case 0x04: // Hours
            case 0x06: // Day of week
            case 0x07: // Day of month
            case 0x08: // Month
            case 0x09: // Year
            case RegisterCentury: // Century
                // Ignore to match reference behavior of DOSBox Staging
                break;

            // Alarm registers: store raw
            case 0x01: // Seconds Alarm
            case 0x03: // Minutes Alarm
            case 0x05: // Hours Alarm
                _cmosRegisters[reg] = value;
                if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                    _loggerService.Information("Alarm register {Reg:X2} set to {Val:X2}", reg, value);
                }
                break;

            case RegisterA:
                // Only lower 7 bits stored; bit7 = Update In Progress (read-only in hardware)
                _cmosRegisters[reg] = (byte)(value & 0x7F);
                byte newDiv = (byte)(value & 0x0F);
                // DOSBox logic: if divider <= 2 then += 7
                if (newDiv <= 2) {
                    newDiv += 7;
                }
                _cmosRegisters.Timer.Divider = newDiv;
                RecalculatePeriodicDelay();
                ValidateDivider(value);
                break;

            case RegisterB:
                _cmosRegisters.IsBcdMode = (value & 0x04) == 0; // Bit 2 clear => BCD mode
                _cmosRegisters[reg] = (byte)(value & 0x7F);     // Mask bit7
                bool prevEnabled = _cmosRegisters.Timer.Enabled;
                _cmosRegisters.Timer.Enabled = (value & 0x40) != 0; // Bit 6 periodic enable
                if ((value & 0x10) != 0) {
                    if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                        _loggerService.Error("CMOS: Update-ended interrupt not supported yet (bit 4 set in Register B).");
                    }
                }
                if (_cmosRegisters.Timer.Enabled && !prevEnabled) {
                    ScheduleNextPeriodic();
                }
                break;

            case RegisterD:
                _cmosRegisters[reg] = (byte)(value & 0x80); // Bit 7 only
                break;

            case RegisterStatusShutdown:
                _cmosRegisters[reg] = (byte)(value & 0x7F);
                break;

            default:
                _cmosRegisters[reg] = (byte)(value & 0x7F);
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("CMOS: Write to unhandled register {Reg:X2} value {Val:X2}", reg, value);
                }
                break;
        }
    }

    private byte HandleDataPortRead() {
        byte reg = _cmosRegisters.CurrentRegister;

        if (reg > 64) {
            if(_loggerService.IsEnabled(LogEventLevel.Warning))
                _loggerService.Warning("CMOS: Read from illegal register {Reg:X2}", reg);
            return 0xFF;
        }

        DateTime now = DateTime.Now; // Matches localtime from C version
        switch (reg) {
            case 0x00: return EncodeTimeComponent(now.Second);
            case 0x02: return EncodeTimeComponent(now.Minute);
            case 0x04: return EncodeTimeComponent(now.Hour);
            case 0x06: return EncodeTimeComponent(((int)now.DayOfWeek + 1)); // 1..7
            case 0x07: return EncodeTimeComponent(now.Day);
            case 0x08: return EncodeTimeComponent(now.Month);
            case 0x09: return EncodeTimeComponent(now.Year % 100);
            case RegisterCentury: return EncodeTimeComponent(now.Year / 100);

            case 0x01: // Seconds Alarm
            case 0x03: // Minutes Alarm
            case 0x05: // Hours Alarm
                return _cmosRegisters[reg];

            case RegisterA:
                // Bit 7 (UIP) set during the ~2ms RTC update window (approximation)
                byte baseA = (byte)(_cmosRegisters[reg] & 0x7F);
                return IsUpdateInProgress(now) ? (byte)(baseA | 0x80) : baseA;

            case RegisterC:
                return ReadStatusC();

            case RegisterB:
            case RegisterD:
            case RegisterStatusShutdown:
            case 0x14: // Equipment byte (external sources may modify)
            case 0x15: // Base mem low
            case 0x16: // Base mem high
            case 0x17: // Ext mem low
            case 0x18: // Ext mem high
            case 0x30: // Ext mem 2 low
            case 0x31: // Ext mem 2 high
                return _cmosRegisters[reg];

            default:
                // For disk geometry / device info registers or others we don't yet emulate:
                return _cmosRegisters[reg];
        }
    }

    private byte ReadStatusC() {
        _cmosRegisters.Timer.Acknowledged = true;

        // If periodic mode enabled: return and clear latched value.
        if (_cmosRegisters.Timer.Enabled) {
            byte latched = _cmosRegisters[RegisterC];
            _cmosRegisters[RegisterC] = 0;
            return latched;
        }

        // Otherwise synthesize flags based on elapsed times
        double nowMs = GetHostTimestamp();
        byte value = 0;

        if (nowMs >= (_cmosRegisters.Last.Timer + _cmosRegisters.Timer.Delay)) {
            _cmosRegisters.Last.Timer = nowMs;
            value |= 0x40; // Periodic interrupt flag
        }

        if (nowMs >= (_cmosRegisters.Last.Ended + 1000.0)) {
            _cmosRegisters.Last.Ended = nowMs;
            value |= 0x10; // Update-ended flag
        }

        return value;
    }

    private void RecalculatePeriodicDelay() {
        // Base clock 32768 Hz, period = 1000 ms / (32768 / (1 << (div-1)))
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
        double nowMs = GetHostTimestamp();
        // Align like DOSBox: schedule at delay - remainder of current cycle
        double rem = nowMs % _cmosRegisters.Timer.Delay;
        _nextPeriodicTriggerMs = nowMs + (_cmosRegisters.Timer.Delay - rem);
    }

    /// <summary>
    /// Should be called periodically by the emulator main loop (if integrated) to simulate the RTC periodic interrupt.
    /// </summary>
    public void Tick() {
        if (!_cmosRegisters.Timer.Enabled || _cmosRegisters.Timer.Delay <= 0) {
            return;
        }
        double nowMs = GetHostTimestamp();
        if (nowMs >= _nextPeriodicTriggerMs) {
            // Latch periodic interrupt flags into status C (upper two bits used in DOSBox for some games)
            _cmosRegisters[RegisterC] |= 0xC0; //Contraption Zack (music)
            _cmosRegisters.Timer.Acknowledged = false;
            _cmosRegisters.Last.Timer = nowMs;
            _nextPeriodicTriggerMs += _cmosRegisters.Timer.Delay;
            _dualPic.ProcessInterruptRequest(8);
        }
    }

    private bool IsUpdateInProgress(DateTime now) {
        // Approximate a 2ms window at end of each second as in DOSBox logic (TickIndex < 0.002f)
        // Use fractional part of the current second.
        double fractional = now.TimeOfDay.TotalMilliseconds % 1000.0 / 1000.0;
        return fractional < 0.002;
    }

    private byte EncodeTimeComponent(int value) {
        return _cmosRegisters.IsBcdMode ? ToBcd((byte)value) : (byte)value;
    }

    private static byte ToBcd(byte binary) {
        int tens = binary / 10;
        int ones = binary % 10;
        return (byte)((tens << 4) | ones);
    }

    private long GetHostTimestamp() => Stopwatch.GetTimestamp();

    private void ValidateDivider(byte written) {
        if ((written & 0x70) != 0x20) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("CMOS: Illegal 22-stage divider value in Register A: {Val:X2}", written);
            }
        }
    }
}