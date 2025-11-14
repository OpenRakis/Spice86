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
/// Emulates the MC146818 Real Time Clock (RTC) and CMOS RAM.
/// <para>
/// The MC146818 chip provides:
/// - Real-time clock with date/time in BCD or binary format
/// - 64 bytes of battery-backed CMOS RAM (registers 0x00-0x3F)
/// - Periodic interrupt capability (IRQ 8)
/// - Alarm functionality
/// - Update-in-progress flag for time reads
/// </para>
/// <para>
/// I/O Ports:
/// - 0x70: Index/address register (write-only, bit 7 = NMI disable)
/// - 0x71: Data register (read/write based on selected index)
/// </para>
/// <para>
/// Key Registers:
/// - 0x00-0x09: Time/date registers (seconds, minutes, hours, day, month, year)
/// - 0x0A: Status Register A (UIP bit, periodic rate selection)
/// - 0x0B: Status Register B (format control, interrupt enables)
/// - 0x0C: Status Register C (interrupt flags, read clears)
/// - 0x0D: Status Register D (valid RAM and battery status)
/// - 0x0F: Shutdown status byte
/// - 0x10+: CMOS configuration data (floppy types, HD info, memory size)
/// </para>
/// <para>
/// This implementation processes periodic events lazily on port access.
/// Paused time (via IPauseHandler) does not advance RTC timing.
/// </para>
/// <para>
/// Reference: DOSBox Staging CMOS implementation, MC146818 datasheet
/// </para>
/// <para>
/// <b>Known deviations and simplifications:</b>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>UIP (Update In Progress) timing:</b> The UIP flag is set/cleared based on elapsed real time, but timing is approximate and not cycle-accurate as on real hardware.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Alarm functionality:</b> Alarm registers are stored but alarm interrupts are not implemented; alarm events are not generated.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>CMOS configuration registers:</b> Only a subset of configuration registers are implemented; many are stubbed or return default values as in DOSBox Staging. Unimplemented registers may return 0 or fixed values.
/// </description>
/// </item>
/// </list>
/// </para>
/// </summary>
public sealed class RealTimeClock : DefaultIOPortHandler, IDisposable {
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

    /// <summary>
    /// Initializes the RTC/CMOS device with default register values.
    /// </summary>
    /// <param name="state">CPU state for I/O operations</param>
    /// <param name="ioPortDispatcher">I/O port dispatcher for registering handlers</param>
    /// <param name="dualPic">PIC for triggering IRQ 8 (periodic timer interrupt)</param>
    /// <param name="pauseHandler">Handler for emulator pause/resume events</param>
    /// <param name="failOnUnhandledPort">Whether to fail on unhandled I/O port access</param>
    /// <param name="loggerService">Logger service for diagnostics</param>
    public RealTimeClock(State state, IOPortDispatcher ioPortDispatcher, DualPic dualPic,
        IPauseHandler pauseHandler, bool failOnUnhandledPort, ILoggerService loggerService)
        : base(state, failOnUnhandledPort, loggerService) {
        _dualPic = dualPic;
        _pauseHandler = pauseHandler;

        // Subscribe to pause lifecycle events to keep timing exact.
        _pauseHandler.Pausing += OnPausing;
        _pauseHandler.Paused += OnPaused;
        _pauseHandler.Resumed += OnResumed;

        // Initialize RTC control registers with defaults matching DOSBox/real hardware
        _cmosRegisters[CmosRegisterAddresses.StatusRegisterA] = 0x26;  // Default rate (1024 Hz) + 22-stage divider
        _cmosRegisters[CmosRegisterAddresses.StatusRegisterB] = 0x02;  // 24-hour mode, no interrupts
        _cmosRegisters[CmosRegisterAddresses.StatusRegisterD] = 0x80;  // Valid RAM + battery good
        
        // Initialize BCD mode based on Status Register B (bit 2: 0=BCD, 1=binary)
        // Default 0x02 has bit 2 clear, so BCD mode is enabled
        _cmosRegisters.IsBcdMode = (_cmosRegisters[CmosRegisterAddresses.StatusRegisterB] & 0x04) == 0;
        
        // Initialize CMOS RAM with base memory size.
        // Base memory in KB: 640 (0x0280), stored as little-endian low/high bytes (0x80 at 0x15, 0x02 at 0x16)
        _cmosRegisters[0x15] = 0x80;  // Low byte of 0x0280 (base memory in KB)
        _cmosRegisters[0x16] = 0x02;  // High byte of 0x0280 (base memory in KB)

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
        // Process any pending periodic timer events before handling the write
        if (port == CmosPorts.Address || port == CmosPorts.Data) {
            ProcessPendingPeriodicEvents();
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
            ProcessPendingPeriodicEvents();
            return HandleDataPortRead();
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
            // Time/date registers - store values (allows DOS/BIOS to set date/time)
            // Note: Reads still return current host system time, not these stored values
            case CmosRegisterAddresses.Seconds:      // 0x00
            case CmosRegisterAddresses.Minutes:      // 0x02
            case CmosRegisterAddresses.Hours:        // 0x04
            case CmosRegisterAddresses.DayOfWeek:    // 0x06
            case CmosRegisterAddresses.DayOfMonth:   // 0x07
            case CmosRegisterAddresses.Month:        // 0x08
            case CmosRegisterAddresses.Year:         // 0x09
            case CmosRegisterAddresses.Century:      // 0x32
                _cmosRegisters[reg] = value;
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("CMOS: Time/date register {Reg:X2} set to {Val:X2} (stored but not used for time reads)", reg, value);
                }
                return;

            // Alarm registers - store but don't implement alarm functionality
            case 0x01:  // Seconds alarm
            case 0x03:  // Minutes alarm
            case 0x05:  // Hours alarm
                _cmosRegisters[reg] = value;
                if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                    _loggerService.Information("CMOS: Alarm register {Reg:X2} set to {Val:X2}", reg, value);
                }
                return;

            case CmosRegisterAddresses.StatusRegisterA:  // 0x0A - Status Register A (rate/divider control)
                _cmosRegisters[reg] = (byte)(value & 0x7F);
                byte newDiv = (byte)(value & 0x0F);
                // DOSBox compatibility: adjust divider values 0-2
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
                    ScheduleNextPeriodic();
                }
                return;

            case CmosRegisterAddresses.StatusRegisterD:  // 0x0D - Status Register D (battery status)
                _cmosRegisters[reg] = (byte)(value & 0x80);  // Bit 7 = RTC power on
                return;

            case CmosRegisterAddresses.ShutdownStatus:  // 0x0F - Shutdown status byte
                _cmosRegisters[reg] = (byte)(value & 0x7F);
                return;

            default:
                // Other registers - store value in CMOS RAM
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

        DateTime now = DateTime.Now;
        switch (reg) {
            // Time registers - return current system time
            case CmosRegisterAddresses.Seconds:      return EncodeTimeComponent(now.Second);
            case CmosRegisterAddresses.Minutes:      return EncodeTimeComponent(now.Minute);
            case CmosRegisterAddresses.Hours:        return EncodeTimeComponent(now.Hour);
            case CmosRegisterAddresses.DayOfWeek:    return EncodeTimeComponent(((int)now.DayOfWeek + 1));
            case CmosRegisterAddresses.DayOfMonth:   return EncodeTimeComponent(now.Day);
            case CmosRegisterAddresses.Month:        return EncodeTimeComponent(now.Month);
            case CmosRegisterAddresses.Year:         return EncodeTimeComponent(now.Year % 100);
            case CmosRegisterAddresses.Century:      return EncodeTimeComponent(now.Year / 100);
            
            // Alarm registers
            case 0x01:  // Seconds alarm
            case 0x03:  // Minutes alarm
            case 0x05:  // Hours alarm
                return _cmosRegisters[reg];
                
            case CmosRegisterAddresses.StatusRegisterA: {  // 0x0A - Status Register A
                // Bit 7 = Update In Progress (UIP) - set during time update cycle
                byte baseA = (byte)(_cmosRegisters[reg] & 0x7F);
                return IsUpdateInProgress(now) ? (byte)(baseA | 0x80) : baseA;
            }
            
            case CmosRegisterAddresses.StatusRegisterC:  // 0x0C - Status Register C (interrupt flags, read clears)
                return ReadStatusC();
                
            // Control and status registers
            case CmosRegisterAddresses.StatusRegisterB:              // 0x0B - Status Register B
            case CmosRegisterAddresses.StatusRegisterD:              // 0x0D - Status Register D
            case CmosRegisterAddresses.ShutdownStatus: // 0x0F - Shutdown status
            
            // CMOS configuration registers
            case 0x14:  // Equipment byte
            case 0x15:  // Base memory low byte
            case 0x16:  // Base memory high byte
            case 0x17:  // Extended memory low byte
            case 0x18:  // Extended memory high byte
            case 0x30:  // Extended memory low byte (alternate)
            case 0x31:  // Extended memory high byte (alternate)
                return _cmosRegisters[reg];
                
            default:
                // Other CMOS RAM locations
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
            // In periodic interrupt mode, return and clear latched flags
            byte latched = _cmosRegisters[CmosRegisterAddresses.StatusRegisterC];
            _cmosRegisters[CmosRegisterAddresses.StatusRegisterC] = 0;
            return latched;
        }

        // Generate flags based on elapsed time when not in periodic mode
        double nowMs = GetElapsedMilliseconds();
        byte value = 0;

        // Periodic interrupt flag (bit 6)
        if (nowMs >= (_cmosRegisters.Last.Timer + _cmosRegisters.Timer.Delay)) {
            _cmosRegisters.Last.Timer = nowMs;
            value |= 0x40;
        }
        
        // Update-ended interrupt flag (bit 4)
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
    /// Schedules the next periodic interrupt event.
    /// Aligns the trigger time to a multiple of the period to maintain consistent timing.
    /// </summary>
    private void ScheduleNextPeriodic() {
        if (_cmosRegisters.Timer.Delay <= 0) {
            return;
        }
        double nowMs = GetElapsedMilliseconds();
        double rem = nowMs % _cmosRegisters.Timer.Delay;
        _nextPeriodicTriggerMs = nowMs + (_cmosRegisters.Timer.Delay - rem);
    }

    /// <summary>
    /// Processes pending periodic timer events.
    /// <para>
    /// This method is called lazily on I/O port access rather than using
    /// real-time callbacks. When a periodic event is due:
    /// 1. Sets interrupt flags in Status Register C (0xC0 = IRQF + PF)
    /// 2. Triggers IRQ 8 via the PIC
    /// 3. Schedules the next event
    /// </para>
    /// <para>
    /// Note: Contraption Zack (music) relies on the 0xC0 flag pattern.
    /// </para>
    /// </summary>
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
            // 0xC0 = IRQF (bit 7) + PF (bit 6) - both flags must be set for games like Contraption Zack to detect periodic timer events correctly.
            // Contraption Zack (music) relies on both IRQF and PF being set in Status Register C to process timer interrupts.
            _cmosRegisters[CmosRegisterAddresses.StatusRegisterC] |= 0xC0;
            _cmosRegisters.Timer.Acknowledged = false;
            _cmosRegisters.Last.Timer = nowMs;
            while (nowMs >= _nextPeriodicTriggerMs) {
                _nextPeriodicTriggerMs += _cmosRegisters.Timer.Delay;
            }
            _dualPic.ProcessInterruptRequest(8);
        }
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
    /// within the first 2ms of a second boundary.
    /// </para>
    /// </summary>
    private bool IsUpdateInProgress(DateTime now) {
        double fractional = (now.TimeOfDay.TotalMilliseconds % 1000.0) / 1000.0;
        return fractional < 0.002;
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
    /// Gets elapsed time in milliseconds since RTC initialization, excluding paused time.
    /// <para>
    /// Uses high-resolution Stopwatch for accurate timing. Pause events are tracked
    /// to ensure that emulator pause time doesn't advance RTC state.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Validates the 22-stage divider value in Status Register A.
    /// Logs a warning if bits 4-6 don't equal 0x20 (the standard value).
    /// </summary>
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