namespace Spice86.Emulator.Devices.Timer;

using Serilog;

using Spice86.Emulator.Errors;
using Spice86.Emulator.VM;
using Spice86.Utils;

public class Counter {
    public const long HardwareFrequency = 1193182;
    private static readonly ILogger _logger = Log.Logger.ForContext<Counter>();
    private readonly ICounterActivator _activator;
    private readonly int _index;
    private readonly Machine _machine;
    private int _bcd;

    private bool _firstByteRead;

    private bool _firstByteWritten;

    private int _mode;

    // Some programs don't set it so let's use by default the simplest mode
    private int _readWritePolicy = 1;

    private long _ticks;
    private ushort _value;

    public Counter(Machine machine, int index, ICounterActivator activator) {
        this._machine = machine;
        this._index = index;
        this._activator = activator;

        // Default is 18.2 times per second
        UpdateDesiredFreqency(18);
    }

    public int GetBcd() {
        return _bcd;
    }

    public int GetIndex() {
        return _index;
    }

    public int GetMode() {
        return _mode;
    }

    public int GetReadWritePolicy() {
        return _readWritePolicy;
    }

    public long GetTicks() {
        return _ticks;
    }

    public int GetValue() {
        return _value;
    }

    public byte GetValueUsingMode() {
        return _readWritePolicy switch {
            0 => throw new UnhandledOperationException(_machine,
                "Latch read is not implemented yet"),
            1 => ReadLsb(),
            2 => ReadMsb(),
            3 => ReadPolicy3(),
            _ => throw new UnhandledOperationException(_machine, $"Invalid readWritePolicy {_readWritePolicy}")
        };
    }

    public bool ProcessActivation(long currentCycles) {
        if (_activator.IsActivated()) {
            _ticks++;
            return true;
        }

        return false;
    }

    public void SetBcd(int bcd) {
        this._bcd = bcd;
    }

    public void SetMode(int mode) {
        this._mode = mode;
    }

    public void SetReadWritePolicy(int readWritePolicy) {
        this._readWritePolicy = readWritePolicy;
    }

    public void SetValue(ushort counter) {
        this._value = counter;
        OnValueWrite();
    }

    public void SetValueUsingMode(byte partialValue) {
        switch (_readWritePolicy) {
            case 1:
                WriteLsb(partialValue);
                break;
            case 2:
                WriteMsb(partialValue);
                break;
            case 3:
                WritePolicy3(partialValue);
                break;
            default: throw new UnhandledOperationException(_machine, $"Invalid readWritePolicy {_readWritePolicy}");
        }
        OnValueWrite();
    }

    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }

    private void OnValueWrite() {
        if (_value == 0) {
            UpdateDesiredFreqency(HardwareFrequency / 0x10000);
        } else {
            UpdateDesiredFreqency(HardwareFrequency / _value);
        }
    }

    private byte ReadLsb() {
        return ConvertUtils.ReadLsb(_value);
    }

    private byte ReadMsb() {
        return ConvertUtils.ReadMsb(_value);
    }

    private byte ReadPolicy3() {
        // LSB first, then MSB
        if (_firstByteRead) {
            // return msb
            _firstByteRead = false;
            return ReadMsb();
        }

        // else return lsb
        _firstByteRead = true;
        return ReadLsb();
    }

    private void UpdateDesiredFreqency(long desiredFrequency) {
        _activator.UpdateDesiredFrequency(desiredFrequency);
        _logger.Information("Updating counter {@Index} frequency to {@DesiredFrequency}.", _index, desiredFrequency);
    }

    private void WriteLsb(byte partialValue) {
        _value = ConvertUtils.WriteLsb(_value, partialValue);
    }

    private void WriteMsb(byte partialValue) {
        _value = ConvertUtils.WriteMsb(_value, partialValue);
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