namespace Spice86.Emulator.Devices.Sound;

using Serilog;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.VM;
using Spice86.Utils;

/// <summary>
/// PC speaker implementation. Does not produce any sound, just handles the bare minimum to make programs run.
/// </summary>
public class PcSpeaker : DefaultIOPortHandler {
    private static readonly ILogger _logger = Program.Logger.ForContext<PcSpeaker>();
    private const int PcSpeakerPortNumber = 0x61;
    private byte _value;

    public PcSpeaker(Machine machine, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort) {
    }

    public override byte Inb(int port) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PC Speaker get value {@PCSpeakerValue}", ConvertUtils.ToHex8(this._value));
        }

        return _value;
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(PcSpeakerPortNumber, this);
    }

    public override void Outb(int port, byte value) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PC Speaker set value {@PCSpeakerValue}", ConvertUtils.ToHex8(value));
        }

        _value = value;
    }
}