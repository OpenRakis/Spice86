using Spice86.Shared.Interfaces;

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

    private readonly ILoggerService _loggerService;

    private bool _latchMode = false;
    private readonly Pit8254Register _latchRegister = new();
    private readonly Pit8254Register _currentCountRegister = new();
    private readonly Pit8254Register _reloadValueRegister = new();
    private bool _isGateHigh = true;
    private bool _needsReload = false;

    /// <summary>
    /// PIT operation modes
    /// </summary>
    public enum PitMode : byte {
        InterruptOnTerminalCount = 0,
        OneShot = 1,
        RateGenerator = 2,
        SquareWave = 3,
        SoftwareStrobe = 4,
        HardwareStrobe = 5,
        RateGeneratorAlias = 6,
        SquareWaveAlias = 7,
        Inactive = 8
    }

    public enum OutputStatus : byte {
        Low = 0,
        High = 1,
    }

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
    public OutputStatus OutputState { get; private set; } = OutputStatus.High;

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

            // Special handling for different modes when a new value is written
            switch (CurrentPitMode) {
                case PitMode.InterruptOnTerminalCount:
                    // Mode 0: Counter immediately loads the new count
                    CurrentCount = ReloadValue;
                    OutputState = OutputStatus.Low;
                    break;
                case PitMode.RateGenerator:
                case PitMode.RateGeneratorAlias:
                case PitMode.SquareWave:
                case PitMode.SquareWaveAlias:
                    // Modes 2 & 3: Reload the counter immediately
                    CurrentCount = ReloadValue;
                    OutputState = OutputStatus.High;
                    break;
                case PitMode.SoftwareStrobe:
                    // Mode 4: Counter continues counting, but will use new value on next reload
                    OutputState = OutputStatus.High;
                    break;
            }
        }
    }

    /// <summary>
    /// Checks whether the counter is activated and if so, processes the counter based on its mode.
    /// </summary>
    /// <returns>Whether the activation was processed.</returns>
    public bool ProcessActivation(bool forced = false) {
        // Special case for Counter 2 (PC Speaker timer) - many games rely on it
        // even when not explicitly activated through the timer system
        // This fixes 'Where in the World is Carmen Sandiego'(among others)
        bool isActivated = Activator.IsActivated || forced;

        if (!isActivated) {
            return false;
        }

        // Process the counter based on its operating mode
        switch (CurrentPitMode) {
            case PitMode.InterruptOnTerminalCount:
                // Mode 0: If count is 0, output stays high. Otherwise decrement and check
                if (CurrentCount > 0) {
                    CurrentCount--;
                    if (CurrentCount == 0) {
                        OutputState = OutputStatus.High; // Output goes high when count reaches 0
                    }
                }
                break;

            case PitMode.OneShot:
                // Mode 1: OneShot - Output remains low until count reaches 0
                if (CurrentCount > 0) {
                    CurrentCount--;
                    if (CurrentCount == 0) {
                        OutputState = OutputStatus.High; // Output goes high when count reaches 0
                    }
                }
                break;

            case PitMode.RateGenerator:
            case PitMode.RateGeneratorAlias:
                // Mode 2: Rate Generator - Output stays high until count = 1, then goes low for one clock
                if (CurrentCount > 0) {
                    CurrentCount--;
                    if (CurrentCount == 1) {
                        OutputState = OutputStatus.Low; // Goes low when count reaches 1
                    } else if (CurrentCount == 0) {
                        CurrentCount = ReloadValue; // Reload when count reaches 0
                        OutputState = OutputStatus.High; // Back to high
                    }
                }
                break;

            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                // Mode 3: Square Wave - Output toggles each time count reaches half and zero
                if (CurrentCount > 0) {
                    ushort halfCount = (ushort)(ReloadValue / 2); // This is how we get a square wave
                    CurrentCount--;

                    if (ReloadValue == 0) {
                        // Special case for reload value = 0 (effectively 65536)
                        halfCount = 0x8000;
                    }

                    if (CurrentCount == halfCount) {
                        OutputState = OutputStatus.Low; // Toggle output at half count
                    } else if (CurrentCount == 0) {
                        OutputState = OutputStatus.High; // Toggle output at zero
                        CurrentCount = ReloadValue; // Reload
                    }
                }
                break;

            case PitMode.SoftwareStrobe:
                // Mode 4: Software Triggered Strobe - Output goes low when count reaches 0
                if (CurrentCount > 0) {
                    CurrentCount--;
                    if (CurrentCount == 0) {
                        OutputState = OutputStatus.Low; // Goes low when count reaches 0
                        _needsReload = true; // Flag for reload after the next clock
                    }
                } else if (_needsReload) {
                    OutputState = OutputStatus.High; // Back to high
                    CurrentCount = ReloadValue; // Reload
                    _needsReload = false;
                }
                break;

            case PitMode.HardwareStrobe:
                // Mode 5: Hardware Triggered Strobe - Same as Mode 4 but triggered by hardware
                if (_isGateHigh && CurrentCount > 0) { // Only count if gate is high
                    CurrentCount--;
                    if (CurrentCount == 0) {
                        OutputState = OutputStatus.Low;
                        _needsReload = true;
                    }
                } else if (_needsReload) {
                    OutputState = OutputStatus.High;
                    CurrentCount = ReloadValue;
                    _needsReload = false;
                }
                break;
        }

        return true;
    }

    /// <summary>
    /// Configures the counter using the specified <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The read/write policy.</param>
    public void Configure(ushort value) {
        ushort readWritePolicy = (ushort)(value >> 4 & 0b11);
        if (readWritePolicy == 0) {
            if (_latchMode) {
                // If a Counter is latched and then, some time later,
                // latched again before the count is read,
                // the second Counter Latch Command is ignored. 
                return;
            }
            _latchMode = true;
            _latchRegister.Value = _currentCountRegister.Value;
            // Counter Latch Commands do not affect the programmed Mode of the Counter
            // or the read write policy in any way.
            return;
        }
        ReadWritePolicy = readWritePolicy;
        int newMode = (ushort)(value >> 1 & 0b111);

        if (Mode != newMode && _loggerService.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _loggerService.Debug("Counter {Index} Mode updated from {OldMode} to {NewMode}", Index, Mode, newMode);
        }

        Mode = newMode;
        Bcd = (value & 1) == 1;

        // Initialize counter state based on the new mode
        InitializeCounterForMode();
    }

    /// <summary>
    /// Initializes the counter state appropriate for the current mode
    /// </summary>
    private void InitializeCounterForMode() {
        switch (CurrentPitMode) {
            case PitMode.InterruptOnTerminalCount:
                OutputState = OutputStatus.Low;
                break;
            case PitMode.OneShot:
            case PitMode.RateGenerator:
            case PitMode.RateGeneratorAlias:
            case PitMode.SoftwareStrobe:
            case PitMode.HardwareStrobe:
                OutputState = OutputStatus.High;
                break;
            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                OutputState = OutputStatus.High;
                IsSquareWaveActive = ReloadValue >= 2;
                break;
        }
    }

    /// <summary>
    /// Called when a value is written to the reload value counter boundary.
    /// It updates the desired frequency based on the new value.
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
    /// Triggers the gate for this counter
    /// </summary>
    public void SetGateState(bool enabled) {
        _isGateHigh = enabled;

        // Handle gate trigger based on current mode
        switch (CurrentPitMode) {
            case PitMode.OneShot:
                if (enabled) {
                    CurrentCount = ReloadValue; // Reload on rising edge
                    OutputState = OutputStatus.Low; // Output goes low
                }
                break;

            case PitMode.RateGenerator:
            case PitMode.RateGeneratorAlias:
                if (enabled) {
                    CurrentCount = ReloadValue; // Reload on rising edge
                    OutputState = OutputStatus.High;
                } else {
                    OutputState = OutputStatus.High; // Output forced high when gate goes low
                }
                break;

            case PitMode.SquareWave:
            case PitMode.SquareWaveAlias:
                IsSquareWaveActive = enabled && ReloadValue >= 2;
                if (enabled) {
                    CurrentCount = ReloadValue; // Reload on rising edge
                    OutputState = OutputStatus.High;
                } else {
                    OutputState = OutputStatus.High; // Output forced high when gate goes low
                }
                break;

            case PitMode.HardwareStrobe:
                if (enabled) {
                    CurrentCount = ReloadValue; // Reload on rising edge
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