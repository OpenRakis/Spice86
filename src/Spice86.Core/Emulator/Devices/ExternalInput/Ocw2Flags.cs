namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Core.Emulator.CPU;


public sealed partial class DualPic {
    [Flags]
    private enum Ocw2Flags : byte {
        EndOfInterrupt = 0x20,
        Specific = 0x40,
        Rotate = 0x80
    }
}
