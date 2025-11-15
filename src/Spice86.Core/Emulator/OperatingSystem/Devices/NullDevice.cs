namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Serilog.Events;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using System.Diagnostics.CodeAnalysis;
using System.IO;

public class NullDevice : VirtualDeviceBase {
    private const string NUL = "NUL";
    private readonly ILoggerService _loggerService;
    public NullDevice(ILoggerService loggerService, IByteReaderWriter memory, uint baseAddress)
        : base(new DosDeviceHeader(memory, baseAddress) {
            Attributes = Enums.DeviceAttributes.CurrentNull,
            Name = NUL,
            NextDevicePointer = new Spice86.Shared.Emulator.Memory.SegmentedAddress(0xFFFF, 0xFFFF)
        }) {
        _loggerService = loggerService;
    }

    public override string Name => NUL;
    public override ushort Information => 0x8084;
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => 0;
    public override long Position { get; set; }

    public override void Flush() {
        // No-op for null device
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Flushing {@Device}", this);
        }
    }

    public override int Read(byte[] buffer, int offset, int count) {
        // No-op for null device
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Reading {@Device}", this);
        }
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin) {
        // No-op for null device
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Seeking {@Device}", this);
        }
        return 0;
    }

    public override void SetLength(long value) {
        // No-op for null device
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Setting length {@Device}", this);
        }
    }

    public override bool TryReadFromControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Reading from control channel of {@Device}", this);
        }

        returnCode = null;
        return false;
    }

    public override bool TryWriteToControlChannel(uint address, ushort size,
        [NotNullWhen(true)] out ushort? returnCode) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Writing to control channel of {@Device}", this);
        }
        returnCode = null;
        return false;
    }

    public override void Write(byte[] buffer, int offset, int count) {
        // No-op for null device
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Writing {@Device}", this);
        }
    }
}