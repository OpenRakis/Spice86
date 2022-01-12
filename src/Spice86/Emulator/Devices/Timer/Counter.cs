namespace Spice86.Emulator.Devices.Timer;

using Serilog;

using Spice86.Emulator.Errors;
using Spice86.Emulator.Machine;
using Spice86.Utils;

public class Counter
{
    private static readonly ILogger _logger = Log.Logger.ForContext<Counter>();
    public const long HardwareFrequency = 1193182;
    private readonly Machine _machine;
    private readonly int _index;

    // Some programs don't set it so let's use by default the simplest mode
    private int _readWritePolicy = 1;

    private int _mode;
    private int _bcd;
    private int _value;
    private bool _firstByteRead;
    private bool _firstByteWritten;
    private long _ticks;
    private readonly ICounterActivator _activator;

    public Counter(Machine machine, int index, ICounterActivator activator)
    {
        this._machine = machine;
        this._index = index;
        this._activator = activator;

        // Default is 18.2 times per second
        UpdateDesiredFreqency(18);
    }

    public int GetIndex()
    {
        return _index;
    }

    public int GetReadWritePolicy()
    {
        return _readWritePolicy;
    }

    public void SetReadWritePolicy(int readWritePolicy)
    {
        this._readWritePolicy = readWritePolicy;
    }

    public int GetMode()
    {
        return _mode;
    }

    public void SetMode(int mode)
    {
        this._mode = mode;
    }

    public int GetBcd()
    {
        return _bcd;
    }

    public void SetBcd(int bcd)
    {
        this._bcd = bcd;
    }

    public int GetValue()
    {
        return _value;
    }

    public void SetValue(int counter)
    {
        this._value = counter;
        OnValueWrite();
    }

    public long GetTicks()
    {
        return _ticks;
    }

    public int GetValueUsingMode()
    {
        return _readWritePolicy switch
        {
            0 => throw new UnhandledOperationException(_machine,
              "Latch read is not implemented yet"),
            1 => ReadLsb(),
            2 => ReadMsb(),
            3 => ReadPolicy3(),
            _ => throw new UnhandledOperationException(_machine, $"Invalid readWritePolicy {_readWritePolicy}")
        };
    }

    public void SetValueUsingMode(int partialValue)
    {
        if (_readWritePolicy == 1)
        {
            WriteLsb(partialValue);
        }
        else if (partialValue == 2)
        {
            WriteMsb(partialValue);
        }
        else if (partialValue == 3)
        {
            WritePolicy3(partialValue);
        }
        throw new UnhandledOperationException(_machine, $"Invalid readWritePolicy {_readWritePolicy}");
    }

    private int ReadPolicy3()
    {
        // LSB first, then MSB
        if (_firstByteRead)
        {
            // return msb
            _firstByteRead = false;
            return ReadMsb();
        }

        // else return lsb
        _firstByteRead = true;
        return ReadLsb();
    }

    private void WritePolicy3(int partialValue)
    {
        // LSB first, then MSB
        if (_firstByteWritten)
        {
            // write MSB
            _firstByteWritten = false;
            WriteMsb(partialValue);
        }
        // Fully written
        else
        {
            // Else write LSB
            _firstByteWritten = true;
            WriteLsb(partialValue);
        }
    }

    private int ReadLsb()
    {
        return ConvertUtils.ReadLsb(_value);
    }

    private void WriteLsb(int partialValue)
    {
        _value = ConvertUtils.WriteLsb(_value, partialValue);
    }

    private int ReadMsb()
    {
        return ConvertUtils.ReadMsb(_value);
    }

    private void WriteMsb(int partialValue)
    {
        _value = ConvertUtils.WriteMsb(_value, partialValue);
    }

    private void OnValueWrite()
    {
        if (_value == 0)
        {
            UpdateDesiredFreqency(HardwareFrequency / 0x10000);
        }
        else
        {
            UpdateDesiredFreqency(HardwareFrequency / _value);
        }
    }

    private void UpdateDesiredFreqency(long desiredFrequency)
    {
        _activator.UpdateDesiredFrequency(desiredFrequency);
        _logger.Information("Updating counter {@Index} frequency to {@DesiredFrequency}.", _index, desiredFrequency);
    }

    public bool ProcessActivation(long currentCycles)
    {
        if (_activator.IsActivated())
        {
            _ticks++;
            return true;
        }

        return false;
    }

    public override string ToString()
    {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}