using Spice86.Shared.Interfaces;

namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Shared.Utils;

/// <summary>
/// Represents a Programmable Interval Timer (PIT) counter in an IBM PC. <br/>
/// The PIT is a chip connected to the CPU and is used to generate accurate time delays under software control. <br/>
/// Each counter can be programmed to operate in one of six modes and can count up or down.
/// </summary>
public class Counter {
    /// <summary>
    /// Equals to 1.193182 MHz
    /// </summary>
    public const long HardwareFrequency = 1193182;
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Gets or sets the activator for the counter.
    /// </summary>
    public CounterActivator Activator { get; protected set; }
    private readonly State _state;

    private bool _firstByteRead;

    private bool _firstByteWritten;

    /// <summary>
    /// Initializes a new instance of the <see cref="Counter"/> class.
    /// </summary>
    /// <param name="state">The CPU state</param>
    /// <param name="loggerService">The logger service implementation</param>
    /// <param name="index">The index of the counter</param>
    /// <param name="activator">The activator for the counter</param>
    public Counter(State state, ILoggerService loggerService, int index, CounterActivator activator) {
        _loggerService = loggerService;
        _state = state;
        Index = index;
        Activator = activator;

        // Default is 18.2 times per second
        UpdateDesiredFreqency(18);
    }

    /// <summary>
    /// Gets or sets the Binary Coded Decimal (BCD) value for the counter.
    /// </summary>
    public int Bcd { get; set; }

    /// <summary>
    /// Gets the index of the counter.
    /// </summary>
    public int Index { get; private set; }

    /// <summary>
    /// Gets or sets the mode of operation for the counter.
    /// </summary>
    public int Mode { get; set; }

    /// <summary>
    /// Gets or sets the latch value for the counter.
    /// </summary>
    public ushort? Latch { get; set; } = null;


    /// <summary>
    /// Gets or sets the read/write policy for the counter.
    /// <br/>
    /// Some programs don't set it so let's use by default the simplest mode (1)
    /// </summary>
    public int ReadWritePolicy { get; set; } = 1;

    /// <summary>
    /// Gets the current tick count of the counter.
    /// <br/>
    /// Defaults to 0xFFFF (65535) when the counter is not activated.
    /// </summary>
    public ushort Ticks { get; private set; } = 0xFFFF;

    /// <summary>
    /// Gets the current value of the counter.
    /// </summary>
    public ushort Value { get; private set; }

    /// <summary>
    /// Gets the value of the counter using the current mode.
    /// </summary>
    public byte ValueUsingMode {
        get {
            ushort value = Latch ?? Ticks;
            byte ret = ReadWritePolicy switch {
                0 => throw new UnhandledOperationException(_state, "Latch read is not implemented yet"),
                1 => Lsb(value),
                2 => Msb(value),
                3 => Policy3(value),
                _ => throw new UnhandledOperationException(_state, $"Invalid readWritePolicy {ReadWritePolicy}")
            };
            return ret;
        }
    }

    /// <summary>
    /// Checks whether the counter is activated and if so, decrements the ticks.
    /// </summary>
    /// <returns>Whether the activation was processed.</returns>
    public bool ProcessActivation() {
        if (Activator.IsActivated) {
            Ticks--;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Configures the counter using the specified <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The read/write policy.</param>
    public void Configure(ushort value) {
        ReadWritePolicy = (ushort)(value >> 4 & 0b11);
        if (ReadWritePolicy == 0) {
            Latch = Ticks;
            ReadWritePolicy = 0b11;
            return;
        }
        Mode = (ushort)(value >> 1 & 0b111);
        Bcd = (ushort)(value & 1);
    }

    /// <summary>
    /// Sets the counter <see cref="Value"/> to the specified <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The counter value</param>
    public void SetValue(ushort value) {
        Value = value;
        OnValueWrite();
    }

    /// <summary>
    /// Sets the value of the counter using the current mode.
    /// </summary>
    public void SetValueUsingMode(byte partialValue) {
        switch (ReadWritePolicy) {
            case 1:
                WriteLsb(partialValue);
                break;
            case 2:
                WriteMsb(partialValue);
                break;
            case 3:
                WritePolicy3(partialValue);
                break;
            default: throw new UnhandledOperationException(_state, $"Invalid readWritePolicy {ReadWritePolicy}");
        }
        OnValueWrite();
    }

    /// <inheritdoc/>
    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }

    /// <summary>
    /// Called when a value is written to the counter. It updates the desired frequency based on the new value.
    /// </summary>
    private void OnValueWrite() {
        if (Value == 0) {
            UpdateDesiredFreqency(HardwareFrequency / 0x10000);
        } else {
            UpdateDesiredFreqency(HardwareFrequency / Value);
        }
    }

    private byte Lsb(ushort value) => ConvertUtils.ReadLsb(value);

    private byte Msb(ushort value) => ConvertUtils.ReadMsb(value);

    private byte Policy3(ushort value) {
        // LSB first, then MSB
        if (_firstByteRead) {
            if (Latch != null) {
                Latch = null;
            }

            // return msb
            _firstByteRead = false;
            return Msb(value);
        }
        // else return lsb
        _firstByteRead = true;
        return Lsb(value);
    }

    public void UpdateDesiredFreqency(long desiredFrequency) {
        Activator.Frequency = desiredFrequency;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Updating counter {Index} frequency to {DesiredFrequency}", Index, desiredFrequency);
        }
    }

    private void WriteLsb(byte partialValue) => Value = ConvertUtils.WriteLsb(Value, partialValue);

    private void WriteMsb(byte partialValue) => Value = ConvertUtils.WriteMsb16(Value, partialValue);

    private void WritePolicy3(byte partialValue) {
        // LSB first, then MSB
        if (_firstByteWritten) {
            // write MSB
            _firstByteWritten = false;
            WriteMsb(partialValue);
        }
        // Fully written
        else {
            // Else write LSB
            _firstByteWritten = true;
            WriteLsb(partialValue);
        }
    }
}
