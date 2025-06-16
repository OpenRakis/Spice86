namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Serilog.Events;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

using System.IO;

public class PrinterDevice : CharacterDevice {

    public PrinterDevice(ILoggerService loggerService,
        ushort strategy = 0, ushort interrupt = 0)
        : base(loggerService, DeviceAttributes.Character, "LPT1", strategy, interrupt) {
    }

    public override string Name => "LPT1";

    public const string Alias = "PRN";

    public override ushort Information => 0x80A0;

    public override bool CanSeek => false;

    public override long Length => 0;

    public override long Position { get; set; } = 0;

    public override bool CanRead =>  false;

    public override bool CanWrite => true;

    public override void Write(byte[] buffer, int offset, int count) {
        string output = System.Text.Encoding.ASCII.GetString(buffer, offset, count);
        if(Logger.IsEnabled(LogEventLevel.Information)) {
            Logger.Information("Writing to printer: {Output}", output);
        }
    }

    public override void Flush() {
        if (Logger.IsEnabled(LogEventLevel.Information)) {
            Logger.Information("Flushing printer");
        }
    }

    public override int Read(byte[] buffer, int offset, int count) {
        if (Logger.IsEnabled(LogEventLevel.Information)) {
            Logger.Information("Reading printer");
        }
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin) {
        if (Logger.IsEnabled(LogEventLevel.Information)) {
            Logger.Information("Seeking printer");
        }
        return 0;
    }

    public override void SetLength(long value) {
        return;
    }
}
