namespace Spice86.Core.Emulator.Devices.Cmos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Emulates the MC146818 Real Time Clock (RTC) and CMOS RAM.
/// <para>
/// Provides real-time clock with date/time in BCD or binary format, 64 bytes of battery-backed CMOS RAM,
/// periodic interrupt capability (IRQ 8), and alarm functionality.
/// </para>
/// <para>
/// I/O Ports: 0x70 (address register), 0x71 (data register).
/// Key registers: 0x00-0x09 (time/date), 0x0A-0x0D (status), 0x0F (shutdown), 0x10+ (CMOS config).
/// </para>
/// <para>
/// Limitations: UIP timing is approximate, alarm interrupts not implemented, partial CMOS config support.
/// </para>
/// </summary>
public sealed class RealTimeClock : DefaultIOPortHandler, IDisposable {
    private readonly DualPic _dualPic;
    private readonly CmosRegisters _cmosRegisters = new();
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;

    private bool _disposed;

    /// <summary>
    /// Gets the emulated clock used by the RTC for time calculations.
    /// </summary>
    public IEmulatedClock Clock => _clock;

    /// <summary>
    /// Initializes the RTC/CMOS device with default register values.
    /// </summary>
    public RealTimeClock(State state, IOPortDispatcher ioPortDispatcher, DualPic dualPic,
        EmulationLoopScheduler scheduler, IEmulatedClock clock, bool failOnUnhandledPort, ILoggerService loggerService)
        : base(state, failOnUnhandledPort, loggerService) {
        _dualPic = dualPic;
        _scheduler = scheduler;
        _clock = clock;

        _cmosRegisters[CmosRegisterAddresses.StatusRegisterA] = 0x26;
        _cmosRegisters[CmosRegisterAddresses.StatusRegisterB] = 0x02;
        _cmosRegisters[CmosRegisterAddresses.StatusRegisterD] = 0x80;
        _cmosRegisters.IsBcdMode = (_cmosRegisters[CmosRegisterAddresses.StatusRegisterB] & 0x04) == 0;
        _cmosRegisters[0x15] = 0x80;
        _cmosRegisters[0x16] = 0x02;

        byte initialDiv = (byte)(0x26 & 0x0F);
        if (initialDiv <= 2) {
            initialDiv += 7;
        }
        _cmosRegisters.Timer.Divider = initialDiv;
        RecalculatePeriodicDelay();

        ioPortDispatcher.AddIOPortHandler(CmosPorts.Address, this);
        ioPortDispatcher.AddIOPortHandler(CmosPorts.Data, this);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("CMOS/RTC initialized");
        }
    }

    /// <summary>
    /// Handles writes to port 0x70 (address/index selection) and 0x71 (data).
    /// </summary>
    public override void WriteByte(ushort port, byte value) {
        if (_loggerService.IsEnabled(LogEventLevel.Information) && port == CmosPorts.Address && value == 0x0B) {
            _loggerService.Information("RTC: Writing 0x0B to address port (selecting StatusB for next read/write)");
        }
        switch (port) {
            case CmosPorts.Address:
                HandleAddressPortWrite(value);
                break;
            case CmosPorts.Data:
                HandleDataPortWrite(value);
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }

    /// <summary>
    /// Handles reads from port 0x71 (data register).
    /// Port 0x70 is write-only.
    /// </summary>
    public override byte ReadByte(ushort port) {
        if (port == CmosPorts.Data) {
            byte result = HandleDataPortRead();
            if (_loggerService.IsEnabled(LogEventLevel.Information) && _cmosRegisters.CurrentRegister == CmosRegisterAddresses.StatusRegisterB) {
                _loggerService.Information("RTC: Port 0x71 read returning 0x{Result:X2} for register 0x{Reg:X2} (StatusB, PIE={PIE})",
                    result, _cmosRegisters.CurrentRegister, (result & 0x40) != 0);
            }
            return result;
        }
        return base.ReadByte(port);
    }

    /// <summary>
    /// Handles writes to port 0x70 (index register).
    /// Bits 0-5: Register index to select
    /// Bit 7: NMI enable (0=enabled, 1=disabled)
    /// </summary>
    private void HandleAddressPortWrite(byte value) {
        _cmosRegisters.CurrentRegister = (byte)(value & 0x3F);
        _cmosRegisters.NmiEnabled = (value & 0x80) == 0;
    }

    /// <summary>
    /// Handles writes to port 0x71 (data register).
    /// Behavior depends on the currently selected register (set via port 0x70).
    /// </summary>
    private void HandleDataPortWrite(byte value) {
        byte reg = _cmosRegisters.CurrentRegister;
        switch (reg) {
            case CmosRegisterAddresses.Seconds:
            case CmosRegisterAddresses.Minutes:
            case CmosRegisterAddresses.Hours:
            case CmosRegisterAddresses.DayOfWeek:
            case CmosRegisterAddresses.DayOfMonth:
            case CmosRegisterAddresses.Month:
            case CmosRegisterAddresses.Year:
            case CmosRegisterAddresses.Century:
                _cmosRegisters[reg] = value;
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("CMOS: Time/date register {Reg:X2} set to {Val:X2} (stored but not used for time reads)", reg, value);
                }
                return;

            case 0x01:
            case 0x03:
            case 0x05:
                _cmosRegisters[reg] = value;
                if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                    _loggerService.Information("CMOS: Alarm register {Reg:X2} set to {Val:X2}", reg, value);
                }
                return;

            case CmosRegisterAddresses.StatusRegisterA:
                _cmosRegisters[reg] = (byte)(value & 0x7F);
                byte newDiv = (byte)(value & 0x0F);
                if (newDiv <= 2) {
                    newDiv += 7;
                }
                _cmosRegisters.Timer.Divider = newDiv;
                RecalculatePeriodicDelay();
                ValidateDivider(value);
                return;

            case CmosRegisterAddresses.StatusRegisterB:  // 0x0B - Status Register B (format/interrupt control)
                _cmosRegisters.IsBcdMode = (value & 0x04) == 0;
                _cmosRegisters[reg] = (byte)(value & 0x7F);
                bool prevEnabled = _cmosRegisters.Timer.Enabled;
                _cmosRegisters.Timer.Enabled = (value & 0x40) != 0;
                if ((value & 0x10) != 0 && _loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("CMOS: Update-ended interrupt not supported (bit 4 set in Register B).");
                }
                if (_cmosRegisters.Timer.Enabled && !prevEnabled) {
                    if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                        _loggerService.Information("RTC: Periodic interrupt enabled via Status Register B write");
                    }
                    ScheduleNextPeriodicInterrupt();
                } else if (!_cmosRegisters.Timer.Enabled && prevEnabled) {
                    if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                        _loggerService.Information("RTC: Periodic interrupt disabled via Status Register B write");
                    }
                    CancelPeriodicInterrupts();
                }
                return;

            case CmosRegisterAddresses.StatusRegisterD:  // 0x0D - Status Register D (battery status)
                _cmosRegisters[reg] = (byte)(value & 0x80);  // Bit 7 = RTC power on
                return;

            case CmosRegisterAddresses.ShutdownStatus:  // 0x0F - Shutdown status byte
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

    /// <summary>
    /// Handles reads from port 0x71 (data register).
    /// Returns the value of the currently selected register.
    /// Time/date registers return current system time in BCD or binary format.
    /// </summary>
    private byte HandleDataPortRead() {
        byte reg = _cmosRegisters.CurrentRegister;
        if (reg > 0x3F) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("CMOS: Read from illegal register {Reg:X2}", reg);
            }
            return 0xFF;
        }

        DateTime now = _clock.CurrentDateTime;
        switch (reg) {
            case CmosRegisterAddresses.Seconds: return EncodeTimeComponent(now.Second);
            case CmosRegisterAddresses.Minutes: return EncodeTimeComponent(now.Minute);
            case CmosRegisterAddresses.Hours: return EncodeTimeComponent(now.Hour);
            case CmosRegisterAddresses.DayOfWeek: return EncodeTimeComponent(((int)now.DayOfWeek + 1));
            case CmosRegisterAddresses.DayOfMonth: return EncodeTimeComponent(now.Day);
            case CmosRegisterAddresses.Month: return EncodeTimeComponent(now.Month);
            case CmosRegisterAddresses.Year: return EncodeTimeComponent(now.Year % 100);
            case CmosRegisterAddresses.Century: return EncodeTimeComponent(now.Year / 100);

            case 0x01:
            case 0x03:
            case 0x05:
                return _cmosRegisters[reg];

            case CmosRegisterAddresses.StatusRegisterA: {
                    byte baseA = (byte)(_cmosRegisters[reg] & 0x7F);
                    return IsUpdateInProgress(now) ? (byte)(baseA | 0x80) : baseA;
                }

            case CmosRegisterAddresses.StatusRegisterC:
                return ReadStatusC();

            case CmosRegisterAddresses.StatusRegisterB:
                {
                    byte value = _cmosRegisters[reg];
                    if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                        _loggerService.Information("RTC: Status Register B read: value=0x{Value:X2}, PIE={PIE}",
                            value, (value & 0x40) != 0);
                    }
                    return value;
                }
            case CmosRegisterAddresses.StatusRegisterD:
            case CmosRegisterAddresses.ShutdownStatus:

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

    /// <summary>
    /// Reads Status Register C (0x0C).
    /// <para>
    /// This register contains interrupt flags:
    /// - Bit 7: IRQF - Interrupt Request Flag (any IRQ pending)
    /// - Bit 6: PF - Periodic Interrupt Flag
    /// - Bit 5: AF - Alarm Interrupt Flag
    /// - Bit 4: UF - Update-Ended Interrupt Flag
    /// </para>
    /// <para>
    /// Reading this register clears all flags. This is critical for proper
    /// interrupt acknowledgment in DOS programs.
    /// </para>
    /// </summary>
    private byte ReadStatusC() {
        _cmosRegisters.Timer.Acknowledged = true;

        if (_cmosRegisters.Timer.Enabled) {
            byte latched = _cmosRegisters[CmosRegisterAddresses.StatusRegisterC];
            _cmosRegisters[CmosRegisterAddresses.StatusRegisterC] = 0;
            return latched;
        }

        double nowMs = _clock.CurrentTimeMs;
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

    /// <summary>
    /// Recalculates the periodic interrupt delay based on the rate divider.
    /// <para>
    /// The MC146818 uses a 32.768 kHz time base. The rate bits (0-3) in Register A
    /// select a divider to generate periodic interrupts at various rates:
    /// - Rate 0 = disabled
    /// - Rate 3-15 = 32768 Hz / (2^(rate-1))
    /// </para>
    /// <para>
    /// Common rates:
    /// - 0x06 = 1024 Hz (1.953 ms period)
    /// - 0x0A = 64 Hz (15.625 ms period)
    /// - 0x0F = 2 Hz (500 ms period)
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Schedules the next periodic interrupt event using EmulationLoopScheduler.
    /// </summary>
    private void ScheduleNextPeriodicInterrupt() {
        if (_cmosRegisters.Timer.Delay <= 0) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("RTC: Cannot schedule periodic interrupt - delay is {Delay}", _cmosRegisters.Timer.Delay);
            }
            return;
        }
        _scheduler.AddEvent(OnPeriodicInterrupt, _cmosRegisters.Timer.Delay, 0);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("RTC: Next periodic interrupt scheduled (delay={Delay:F3}ms) via scheduler", _cmosRegisters.Timer.Delay);
        }
    }

    /// <summary>
    /// Cancels all pending periodic interrupt events from the scheduler.
    /// </summary>
    private void CancelPeriodicInterrupts() {
        _scheduler.RemoveEvents(OnPeriodicInterrupt);
    }
    
    /// <summary>
    /// Callback invoked by EmulationLoopScheduler when periodic interrupt should fire.
    /// Triggers the interrupt and schedules the next one.
    /// </summary>
    /// <param name="value">Controller-supplied value (unused for RTC).</param>
    private void OnPeriodicInterrupt(uint value) {
        if (!_cmosRegisters.Timer.Enabled) {
            return;
        }
        _cmosRegisters[CmosRegisterAddresses.StatusRegisterC] |= 0xC0;
        _cmosRegisters.Timer.Acknowledged = false;
        _cmosRegisters.Last.Timer = _clock.CurrentTimeMs;
        _dualPic.ProcessInterruptRequest(8);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("RTC: Periodic interrupt fired via scheduler, raising IRQ 8");
        }
        ScheduleNextPeriodicInterrupt();
    }

    /// <summary>
    /// Checks if the RTC is currently in an update cycle.
    /// <para>
    /// The MC146818 sets the UIP (Update In Progress) bit in Status Register A
    /// for approximately 2ms while updating time registers. Programs should
    /// poll this bit before reading time to avoid seeing inconsistent values.
    /// </para>
    /// <para>
    /// This implementation approximates the behavior by checking if we're
    /// within 2ms of the next second boundary.
    /// </para>
    /// </summary>
    private bool IsUpdateInProgress(DateTime now) {
        double msInSecond = now.TimeOfDay.TotalMilliseconds % 1000.0;
        return msInSecond >= 998.0 || msInSecond < 2.0;
    }

    /// <summary>
    /// Encodes a time/date component in BCD or binary format.
    /// Format is determined by bit 2 of Status Register B (0x0B).
    /// </summary>
    /// <param name="value">The binary value to encode</param>
    /// <returns>BCD-encoded value if BCD mode is active, otherwise binary value</returns>
    private byte EncodeTimeComponent(int value) =>
        _cmosRegisters.IsBcdMode ? BcdConverter.ToBcd((byte)value) : (byte)value;

    /// <summary>
    /// Validates the 22-stage divider value in Status Register A.
    /// Logs a warning if bits 4-6 don't equal 0x20 (the standard value).
    /// </summary>
    private void ValidateDivider(byte written) {
        if ((written & 0x70) != 0x20 && _loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("CMOS: Illegal 22-stage divider value in Register A: {Val:X2}", written);
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}