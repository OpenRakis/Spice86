using Spice86.Logging;
using Spice86.Shared.Interfaces;

namespace Spice86.Core.Emulator.Devices.Timer;

using Serilog;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Utils;

public class Counter {
    public const long HardwareFrequency = 1193182;
    private readonly ILoggerService _loggerService;
    public CounterActivator Activator { get; protected set; }
    private readonly Machine _machine;

    private bool _firstByteRead;

    private bool _firstByteWritten;

    public Counter(Machine machine, ILoggerService loggerService, int index, CounterActivator activator) {
        _loggerService = loggerService;
        _machine = machine;
        Index = index;
        Activator = activator;

        // Default is 18.2 times per second
        UpdateDesiredFreqency(18);
    }

    public int Bcd { get; set; }

    public int Index { get; private set; }

    public int Mode { get; set; }

    public ushort? Latch { get; set; } = null;

    /// <summary>
    /// Some programs don't set it so let's use by default the simplest mode (1)
    /// </summary>
    public int ReadWritePolicy { get; set; } = 1;

    public ushort Ticks { get; private set; } = 0xFFFF;

    public ushort Value { get; private set; }

    public byte ValueUsingMode {
        get {
            ushort value = Latch ?? Ticks;
            byte ret = ReadWritePolicy switch {
                0 => throw new UnhandledOperationException(_machine, "Latch read is not implemented yet"),
                1 => Lsb(value),
                2 => Msb(value),
                3 => Policy3(value),
                _ => throw new UnhandledOperationException(_machine, $"Invalid readWritePolicy {ReadWritePolicy}")
            };
            return ret;
        }
    }

    /// <summary>
    /// TODO: Use <paramref name="currentCycles"/>
    /// </summary>
    /// <param name="currentCycles"></param>
    /// <returns></returns>
    public bool ProcessActivation(long currentCycles) {
        if (Activator.IsActivated) {
            Ticks--;
            return true;
        }

        return false;
    }

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

    private byte Lsb(ushort value) {
        return ConvertUtils.ReadLsb(value);
    }

    private byte Msb(ushort value) {
        return ConvertUtils.ReadMsb(value);
    }

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

    private void UpdateDesiredFreqency(long desiredFrequency) {
        Activator.Frequency = desiredFrequency;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("Updating counter {Index} frequency to {DesiredFrequency}.", Index, desiredFrequency);
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
