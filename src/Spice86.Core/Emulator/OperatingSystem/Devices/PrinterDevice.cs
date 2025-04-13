namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

public class PrinterDevice : NullDevice {
    private readonly Dos _dos;
    public PrinterDevice(ILoggerService loggerService, Dos dos,
        ushort strategy = 0, ushort interrupt = 0)
        : base(loggerService, DeviceAttributes.Character, strategy, interrupt) {
        _dos = dos;
    }

    public override string Name => "LPT1";

    public const string Alias = "PRN";

    public override ushort Information => 0x80A0;

    public override int Read(byte[] buffer, int offset, int count) {
        _dos.ErrorCode = ErrorCode.AccessDenied;
        return base.Read(buffer, offset, count);
    }
}
