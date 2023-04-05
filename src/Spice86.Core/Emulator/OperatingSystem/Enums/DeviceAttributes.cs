namespace Spice86.Core.Emulator.OperatingSystem.Enums;

[Flags]
public enum DeviceAttributes {
    CurrentStdin = 0x1,
    CurrentStdout = 0x2,
    CurrentNull = 0x4,
    CurrentClock = 0x8,
    Special = 0x10,
    FatDevice = 0x2000,
    Ioctl = 0x4000,
    Character = 0x8000
}