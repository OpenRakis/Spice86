namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Core.Emulator.CPU;


public sealed partial class DualPic {
    [Flags]
    private enum Ocw3Flags : byte {
        ReadIssr = 0x01,
        FunctionSelect = 0x02,
        Poll = 0x04,
        CommandSelect = 0x08,
        SpecialMaskEnable = 0x20,
        SpecialMaskSelect = 0x40
    }
}
