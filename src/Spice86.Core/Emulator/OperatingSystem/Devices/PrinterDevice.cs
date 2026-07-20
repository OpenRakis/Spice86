namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Interfaces;

using System.IO;

public class PrinterDevice : CharacterDevice {
    private const string LPT1 = "LPT1";
    private readonly ILoggerService _loggerService;

    public PrinterDevice(ILoggerService loggerService, IByteReaderWriter memory,
        uint baseAddress)
        : base(memory, baseAddress, LPT1) {
        _loggerService = loggerService;
    }

    public override string Name => LPT1;

    public override string Alias => "PRN";

    public override ushort Information => 0x80A0;

    public override bool CanSeek => false;

    public override long Length => 0;

    public override long Position { get; set; } = 0;

    public override bool CanRead =>  false;

    public override bool CanWrite => true;

    public override void Write(byte[] buffer, int offset, int count) {
        string output = System.Text.Encoding.ASCII.GetString(buffer, offset, count);
        if(_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation("Writing to printer: {Output}", output);
        }
    }

    public override void Flush() {
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation("Flushing printer");
        }
    }

    public override int Read(byte[] buffer, int offset, int count) {
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation("Reading printer");
        }
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin) {
        if (_loggerService.IsEnabled(LogLevel.Information)) {
            _loggerService.LogInformation("Seeking printer");
        }
        return 0;
    }

    public override void SetLength(long value) {
        return;
    }
}
