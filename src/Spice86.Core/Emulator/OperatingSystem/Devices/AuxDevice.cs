namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

using System.IO;

public class AuxDevice : CharacterDevice {
    public AuxDevice(ILoggerService loggerService,
        ushort strategy = 0, ushort interrupt = 0)
        : base(loggerService, DeviceAttributes.Character, "AUX",
            strategy, interrupt) {
    }

    public override string Name => "AUX";

    public const string Alias = "COM1";

    public override ushort Information { get; }
    public override bool CanRead { get; }
    public override bool CanSeek { get; }
    public override bool CanWrite { get; }
    public override long Length { get; }
    public override long Position { get; set; }

    public override void Flush() {
        if (Logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            Logger.Warning("Flushing {@Device}", this);
        }
        // No-op for aux device
    }

    public override int Read(byte[] buffer, int offset, int count) {
        // No-op for aux device
        if (Logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            Logger.Warning("Reading {@Device}", this);
        }
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin) {
        if (Logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            Logger.Warning("Seeking {@Device}", this);
        }
        return 0;
    }

    public override void SetLength(long value) {
        if (Logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            Logger.Warning("Setting length {@Device}", this);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) {
        if (Logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            Logger.Warning("Writing {@Device}", this);
        }
    }
}
