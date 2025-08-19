using Spice86.Shared.Interfaces;

using System.Diagnostics;

namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
/// Represents a Programmable Interval Timer (PIT) counter in an IBM PC. <br/>
/// The PIT is a chip connected to the CPU and is used to generate accurate time delays under software control. <br/>
/// Each counter can be programmed to operate in one of six modes and can count up or down.
/// </summary>
public class Pit8254Counter {
    /// <summary>
    /// Equals to 1.193182 MHz
    /// </summary>
    public const long HardwareFrequency = 1193182;
    /// <summary>
    /// Milliseconds per PIT tick
    /// </summary>
    public const float MsPerPitTick = 1000.0f / HardwareFrequency;
    /// <summary>
    /// Duration of a single PIT clock pulse in nanoseconds
    /// </summary>
    public const double PitClockPulseNs = 1_000_000_000.0 / HardwareFrequency; // ~838 nanoseconds

    private readonly ILoggerService _loggerService;

    private bool _latchMode = false;
    private readonly Pit8254Register _latchRegister = new();
    private readonly Pit8254Register _currentCountRegister = new();
    private readonly Pit8254Register _reloadValueRegister = new();

    // Track pulse timing
    private bool _inPulseState;
    private long _pulseStartTimestamp;
    private readonly Stopwatch _pulseTimer = Stopwatch.StartNew();

    /// <summary>
    /// PIT operation modes as defined in Intel 8254 datasheet section 3.3
    /// </summary>
    public enum PitMode : byte {
        /// <summary>
        /// Mode 0: Interrupt on Terminal Count
        /// Output starts low, goes high when counter reaches zero
        /// Counting stops when terminal count is reached
        /// Reference: Intel 8254 Datasheet, Mode 0 Operation
        /// </summary>
        InterruptOnTerminalCount = 0,

        /// <summary>
        /// Mode 1: Programmable One-Shot
        /// Output goes low on gate rising edge, goes high when terminal count is reached
        /// Counter is retriggerable
        /// Reference: Intel 8254 Datasheet, Mode 1 Operation
        /// </summary>
        OneShot = 1,

        /// <summary>
        /// Mode 2: Rate Generator
        /// Output stays high except for one clock cycle before terminal count
        /// Counter automatically reloads after reaching zero
        /// Reference: Intel 8254 Datasheet, Mode 2 Operation
        /// </summary>
        RateGenerator = 2,

        /// <summary>
        /// Mode 3: Square Wave Generator
        /// Output is high for half the count and low for half the count
        /// Counter automatically reloads after reaching zero
        /// Reference: Intel 8254 Datasheet, Mode 3 Operation
        /// </summary>
        SquareWave = 3,

        /// <summary>
        /// Mode 4: Software Triggered Strobe
        /// Output stays high until terminal count is reached, then goes low for one clock cycle
        /// Counter automatically reloads after reaching zero
        /// Reference: Intel 8254 Datasheet, Mode 4 Operation
        /// </summary>
        SoftwareStrobe = 4,

        /// <summary>
        /// Mode 5: Hardware Triggered Strobe
        /// Output stays high until terminal count after gate trigger, then goes low for one clock cycle
        /// Reference: Intel 8254 Datasheet, Mode 5 Operation
        /// </summary>
        HardwareStrobe = 5,

        /// <summary>
        /// Mode 6: Alias of Mode 2 (Rate Generator)
        /// Intel 8254 datasheet specifies that modes 2 and 6 are equivalent
        /// </summary>
        RateGeneratorAlias = 6,

        /// <summary>
        /// Mode 7: Alias of Mode 3 (Square Wave Generator)
        /// Intel 8254 datasheet specifies that modes 3 and 7 are equivalent
        /// </summary>
        SquareWaveAlias = 7,

        /// <summary>
        /// Not an actual mode, used to indicate the counter is not operational
        /// </summary>
        Inactive = 8
    }

    public enum OutputStatus : byte {
        Low = 0,
        High = 1,
    }

    /// <summary>
    /// Gets whether the gate input is currently enabled
    /// </summary>
    public bool IsGateEnabled { get; private set; } = true;


    /// <summary>
    /// Gets the current PIT mode as enum type
    /// </summary>
    public PitMode CurrentPitMode => (PitMode)Mode;

    /// <summary>
    /// Gets whether the counter is active for square wave generation
    /// </summary>
    public bool IsSquareWaveActive { get; private set; }

    /// <summary>
    /// Gets the current output state (high/low) for speaker signal
    /// </summary>
    public OutputStatus OutputState { get; set; } = OutputStatus.High;

    /// <summary>
    /// Initializes a new instance of the <see cref="Pit8254Counter"/> class.
    /// </summary>
    /// <param name="loggerService">The logger service implementation</param>
    /// <param name="index">The index of the counter in the counter array.</param>
    /// <param name="activator">The activator for the counter</param>
    public Pit8254Counter(ILoggerService loggerService, int index, CounterActivator activator) {
        _loggerService = loggerService;
        Activator = activator;
        Index = index;
        // Initialize values to trigger initial frequency setup
        ReloadValue = 0;
        CurrentCount = 0xFFFF;
        _inPulseState = false;
    }

    /// <summary>
    /// Gets or sets the activator for the counter.
    /// </summary>
    public CounterActivator Activator { get; }

    /// <summary>
    /// Calculated frequency for the counter
    /// </summary>
    public long Frequency => Activator.Frequency;

    /// <summary>
    /// Fires when the Timer parameters are changed.
    /// </summary>
    public event EventHandler? SettingChangedEvent;

    /// <summary>
    /// Event for when the counter gate state changes
    /// </summary>
    public event EventHandler<bool>? GateStateChanged;

    /// <summary>
    /// Gets or sets the Binary Coded Decimal (BCD) value for the counter.
    /// </summary>
    public bool Bcd { get; set; }

    /// <summary>
    /// Gets the index of the counter.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Gets or sets the mode of operation for the counter.
    /// Mode 3 is the mode set by IBM PC BIOS:
    ///   MOV AL,36H		; SEL TIM 0,LSB,MSB,MODE 3
    ///   OUT TIMER+3,AL	; WRITE TIMER MODE PEG
    /// https://github.com/gawlas/IBM-PC-BIOS/blob/e6cae33370fa7cd0568d72b785f682971edcc70c/IBM%20PC%20XT/XT%20BIOS%20V3/POST.ASM#L714-L715
    /// </summary>
    public int Mode { get; set; } = 3;

    /// <summary>
    /// Gets or sets the read/write policy for the counter.
    /// <br/>
    /// Looks like some programs don't set it and expect policy #3 to be used
    /// </summary>
    public int ReadWritePolicy { get; set; } = 3;

    /// <summary>
    /// The current count of the counter.
    /// </summary>
    public ushort CurrentCount { get => _currentCountRegister.Value; set => _currentCountRegister.Value = value; }

    /// <summary>
    /// The current reload value of the counter (value at which Count should be set when it reaches 0).
    /// </summary>
    public ushort ReloadValue {
        get => _reloadValueRegister.Value;
        set {
            _reloadValueRegister.Value = value;
            OnReloadValueWrite();
        }
    }

    /// <summary>
    /// Gets the period in milliseconds based on the reload value
    /// </summary>
    public float PeriodMs => MsPerPitTick * (ReloadValue == 0 ? 0x10000 : ReloadValue);

    /// <summary>
    /// Calculates the cycle length for audio generation at the given sample rate
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz</param>
    /// <returns>The cycle length in samples</returns>
    public float CalculateCycleLength(int sampleRate) {
        float counterMs = PeriodMs;
        float cycleLength = (sampleRate * counterMs) / 1000.0f;
        return cycleLength <= 2 ? 2 : cycleLength; // Minimum cycle length
    }

    /// <summary>
    /// Calculates the cycle step for each sample based on the sample rate
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz</param>
    /// <returns>The cycle step value</returns>
    public float CalculateCycleStep(int sampleRate) {
        return 1.0f / CalculateCycleLength(sampleRate);
    }

    /// <summary>
    /// Access the value of the counter using the current mode.
    /// </summary>
    public byte ReadCurrentCountByte() {
        if (_latchMode) {
            // When in latch mode, return the latch value instead of the current count.
            byte res = _latchRegister.ReadValue(ReadWritePolicy);
            if (_latchRegister.ValueFullyRead) {
                // Set Latch mode to null so that current count is read next time. Fully read depends on the policy.
                _latchMode = false;
            }
            return res;
        }
        return _currentCountRegister.ReadValue(ReadWritePolicy);
    }

    public void WriteReloadValueByte(byte value) {
        _reloadValueRegister.WriteValue(ReadWritePolicy, value);
        if (_reloadValueRegister.ValueFullyWritten) {
            OnReloadValueWrite();
        }
    }

    /// <summary>
    /// Checks whether the counter is activated and if so, decrements the ticks.
    /// Implements the behavior defined in Intel 8254 Datasheet section 3.3 "Mode Definitions"
    /// </summary>
    /// <returns>Whether the activation was processed.</returns>
    public bool ProcessActivation() {
        if (Activator.IsActivated) {
            // Handle pulse state machine for accurate timing
            if (_inPulseState) {
                ProcessPulseState();
                return true;
            }

            // For certain modes, counting is inhibited when gate is low
            // "When GATE is low, counting is inhibited." - Intel 8254 datasheet
            bool shouldCount = GetIfWeShouldCount();
            if (!shouldCount) {
                return false;
            }

            // Fix: Prevent rollover
            if (CurrentCount > 0) {
                CurrentCount--;
            }

            // Handle counter reaching zero based on the mode
            if (CurrentCount == 0) {
                switch (CurrentPitMode) {
                    case PitMode.InterruptOnTerminalCount:
                        // Mode 0: Output goes high when counter reaches terminal count
                        OutputState = OutputStatus.High;
                        // Counter stops at zero
                        break;

                    case PitMode.OneShot:
                        // Mode 1: One-shot - output goes low until counter reaches zero, then high
                        OutputState = OutputStatus.High;
                        // Counter stops at zero
                        break;

                    case PitMode.RateGenerator:
                    case PitMode.RateGeneratorAlias:
                        // Mode 2: Rate generator - output goes low for one clock cycle when counter reaches zero
                        OutputState = OutputStatus.Low;
                        // Reload the counter
                        CurrentCount = ReloadValue;
                        // Start pulse timing using hardware-accurate timing
                        StartPulse();
                        break;

                    case PitMode.SquareWave:
                    case PitMode.SquareWaveAlias:
                        // Mode 3: Square wave - output toggles between high and low
                        OutputState = OutputState == OutputStatus.High ? OutputStatus.Low : OutputStatus.High;
                        // Reload the counter
                        CurrentCount = ReloadValue;
                        break;

                    case PitMode.HardwareStrobe:case PitMode.SoftwareStrobe:
                        // Mode 4: Software triggered strobe - output goes low when counter reaches zero
                        OutputState = OutputStatus.Low;
                        // Reload the counter
                        CurrentCount = ReloadValue;
                        // Start pulse timing using hardware-accurate timing
                        StartPulse();
                        break;
                }
            } else {
                // Handle mid-count behavior for certain modes
                switch (CurrentPitMode) {
                    case PitMode.SquareWave:
                    case PitMode.SquareWaveAlias:
                        // For square wave, toggle halfway through the count
                        if (CurrentCount == ReloadValue / 2) {
                            OutputState = OutputState == OutputStatus.High ? OutputStatus.Low : OutputStatus.High;
                        }
                        break;
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Starts a new hardware-accurate pulse
    /// </summary>
    private void StartPulse() {
        _inPulseState = true;
        _pulseStartTimestamp = _pulseTimer.ElapsedTicks;

        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Counter {Index}: Starting pulse, output LOW", Index);
        }
    }

    /// <summary>
    /// Processes the pulse timing state machine using accurate hardware timing
    /// </summary>
    private void ProcessPulseState() {
        if (!_inPulseState) {
            return;
        }

        // Calculate elapsed time in nanoseconds
        long currentTicks = _pulseTimer.ElapsedTicks;
        long elapsedTicks = currentTicks - _pulseStartTimestamp;
        double elapsedNanoseconds = (elapsedTicks * 1_000_000_000.0) / Stopwatch.Frequency;

        // Check if pulse duration (one PIT clock cycle) has elapsed
        if (elapsedNanoseconds >= PitClockPulseNs) {
            // Pulse complete, return output to HIGH
            OutputState = OutputStatus.High;
            _inPulseState = false;

            if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                _loggerService.Verbose("Counter {Index}: Pulse complete after {ElapsedNs:F2}ns, output returned to HIGH",
                    Index, elapsedNanoseconds);
            }
        }
    }

    private bool GetIfWeShouldCount() {
        bool shouldCount = true;
        switch (CurrentPitMode) {
            case PitMode.InterruptOnTerminalCount:
            case PitMode.RateGenerator:
            case PitMode.RateGeneratorAlias:
            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
            case PitMode.SoftwareStrobe:
                // These modes don't count when gate is disabled
                shouldCount = IsGateEnabled;
                break;
        }
        return shouldCount;
    }

    /// <summary>
    /// Configures the counter using the specified <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The read/write policy.</param>
    public void Configure(ushort value) {
        ushort readWritePolicy = (ushort)(value >> 4 & 0b11);
        if (readWritePolicy == 0) {
            if (_latchMode) {
                // If a Counter is latched and then, some time later, latched again before the count is read, the second Counter Latch Command is ignored. 
                return;
            }
            _latchMode = true;
            _latchRegister.Value = _currentCountRegister.Value;
            // Counter Latch Commands do not affect the programmed Mode of the Counter or the read write policy in any way.
            return;
        }
        ReadWritePolicy = readWritePolicy;
        Mode = (ushort)(value >> 1 & 0b111);
        Bcd = (value & 1) == 1;

        // Reset pulse state when mode changes
        _inPulseState = false;

        SettingChangedEvent?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called when a value is written to the reload value counter boundary. It updates the desired frequency based on the new value.
    /// </summary>
    private void OnReloadValueWrite() {
        if (ReloadValue == 0) {
            // 18.2065 Hz
            UpdateDesiredFrequency(HardwareFrequency / 0x10000);
        } else {
            UpdateDesiredFrequency(HardwareFrequency / ReloadValue);
        }

        // Update square wave state if needed
        if (CurrentPitMode is PitMode.SquareWave or PitMode.SquareWaveAlias) {
            // Only consider valid counter values
            IsSquareWaveActive = ReloadValue >= 2;
        }
    }

    /// <summary>
    /// Updates the frequency of activation based on the desired frequency
    /// </summary>
    /// <param name="desiredFrequency">The desired frequency of activation</param>
    public void UpdateDesiredFrequency(long desiredFrequency) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Updating counter {Index} frequency to {DesiredFrequency}", Index, desiredFrequency);
        }
        Activator.Frequency = desiredFrequency;
        SettingChangedEvent?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Triggers the gate for this counter.
    /// Implements the GATE input behavior as defined in Intel 8254 Datasheet section 3.3
    /// </summary>
    /// <param name="enabled">Whether the gate is enabled (high) or disabled (low)</param>
    public void SetGateState(bool enabled) {
        // Store gate state for modes where it inhibits counting
        IsGateEnabled = enabled;

        // Handle gate trigger based on current mode
        switch (CurrentPitMode) {
            case PitMode.InterruptOnTerminalCount:
                // Mode 0: "The GATE input is level sensitive and active high. When GATE is low, counting is inhibited."
                // Reference: Intel 8254 Datasheet, Mode 0 Operation
                // Suspend counting is done above in ProcessActivation
                break;

            case PitMode.OneShot:
                // Mode 1: "The GATE input is edge sensitive and rising-edge triggered. A rising edge of GATE loads the counter
                // with the initial count and initiates counting."
                // Reference: Intel 8254 Datasheet, Mode 1 Operation
                if (enabled) {
                    OutputState = OutputStatus.Low;
                    // Load counter with reload value and start counting
                    CurrentCount = ReloadValue;
                }
                break;

            case PitMode.RateGenerator:
            case PitMode.RateGeneratorAlias:
                // Mode 2: "The GATE input is level sensitive and active high. When GATE is low, counting is inhibited.
                // When GATE goes from low to high, the counter is reloaded with the initial count and counting begins."
                // Reference: Intel 8254 Datasheet, Mode 2 Operation
                if (!enabled) {
                    OutputState = OutputStatus.High;
                    // Cancel any ongoing pulse
                    _inPulseState = false;
                } else {
                    // Reload counter on gate rising edge
                    CurrentCount = ReloadValue;
                }
                break;

            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                // Mode 3: "The GATE input is level sensitive and active high. When GATE is low, counting is inhibited.
                // When GATE goes from low to high, the counter is reloaded with the initial count and counting begins."
                // Reference: Intel 8254 Datasheet, Mode 3 Operation
                IsSquareWaveActive = enabled;
                if (enabled) {
                    // Reset the output state to high when starting
                    OutputState = OutputStatus.High;
                    // Reload counter on gate rising edge
                    CurrentCount = ReloadValue;
                } else {
                    OutputState = OutputStatus.High;
                }
                break;

            case PitMode.SoftwareStrobe:
                // Mode 4: "The GATE input is level sensitive and active high. When GATE is low, counting is inhibited."
                // Reference: Intel 8254 Datasheet, Mode 4 Operation
                if (!enabled) {
                    // Cancel any ongoing pulse
                    _inPulseState = false;
                }
                break;

            case PitMode.HardwareStrobe:
                // Mode 5: "The GATE input is edge sensitive and rising-edge triggered. A rising edge of GATE initiates counting."
                // Reference: Intel 8254 Datasheet, Mode 5 Operation
                if (!enabled) {
                    // Cancel any ongoing pulse
                    _inPulseState = false;
                }
                break;
        }

        GateStateChanged?.Invoke(this, enabled);
    }

    /// <inheritdoc/>
    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}