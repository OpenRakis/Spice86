namespace Spice86.Emulator.Devices.Timer;

using Serilog;

using Spice86.Emulator.Errors;
using Spice86.Emulator.VM;
using Spice86.Utils;

public class Counter {
    public const long HardwareFrequency = 1193182;
    private static readonly ILogger _logger = Program.Logger.ForContext<Counter>();
    public CounterActivator Activator { get; protected set; }
    private readonly Machine _machine;

    private bool _firstByteRead;

    private bool _firstByteWritten;

    public Counter(Machine machine, int index, CounterActivator activator) {
        this._machine = machine;
        Index = index;
        this.Activator = activator;

        // Default is 18.2 times per second
        UpdateDesiredFreqency(18);
    }

    public int Bcd { get; set; }

    public int Index { get; private set; }

    public int Mode { get; set; }

    /// <summary>
    // Some programs don't set it so let's use by default the simplest mode (1)
    /// </summary>
    public int ReadWritePolicy { get; set; } = 1;

    public long Ticks { get; private set; }

    public ushort Value { get; private set; }

    public byte ValueUsingMode {
        get {
            return ReadWritePolicy switch {
                0 => throw new UnhandledOperationException(_machine,
                    "Latch read is not implemented yet"),
                1 => Lsb,
                2 => Msb,
                3 => Policy3,
                _ => throw new UnhandledOperationException(_machine, $"Invalid readWritePolicy {ReadWritePolicy}")
            };
        }
    }

    /// <summary>
    /// TODO: Use <paramref name="currentCycles"/>
    /// </summary>
    /// <param name="currentCycles"></param>
    /// <returns></returns>
    public bool ProcessActivation(long currentCycles) {
        if (Activator.IsActivated) {
            Ticks++;
            return true;
        }

        return false;
    }

    public void SetValue(ushort counter) {
        Value = counter;
        OnValueWrite();
    }

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
            default: throw new UnhandledOperationException(_machine, $"Invalid readWritePolicy {ReadWritePolicy}");
        }
        OnValueWrite();
    }

    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }

    private void OnValueWrite() {
        if (Value == 0) {
            UpdateDesiredFreqency(HardwareFrequency / 0x10000);
        } else {
            UpdateDesiredFreqency(HardwareFrequency / Value);
        }
    }

    private byte Lsb =>ConvertUtils.ReadLsb(Value);

    private byte Msb => ConvertUtils.ReadMsb(Value);

    private byte Policy3 {
        get {
            // LSB first, then MSB
            if (_firstByteRead) {
                // return msb
                _firstByteRead = false;
                return Msb;
            }
            // else return lsb
            _firstByteRead = true;
            return Lsb;
        }
    }

    private void UpdateDesiredFreqency(long desiredFrequency) {
        Activator.UpdateDesiredFrequency(desiredFrequency);
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Updating counter {@Index} frequency to {@DesiredFrequency}.", Index, desiredFrequency);
        }
    }

    private void WriteLsb(byte partialValue) {
        Value = ConvertUtils.WriteLsb(Value, partialValue);
    }

    private void WriteMsb(byte partialValue) {
        Value = ConvertUtils.WriteMsb(Value, partialValue);
    }

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