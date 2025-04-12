namespace Spice86.Core.Emulator.OperatingSystem.Devices;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Interfaces;

public class PrinterDevice : NullDevice {
    private readonly State _state;
    public PrinterDevice(ILoggerService loggerService, State state,
        ushort strategy = 0, ushort interrupt = 0)
        : base(loggerService, DeviceAttributes.Character, strategy, interrupt) {
        _state = state;
    }

    public override string Name => "LPT1";

    public const string Alias = "PRN";

    public override ushort Information => 0x80A0;

    public override int Read(byte[] buffer, int offset, int count) {
        //TODO: Use DOS_SetError function (that sets DOS_Block.Error) once DOS processes management is implemented
        _state.AX = (ushort)ErrorCode.AccessDenied;
        return base.Read(buffer, offset, count);
    }
}
