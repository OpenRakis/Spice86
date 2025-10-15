namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.IO;

public class AuxDevice : CharacterDevice {
    private readonly ILoggerService _loggerService;
    public AuxDevice(ILoggerService loggerService,
        IByteReaderWriter memory, SegmentedAddress baseAddress)
        : base(memory, baseAddress, "AUX") {
        _loggerService = loggerService;
    }

    public override string Name => "AUX";

    public override string? Alias => "COM1";

    public override ushort Information { get; }
    public override bool CanRead { get; }
    public override bool CanSeek { get; }
    public override bool CanWrite { get; }
    public override long Length { get; }
    public override long Position { get; set; }

    public override void Flush() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("Flushing {@Device}", this);
        }
        // No-op for aux device
    }

    public override int Read(byte[] buffer, int offset, int count) {
        // No-op for aux device
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("Reading {@Device}", this);
        }
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("Seeking {@Device}", this);
        }
        return 0;
    }

    public override void SetLength(long value) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("Setting length {@Device}", this);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("Writing {@Device}", this);
        }
    }
}
