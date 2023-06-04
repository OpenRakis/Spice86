namespace Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;

public enum VgaPort {
    AttributeAddress = 0x3C0,
    AttributeData = 0x3C1,
    MiscOutputWrite = 0x3C2,
    SequencerAddress = 0x3C4,
    DacPelMask = 0x3C6,
    DacAddressReadIndex = 0x3C7,
    DacAddressWriteIndex = 0x3C8,
    DacData = 0x3C9,
    MiscOutputRead = 0x3CC,
    GraphicsControllerAddress = 0x3CE,
    CrtControllerAddress = 0x3B4,
    CrtControllerAddressAlt = 0x3D4,
    InputStatus1ReadAlt = 0x3DA
}