namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

public class PrinterDevice : NullDevice {
    public PrinterDevice(ILoggerService loggerService,
        ushort strategy = 0, ushort interrupt = 0)
        : base(loggerService, DeviceAttributes.Character, strategy, interrupt) {
    }

    public override string Name => "LPT1";

    public const string Alias = "PRN";

    public override ushort Information => 0x80A0;

    public override bool CanRead =>  false;

    public override int Read(byte[] buffer, int offset, int count) {
        return base.Read(buffer, offset, count);
    }
}
