namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems; 

/// <summary>
/// Constants for some Expanded Memory Manager subFunctions IDs.
/// </summary>
public static class EmmSubFunctions {
    public const byte UsePhysicalPageNumbers = 0x00;
    public const byte UseSegmentedAddress = 0x01;
    public const byte HandleNameGet = 0x00;
    public const byte HandleNameSet = 0x01;
    public const byte GetUnallocatedRawPages = 0x01;
    public const byte GetHardwareConfigurationArray = 0x00;
}