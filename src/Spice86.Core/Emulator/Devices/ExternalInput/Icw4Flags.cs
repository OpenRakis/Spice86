namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Core.Emulator.CPU;


public sealed partial class DualPic {
    [Flags]
    private enum Icw4Flags : byte {
        Intel8086Mode = 0x01,
        AutoEoi = 0x02,
        SpecialFullyNestedMode = 0x10
    }
}
