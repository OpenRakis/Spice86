namespace Spice86.Core.Emulator.Devices.Timer;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

/// <summary>
///     Simulates the three-channel programmable interval timer, wiring channel 0 to the PIC scheduler and channel 2 to
///     the PC speaker shim while maintaining deterministic behavior.
/// </summary>
public sealed class PitTimer : DefaultIOPortHandler, IDisposable, IPitControl, ITimeMultiplier {
    /// <summary>Underlying clock rate in hertz used by the PIT.</summary>
    public const int PitTickRate = 1193182;

    private const double PitTickRateKhz = PitTickRate / 1000.0;

    private const double PeriodOf1KPitTicks = 1000.0 / PitTickRate;
    private const double BinaryCounterMaxValue = 0xffff; // Highest binary count for 16-bit PIT counters
    private const int ByteMask = 0xff; // Eight-bit mask used by the data port state machine
    private const ushort PitChannel0Port = 0x40; // Read/Write handlers install port 0x40
    private const ushort PitChannel1Port = 0x41; // Read handler installs 0x41 (write handler intentionally omitted)
    private const ushort PitChannel2Port = 0x42; // Read/Write handlers install port 0x42
    private const ushort PitControlPort = 0x43; // Write handler installs port 0x43

    // Zero reloads represent the full binary range (65536 ticks), while BCD mode is capped at 9999.
    private const int MaxBcdCount = 9999;
    private const int MaxDecCount = 0x10000;

    private const byte TimerStatusModeMask = 0x07;
    private readonly IOPortDispatcher _ioPortDispatcher;
    private readonly IPitSpeaker _pcSpeaker;
    private readonly DualPic _pic;
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;

    // Three PIT channels are supported. Each uses the same state machine while addressing distinct peripherals.
    private readonly PitChannel[] _pitChannels = new PitChannel[3];
    private readonly IWallClock _wallClock;

    private bool _isChannel2GateHigh;

    // the timer status cannot be overwritten until it is read or the timer was
    // reprogrammed.
    private byte _latchedTimerStatus;

    private bool _latchedTimerStatusLocked;

    // TODO: Implement time Multiplier
    private double _timeMultiplier = 1.0;

    /// <summary>
    ///     Initializes a new PIT instance and installs I/O handlers for all timer ports.
    /// </summary>
    /// <param name="ioPortDispatcher">I/O dispatcher used to register port handlers.</param>
    /// <param name="state">CPU state.</param>
    /// <param name="pic">Programmable interrupt controller that receives channel 0 callbacks.</param>
    /// <param name="pcSpeaker">Speaker shim that mirrors channel 2 reloads and control words.</param>
    /// <param name="scheduler">The event scheduler.</param>
    /// <param name="clock">The emulated clock.</param>
    /// <param name="loggerService">Logger for trace output.</param>
    /// <param name="failOnUnhandledPort">Whether to throw on unhandled port access.</param>
    public PitTimer(IOPortDispatcher ioPortDispatcher, State state, DualPic pic, IPitSpeaker pcSpeaker, 
        EmulationLoopScheduler scheduler, IEmulatedClock clock, ILoggerService loggerService, bool failOnUnhandledPort)
        : base(state, failOnUnhandledPort, loggerService) {
        _ioPortDispatcher = ioPortDispatcher;
        _pic = pic;
        _pcSpeaker = pcSpeaker;
        _scheduler = scheduler;
        _clock = clock;
        _wallClock = new WallClock();
        SystemStartTime = _wallClock.UtcNow;

        InstallHandlers();
        InitializeChannels();
    }

    /// <summary>
    ///     Gets the UTC timestamp captured when the timer instance was constructed.
    /// </summary>
    public DateTime SystemStartTime { get; }

    // Channel 0
    // ~~~~~~~~~
    // Channel 0 feeds the PIC's IRQ 0 input. BIOS firmware typically loads 0 or
    // 65535 (an effective count of 65536), which yields 18.2065 Hz and one IRQ
    // every 54.9254 ms. The controller asserts interrupts on the rising edge of
    // the channel output and can emit single shots or a continuous tick train.
    private ref PitChannel Channel0 => ref _pitChannels[0];

    // Channel 1
    // ~~~~~~~~~
    // Channel 1 historically teamed with DMA channel 0 to refresh DRAM by
    // generating periodic strobes. Later chipsets replaced this path with
    // dedicated refresh hardware, so most modern systems leave channel 1 idle.
    private ref PitChannel Channel1 => ref _pitChannels[1];

    // Channel 2
    // ~~~~~~~~~
    // Channel 2 drives the PC speaker, so its reload value maps directly to the
    // audible pitch. Software toggles its gate through bit 0 of I/O port 0x61 and
    // can read the output level through bit 5 of the same port.
    private ref PitChannel Channel2 => ref _pitChannels[2];

    private double PicFullIndex => _clock.CurrentTimeMs;

    /// <summary>
    ///     Uninstalls all registered I/O handlers and clears the scheduled channel 0 event.
    /// </summary>
    public void Dispose() {
        _ioPortDispatcher.RemoveIOPortHandler(PitChannel0Port);
        _ioPortDispatcher.RemoveIOPortHandler(PitChannel1Port);
        _ioPortDispatcher.RemoveIOPortHandler(PitChannel2Port);
        _ioPortDispatcher.RemoveIOPortHandler(PitControlPort);

        _scheduler.RemoveEvents(PitChannel0Event);
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        return port switch {
            PitChannel0Port or PitChannel1Port or PitChannel2Port => (byte)ReadLatch(port),
            _ => base.ReadByte(port)
        };
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        switch (port) {
            case PitChannel0Port:
            case PitChannel2Port:
                WriteLatch(port, value);
                break;
            case PitControlPort:
                HandleControlPortWrite(port, value);
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }

    /// <summary>
    ///     Updates the channel 2 gate input and applies the mode-specific bookkeeping used by the timer.
    /// </summary>
    /// <param name="input">New gate level. True represents a high-gate signal.</param>
    /// <remarks>
    ///     Redundant transitions return immediately. Modes 0, 2, and 3 restart counting or sample the latch, mode 1
    ///     reloads the one-shot only on a rising edge, and unsupported modes emit a warning.
    /// </remarks>
    public void SetGate2(bool input) {
        // Skip work when the gate remains unchanged.
        if (_isChannel2GateHigh == input) {
            return;
        }

        ref PitChannel channel = ref Channel2;
        PitMode mode = channel.Mode;

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Gate 2 input changed from {PreviousState} to {CurrentState} in mode {Mode}",
                _isChannel2GateHigh, input, PitModeToString(mode));
        }

        switch (mode) {
            case PitMode.InterruptOnTerminalCount:
                if (input) {
                    channel.Start = PicFullIndex;
                } else {
                    // Capture the current counter value before freezing the channel.
                    CounterLatch(ref channel, 2);
                    channel.Count = channel.ReadLatch;
                }

                break;

            case PitMode.OneShot:
                // Rising gate reloads the counter; falling edges leave the state untouched.
                if (input) {
                    channel.Counting = true;
                    channel.Start = PicFullIndex;
                }

                break;

            case PitMode.RateGenerator:
            case PitMode.RateGeneratorAlias:
            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                // If the gate is enabled, restart counting. If disable store the
                // current read_latch
                if (input) {
                    channel.Start = PicFullIndex;
                } else {
                    CounterLatch(ref channel, 2);
                }

                break;

            case PitMode.SoftwareStrobe:
            case PitMode.HardwareStrobe:
            case PitMode.Inactive:
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("Unsupported gate 2 mode {Mode}", PitModeToString(mode));
                }

                break;
        }

        _isChannel2GateHigh = input;
    }

    /// <summary>
    ///     Reports the current channel 2 output level for consumers that poll the PPI mirror.
    /// </summary>
    /// <returns>True when the OUT signal is high; otherwise, false.</returns>
    public bool IsChannel2OutputHigh() {
        return IsChannelOutputHigh(Channel2);
    }

    /// <summary>
    ///     Adjusts the PIT time multiplier applied to subsequent scheduling operations.
    /// </summary>
    /// <param name="value">Multiplier greater than zero to speed up (&gt; 1) or slow down (&lt; 1) the emulation.</param>
    /// <remarks>Non-positive inputs reset the multiplier to its neutral value (1.0).</remarks>
    public void SetTimeMultiplier(double value) {
        if (value <= 0) {
            _loggerService.Warning("Requested time multiplier {Multiplier} is negative; reverting to 1.0.", value);
            _timeMultiplier = 1.0;
            return;
        }

        _loggerService.Debug("Time multiplier set to {Multiplier}", value);

        _timeMultiplier = value;
    }

    /// <summary>
    ///     Restores all channels to their power-on configuration and reschedules channel 0.
    /// </summary>
    public void Reset() {
        InitializeChannels();
    }

    private void InstallHandlers() {
        _ioPortDispatcher.AddIOPortHandler(PitChannel0Port, this);
        _ioPortDispatcher.AddIOPortHandler(PitChannel1Port, this);
        _ioPortDispatcher.AddIOPortHandler(PitChannel2Port, this);
        _ioPortDispatcher.AddIOPortHandler(PitControlPort, this);
    }

    private static int GetMaxCount(in PitChannel channel) {
        return channel.Bcd ? MaxBcdCount : MaxDecCount;
    }

    private static string PitModeToString(PitMode mode) {
        return mode switch {
            PitMode.InterruptOnTerminalCount => "Interrupt on terminal count",
            PitMode.OneShot => "One-shot",
            PitMode.RateGenerator => "Rate generator",
            PitMode.SquareWave => "Square wave generator",
            PitMode.SoftwareStrobe => "Software-triggered strobe",
            PitMode.HardwareStrobe => "Hardware-triggered strobe",
            PitMode.RateGeneratorAlias => "Rate generator (alias)",
            PitMode.SquareWaveAlias => "Square wave generator (alias)",
            PitMode.Inactive => "Inactive",
            _ => "Unknown"
        };
    }

    private static void UpdateChannelDelay(ref PitChannel channel) {
        // The divider cannot be zero, so a stored zero represents 65536 (or 10000 when programmed for BCD counts).
        int freqDivider = channel.Count != 0 ? channel.Count : GetMaxCount(channel) + 1;

        // The delay calculation is the same regardless of whether InstructionsPerSecond is configured.
        // When InstructionsPerSecond is set, the CyclesAllocated in ExecutionStateSlice will be adjusted accordingly
        // by the CycleLimiterFactory, which ensures that ticks represent instruction-based time rather than
        // wall-clock time. This maintains backward compatibility with the old instruction-based timer model.
        channel.Delay = 1000.0 * freqDivider / PitTickRate;
    }

    private static void SaveReadLatch(ref PitChannel channel, double latchTime) {
        // Latch is a 16-bit counter, wrap it to ensure it doesn't overflow
        int wrapped = (int)Math.Round(latchTime) % ushort.MaxValue;
        channel.ReadLatch = (ushort)wrapped;
    }

    // Handles the scheduled channel 0 callback:
    // - asserts IRQ0 via the PIC, which delivers the timer tick
    // - if channel 0 runs in a periodic mode (anything except Mode 0), advances its start time,
    //   refreshes the cached delay when a new divisor was latched, and enqueues the next callback
    //   at the exact tick interval calculated from update_channel_delay
    // The ignored `value` parameter is preserved for parity with the PIC event signature.
    private void PitChannel0Event(uint value) {
        _pic.ActivateIrq(0);

        if (Channel0.Mode == PitMode.InterruptOnTerminalCount) {
            return;
        }

        Channel0.Start += Channel0.Delay;

        if (Channel0.UpdateCount) {
            UpdateChannelDelay(ref Channel0);
            Channel0.UpdateCount = false;
        }

        _scheduler.AddEvent(PitChannel0Event, Channel0.Delay);
    }

    private bool IsChannelOutputHigh(in PitChannel channel) {
        double index = PicFullIndex - channel.Start;
        switch (channel.Mode) {
            case PitMode.InterruptOnTerminalCount:
                if (channel.ModeChanged) {
                    return false;
                }

                return index > channel.Delay;

            case PitMode.RateGenerator:
            case PitMode.RateGeneratorAlias:
                if (channel.ModeChanged) {
                    return true;
                }

                index %= channel.Delay;
                return index > 0;

            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                if (channel.ModeChanged) {
                    return true;
                }

                index %= channel.Delay;
                return index * 2 < channel.Delay;

            case PitMode.SoftwareStrobe:
                // Only low on terminal count
                //  if(fmod(index,(double)channel.delay) == 0) return false;
                //  Maybe take one rate tick in consideration
                // The Easiest solution is to report always high (Space Marines uses this mode)
                return true;

            case PitMode.OneShot:
            case PitMode.HardwareStrobe:
            case PitMode.Inactive:
            default:
                // Modes 1 and 5 depend on external gating, and mode 7 is unused, so treat them as illegal in this read path.
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("Illegal Mode {Mode} for reading output", PitModeToString(channel.Mode));
                }

                return true;
        }
    }

    // Captures the PIT status word when the control port issues a read-back command. The status byte is populated once
    // until either the CPU reads it or a new control word unlocks the latch.
    //   - bit 0 reflects BCD mode
    //   - bits 1–3 report the current mode
    //   - bits 4–5 encode the read/load access mode
    //   - bit 6 is set if the counter output transitioned since the last load
    //   - bit 7 mirrors the instantaneous OUT pin level
    // The latch is then marked as pending (`CounterStatusSet`) and locked to prevent recalculation until the status byte
    // is consumed.
    private void StatusLatch(ref PitChannel channel) {
        // the timer status cannot be overwritten until it is read or the timer was reprogrammed.
        if (_latchedTimerStatusLocked) {
            return;
        }

        _latchedTimerStatus = 0;

        if (channel.Bcd) {
            _latchedTimerStatus |= (byte)TimerStatusFlags.Bcd;
        }

        _latchedTimerStatus |= (byte)(((byte)channel.Mode & TimerStatusModeMask) << 1);

        switch (channel.ReadMode) {
            case AccessMode.Latch or AccessMode.Both:
                _latchedTimerStatus |= (byte)(TimerStatusFlags.AccessLow | TimerStatusFlags.AccessHigh);
                break;
            case AccessMode.Low:
                _latchedTimerStatus |= (byte)TimerStatusFlags.AccessLow;
                break;
            case AccessMode.High:
                _latchedTimerStatus |= (byte)TimerStatusFlags.AccessHigh;
                break;
        }

        if (IsChannelOutputHigh(channel)) {
            _latchedTimerStatus |= (byte)TimerStatusFlags.Output;
        }

        if (channel.ModeChanged) {
            _latchedTimerStatus |= (byte)TimerStatusFlags.NullCount;
        }

        channel.CounterStatusSet = true;
        _latchedTimerStatusLocked = true;
    }

    // Responds to a read command by capturing the channel's current countdown value into the read latch. Control flow
    // follows the same rules as the device:
    //   - channel 2 ignores latch requests while its gate is low, except in one-shot mode.
    //   - elapsed time is measured against `PicFullIndex`; if a mode change is pending, the cached latch is reduced by
    //     the elapsed tick count.
    //   - otherwise, each PIT mode applies its specific formula (including BCD arithmetic) before `SaveReadLatch`
    //     commits the wrapped 16-bit value.
    private void CounterLatch(ref PitChannel channel, int channelIndex) {
        channel.GoReadLatch = false;

        // Skip updates when channel 2's gate input is low (except in one-shot mode).
        if (channelIndex == 2 && !_isChannel2GateHigh &&
            channel.Mode != PitMode.OneShot) {
            return;
        }

        // Fill the read_latch of the selected counter with the current count
        double elapsedMs = PicFullIndex - channel.Start;

        if (channel.ModeChanged) {
            double elapsedTicks = elapsedMs * PitTickRateKhz;
            // Ensure the remaining ticks aren't negative
            double remainingTicks = Math.Max(0.0, channel.ReadLatch - elapsedTicks);
            SaveReadLatch(ref channel, remainingTicks);
            return;
        }

        double count = channel.Count;

        switch (channel.Mode) {
            case PitMode.SoftwareStrobe:
            case PitMode.InterruptOnTerminalCount:
                if (elapsedMs > channel.Delay) {
                    // Counter keeps on counting after passing terminal count
                    elapsedMs -= channel.Delay;
                    if (channel.Bcd) {
                        elapsedMs %= PeriodOf1KPitTicks * 10000.0;
                        SaveReadLatch(ref channel, MaxBcdCount - (elapsedMs * PitTickRateKhz));
                    } else {
                        elapsedMs %= PeriodOf1KPitTicks * MaxDecCount;
                        SaveReadLatch(ref channel, BinaryCounterMaxValue - (elapsedMs * PitTickRateKhz));
                    }
                } else {
                    SaveReadLatch(ref channel, count - (elapsedMs * PitTickRateKhz));
                }

                break;

            case PitMode.OneShot:
                if (channel.Counting) {
                    if (elapsedMs > channel.Delay) {
                        SaveReadLatch(ref channel, BinaryCounterMaxValue);
                    } else {
                        SaveReadLatch(ref channel, count - (elapsedMs * PitTickRateKhz));
                    }
                }

                break;

            case PitMode.RateGenerator:
            case PitMode.RateGeneratorAlias:
                elapsedMs %= channel.Delay;
                SaveReadLatch(ref channel, count - (elapsedMs / channel.Delay * count));
                break;

            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                elapsedMs %= channel.Delay;
                // if (channel.mode== PitMode::SquareWave) ticks_since_then /= 2
                // TODO figure this out on real hardware
                elapsedMs *= 2;
                if (elapsedMs > channel.Delay) {
                    elapsedMs -= channel.Delay;
                }

                SaveReadLatch(ref channel, count - (elapsedMs / channel.Delay * count));
                // In mode 3 it never returns odd numbers LSB (if odd number is
                // written, 1 will be subtracted on the first clock and then always
                // 2) fixes "Corncob 3D"
                channel.ReadLatch &= 0xfffe;
                break;

            case PitMode.HardwareStrobe:
            case PitMode.Inactive:
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("Illegal Mode {Mode} for reading counter {Counter}",
                        PitModeToString(channel.Mode), count);
                }

                SaveReadLatch(ref channel, BinaryCounterMaxValue);
                break;
        }
    }

    // Handles byte writes to the PIT channel data ports. The write path round-trips through BCD when the channel is in
    // decimal mode, updates the staged latch according to the access mode state machine, and once a full word has been
    // loaded, it refreshes the running counter, reschedules IRQ0 events, and notifies the PC speaker.
    private void WriteLatch(ushort port, uint value) {
        // write_latch is 16-bits
        byte val = (byte)value;
        byte channelNum = (byte)(port - PitChannel0Port);

        ref PitChannel channel = ref _pitChannels[channelNum];

        if (channel.Bcd) {
            channel.WriteLatch = NumericConverters.DecimalToBcd(channel.WriteLatch);
        }

        switch (channel.WriteMode) {
            case AccessMode.Latch:
                channel.WriteLatch =
                    (ushort)((channel.WriteLatch & 0x00FF) | ((val & ByteMask) << 8));
                channel.WriteMode = AccessMode.Both;
                break;

            case AccessMode.Both:
                channel.WriteLatch =
                    (ushort)((channel.WriteLatch & 0xFF00) | (val & ByteMask));
                channel.WriteMode = AccessMode.Latch;
                break;

            case AccessMode.Low:
                channel.WriteLatch =
                    (ushort)((channel.WriteLatch & 0xFF00) | (val & ByteMask));
                break;

            case AccessMode.High:
                channel.WriteLatch =
                    (ushort)((channel.WriteLatch & 0x00FF) | ((val & ByteMask) << 8));
                break;
            default:
                throw new InvalidOperationException(
                    $"Illegal write mode: {(byte)channel.WriteMode} ({channel.WriteMode})");
        }

        if (channel.Bcd) {
            channel.WriteLatch = NumericConverters.BcdToDecimal(channel.WriteLatch);
        }

        if (channel.WriteMode == AccessMode.Latch) {
            return;
        }

        channel.Count = channel.WriteLatch != 0 ? channel.WriteLatch : GetMaxCount(channel);

        if (channel is { ModeChanged: false, Mode: PitMode.RateGenerator or PitMode.RateGeneratorAlias } &&
            channelNum == 0) {
            // Mode 2 delays new reloads until the active cycle completes. Channel 2 still applies updates immediately.
            channel.UpdateCount = true;
            return;
        }

        channel.Start = PicFullIndex;
        UpdateChannelDelay(ref channel);

        switch (channelNum) {
            case 0:
                if (channel.ModeChanged || channel.Mode == PitMode.InterruptOnTerminalCount) {
                    if (channel.Mode == PitMode.InterruptOnTerminalCount) {
                        _scheduler.RemoveEvents(PitChannel0Event);
                    }

                    _scheduler.AddEvent(PitChannel0Event, channel.Delay);
                } else if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                    _loggerService.Information("PIT 0 Timer set without new control word");
                }

                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    double frequency = 1000.0 / channel.Delay;
                    string pitMode = PitModeToString(channel.Mode);
                    _loggerService.Verbose("PIT 0 Timer at {Frequency:F4} Hz {PitMode}", frequency, pitMode);
                }

                break;

            case 2:
                _pcSpeaker.SetCounter(channel.Count, channel.Mode);
                break;

            default:
                _loggerService.Error("PIT: Illegal timer selected for writing: {Channel}", channelNum);
                break;
        }

        channel.ModeChanged = false;
    }

    // Services byte reads from the PIT channel data ports. If a pending status byte was latched, it is returned first.
    // Otherwise, the method engages the two-step read state machine, including BCD conversions and access-mode
    // transitions, before yielding the latched counter value.
    private uint ReadLatch(ushort port) {
        byte channelNum = (byte)(port - PitChannel0Port);
        ref PitChannel channel = ref _pitChannels[channelNum];

        // The first thing that is being read from this counter now is
        // the counter status.
        byte ret;

        // Timer is being reprogrammed, unlock the status
        if (channel.CounterStatusSet) {
            channel.CounterStatusSet = false;
            _latchedTimerStatusLocked = false;
            ret = _latchedTimerStatus;
        } else {
            if (channel.GoReadLatch) {
                CounterLatch(ref channel, channelNum);
            }

            if (channel.Bcd) {
                channel.ReadLatch = NumericConverters.DecimalToBcd(channel.ReadLatch);
            }

            switch (channel.ReadMode) {
                case AccessMode.Latch:
                    // read MSB & return to state 3
                    ret = (byte)((channel.ReadLatch >> 8) & ByteMask);
                    channel.ReadMode = AccessMode.Both;
                    channel.GoReadLatch = true;
                    break;

                case AccessMode.Low:
                    // read LSB
                    ret = (byte)(channel.ReadLatch & ByteMask);
                    channel.GoReadLatch = true;
                    break;

                case AccessMode.High:
                    // read MSB
                    ret = (byte)((channel.ReadLatch >> 8) & ByteMask);
                    channel.GoReadLatch = true;
                    break;

                case AccessMode.Both:
                    // read LSB followed by MSB
                    ret = (byte)(channel.ReadLatch & ByteMask);
                    channel.ReadMode = AccessMode.Latch;
                    break;

                default:
                    throw new InvalidOperationException($"{nameof(PitTimer)} - error in readlatch");
            }

            if (channel.Bcd) {
                channel.ReadLatch = NumericConverters.BcdToDecimal(channel.ReadLatch);
            }
        }

        return ret;
    }

    // Processes read-back commands targeting a single counter. The decoded `ReadBackStatus` controls whether the counter
    // value is latched, the status byte is assembled, and the access state machine is reset, while reproducing the side
    // effects on IRQ0 and the speaker when the mode changes.
    private void LatchSingleChannel(byte channelNum, byte val) {
        ref PitChannel channel = ref _pitChannels[channelNum];

        var rbs = new ReadBackStatus { Data = val };

        if (rbs.AccessModeNone) {
            CounterLatch(ref channel, channelNum);
            return;
        }

        // Save output status to be used with timer 0 irq
        bool oldOutput = IsChannelOutputHigh(Channel0);
        // Save the current count value to be re-used in undocumented newmode
        CounterLatch(ref channel, channelNum);

        channel.Bcd = rbs.BcdState;

        if (channel.Bcd) {
            channel.Count = Math.Min(channel.Count, MaxBcdCount);
        }

        if (channel.CounterStatusSet) {
            channel.CounterStatusSet = false;
            _latchedTimerStatusLocked = false;
        }

        channel.Start = PicFullIndex;
        channel.GoReadLatch = true;
        channel.UpdateCount = false;
        channel.Counting = false;
        channel.ReadMode = channel.WriteMode = rbs.AccessMode;
        channel.Mode = rbs.PitMode;

        switch (channelNum) {
            // A rising edge asserts IRQ0 and the line remains high until the CPU acknowledges it. Mode 0 initializes low,
            // so the interrupt is cleared immediately, while modes 2 and 3 start high. The prior output level guards the
            // edge detection, so only low-to-high transitions trigger the activation path.
            case 0: {
                _scheduler.RemoveEvents(PitChannel0Event);
                if (channel.Mode != PitMode.InterruptOnTerminalCount && !oldOutput) {
                    _pic.ActivateIrq(0);
                } else {
                    _pic.DeactivateIrq(0);
                }

                break;
            }
            case 2:
                _pcSpeaker.SetCounter(0, PitMode.SquareWave);
                break;
        }

        channel.ModeChanged = true;

        if (channelNum == 2) {
            // Inform the speaker shim that the control word was written.
            _pcSpeaker.SetPitControl(channel.Mode);
        }
    }

    // Executes a read-back command that simultaneously latches multiple counters. Channel selection and suppression
    // flags are evaluated in priority order so count and status latches are captured consistently.
    private void LatchAllChannels(byte val) {
        var flags = (ReadBackFlags)val;

        // Latch multiple pit counters
        if ((flags & ReadBackFlags.SuppressCountLatch) == 0) {
            if ((flags & ReadBackFlags.Counter0) != 0) {
                CounterLatch(ref Channel0, 0);
            }

            if ((flags & ReadBackFlags.Counter1) != 0) {
                CounterLatch(ref Channel1, 1);
            }

            if ((flags & ReadBackFlags.Counter2) != 0) {
                CounterLatch(ref Channel2, 2);
            }
        }

        if ((flags & ReadBackFlags.SuppressStatusLatch) != 0) {
            return;
        }

        // Status and values can be latched simultaneously
        if ((flags & ReadBackFlags.Counter0) != 0) {
            // Latch status words
            StatusLatch(ref Channel0);
        } else if ((flags & ReadBackFlags.Counter1) != 0) {
            // but only 1 status can be latched simultaneously
            StatusLatch(ref Channel1);
        } else if ((flags & ReadBackFlags.Counter2) != 0) {
            StatusLatch(ref Channel2);
        }
    }

    // Decodes writes to the PIT control port. The command byte selects either a specific channel (handled by
    // `LatchSingleChannel`) or the aggregate read-back path (`LatchAllChannels`).
    private void HandleControlPortWrite(ushort port, uint value) {
        byte val = (byte)value;
        byte channelNum = (byte)((val >> 6) & 0x03);

        if (channelNum < 3) {
            LatchSingleChannel(channelNum, val);
        } else {
            LatchAllChannels(val);
        }
    }

    private void InitializeChannels() {
        _scheduler.RemoveEvents(PitChannel0Event);

        Channel0.Bcd = false;
        Channel0.Count = GetMaxCount(Channel0);
        Channel0.ReadLatch = 0;
        Channel0.WriteLatch = 0;
        Channel0.ReadMode = AccessMode.Both;
        Channel0.WriteMode = AccessMode.Both;
        Channel0.Mode = PitMode.SquareWave;
        Channel0.GoReadLatch = true;
        Channel0.CounterStatusSet = false;
        Channel0.UpdateCount = false;

        Channel1.Bcd = false;
        Channel1.Count = 18;
        Channel1.ReadMode = AccessMode.Low;
        Channel1.WriteMode = AccessMode.Both;
        Channel1.Mode = PitMode.RateGenerator;
        Channel1.GoReadLatch = true;
        Channel1.CounterStatusSet = false;

        Channel2.Bcd = false;
        Channel2.Count = 1320;
        Channel2.ReadLatch = 1320;
        Channel2.ReadMode = AccessMode.Both;
        Channel2.WriteMode = AccessMode.Both;
        Channel2.Mode = PitMode.SquareWave;
        Channel2.GoReadLatch = true;
        Channel2.CounterStatusSet = false;
        Channel2.Counting = false;

        UpdateChannelDelay(ref Channel0);
        UpdateChannelDelay(ref Channel1);
        UpdateChannelDelay(ref Channel2);

        _latchedTimerStatusLocked = false;
        _isChannel2GateHigh = false;
        _scheduler.AddEvent(PitChannel0Event, Channel0.Delay);
    }

    /// <summary>
    ///     Captures an immutable snapshot of the specified channel's internal state.
    /// </summary>
    /// <param name="index">Channel index in the range 0 to 2.</param>
    /// <returns>Snapshot describing the channel at the current scheduler index.</returns>
    public PitChannelSnapshot GetChannelSnapshot(int index) {
        ref PitChannel channel = ref _pitChannels[index];
        return new PitChannelSnapshot(channel.Count,
            channel.Delay,
            channel.Start,
            channel.ReadLatch,
            channel.WriteLatch,
            channel.Mode,
            channel.Bcd,
            channel.Counting,
            channel.ModeChanged,
            channel.GoReadLatch);
    }

    /// <summary>
    ///     Gets the number of milliseconds that have elapsed since <see cref="SystemStartTime" />.
    /// </summary>
    /// <returns>Elapsed milliseconds.</returns>
    public long GetTicks() {
        return (long)(_wallClock.UtcNow - SystemStartTime).TotalMilliseconds;
    }

    /// <summary>
    ///     Gets the number of microseconds that have elapsed since <see cref="SystemStartTime" />.
    /// </summary>
    /// <returns>Elapsed microseconds.</returns>
    public long GetTicksUs() {
        return (long)((_wallClock.UtcNow - SystemStartTime).TotalMilliseconds * 1000.0);
    }

    /// <summary>
    ///     Computes the difference between two monotonic tick readings.
    /// </summary>
    /// <param name="newTicks">Later tick value.</param>
    /// <param name="oldTicks">Earlier tick value.</param>
    /// <returns>Difference in ticks.</returns>
    public static long GetTicksDiff(long newTicks, long oldTicks) {
        Debug.Assert(newTicks >= oldTicks);
        return newTicks - oldTicks;
    }

    // Many subsystems (focus management, overlay logging, IPX networking, serial timeouts, MIDI SysEx pacing) call
    // GetTicksSince to benchmark elapsed milliseconds.
    /// <summary>
    ///     Calculates elapsed milliseconds relative to a prior <see cref="GetTicks" /> reading.
    /// </summary>
    /// <param name="oldTicks">Reference tick value in milliseconds.</param>
    /// <returns>Elapsed milliseconds.</returns>
    public long GetTicksSince(long oldTicks) {
        long now = GetTicks();
        return GetTicksDiff(now, oldTicks);
    }

    // The main timing loop tracks microsecond sleep durations with GetTicksUsSince to refine the scheduler.
    /// <summary>
    ///     Calculates elapsed microseconds relative to a prior <see cref="GetTicksUs" /> reading.
    /// </summary>
    /// <param name="oldTicks">Reference tick value in microseconds.</param>
    /// <returns>Elapsed microseconds.</returns>
    public long GetTicksUsSince(long oldTicks) {
        long now = GetTicksUs();
        return GetTicksDiff(now, oldTicks);
    }

    [Flags]
    private enum TimerStatusFlags : byte {
        Bcd = 0x01,
        AccessLow = 0x10,
        AccessHigh = 0x20,
        NullCount = 0x40,
        Output = 0x80
    }

    [Flags]
    private enum ReadBackFlags : byte {
        Counter0 = 0x02,
        Counter1 = 0x04,
        Counter2 = 0x08,
        SuppressStatusLatch = 0x10,
        SuppressCountLatch = 0x20
    }
}
