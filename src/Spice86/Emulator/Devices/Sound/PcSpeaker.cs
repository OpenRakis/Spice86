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
    private const int PcSpeakerOutputOnlyPortPortNumber = 0x42;

    private InternalSpeaker _pcSpeaker = new();

    public PcSpeaker(Machine machine, Configuration configuration) : base(machine, configuration) {
    }

    public override byte ReadByte(int port) {
        var value = _pcSpeaker.ReadByte(port);
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PC Speaker get value {@PCSpeakerValue}", ConvertUtils.ToHex8(value));
        }
        return value;
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(PcSpeakerPortNumber, this);
        ioPortDispatcher.AddIOPortHandler(PcSpeakerOutputOnlyPortPortNumber, this);
    }

    public override void WriteByte(int port, byte value) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PC Speaker set value {@PCSpeakerValue}", ConvertUtils.ToHex8(value));
        }

        _pcSpeaker.WriteByte(port, value);
    }
}