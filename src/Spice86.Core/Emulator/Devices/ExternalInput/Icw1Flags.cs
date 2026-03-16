namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Core.Emulator.CPU;


public sealed partial class DualPic {
    [Flags]
    private enum Icw1Flags : byte {
        RequireIcw4 = 0x01,
        SingleMode = 0x02,
        FourByteInterval = 0x04,
        LevelTriggered = 0x08,
        Initialization = 0x10,
        ProcessorModeMask = 0xe0
    }
}
