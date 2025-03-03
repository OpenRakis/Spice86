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
    private readonly ILoggerService _loggerService;

    private bool _latchMode = false;
    private readonly Pit8254Register _latchRegister = new();
    private readonly Pit8254Register _currentCountRegister = new();
    private readonly Pit8254Register _reloadValueRegister = new();

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
    /// </summary>
    /// <returns>Whether the activation was processed.</returns>
    public bool ProcessActivation() {
        if (Activator.IsActivated) {
            CurrentCount--;
            return true;
        }

        return false;
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
        if (Mode != 3 && _loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("Counter {Index} Mode updated to {Mode} which is not supported.", Index, Mode);
        }
        Bcd = (value & 1) == 1;
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
    
    /// <inheritdoc/>
    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}