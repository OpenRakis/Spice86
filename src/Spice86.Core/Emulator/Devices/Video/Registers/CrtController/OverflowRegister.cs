namespace Spice86.Core.Emulator.Devices.Video.Registers.CrtController;

/// <summary>
/// Represents the 8 bit Overflow register.
/// </summary>
public class OverflowRegister : Register8 {
    public int VerticalTotal89 => (GetBit(0) ? 1 << 8 : 0) | (GetBit(5) ? 1 << 9 : 0);
    public int VerticalDisplayEnd89 => (GetBit(1) ? 1 << 8 : 0) | (GetBit(6) ? 1 << 9 : 0);
    public int VerticalSyncStart89 => (GetBit(2) ? 1 << 8 : 0) | (GetBit(7) ? 1 << 9 : 0);
    public int VerticalBlankingStart8 => GetBit(3) ? 1 << 8 : 0;
    public int LineCompare8 => GetBit(4) ? 1 << 8 : 0;
}