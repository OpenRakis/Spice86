namespace Spice86.Emulator.Devices.Sound;

using Serilog;

using Spice86.Emulator.IOPorts;
using Spice86.Emulator.Sound.PCSpeaker;
using Spice86.Emulator.VM;
using Spice86.Utils;

/// <summary>
/// PC speaker implementation.
/// </summary>
public class PcSpeaker : DefaultIOPortHandler {
    private static readonly ILogger _logger = Program.Logger.ForContext<PcSpeaker>();
    private const int PcSpeakerPortNumber = 0x61;
    private byte _value;

    private InternalSpeaker _pcSpeaker = new();

    public PcSpeaker(Machine machine, Configuration configuration) : base(machine, configuration) {
    }

    public override byte ReadByte(int port) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PC Speaker get value {@PCSpeakerValue}", ConvertUtils.ToHex8(this._value));
        }

        return _value;
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(PcSpeakerPortNumber, this);
    }

    public override void WriteByte(int port, byte value) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PC Speaker set value {@PCSpeakerValue}", ConvertUtils.ToHex8(value));
        }

        _value = value;

        ((IOutputPort)_pcSpeaker).WriteByte(port, value);
    }
}