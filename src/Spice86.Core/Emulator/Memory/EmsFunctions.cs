namespace Spice86.Core.Emulator.Memory;

internal static class EmsFunctions {
    public const byte GetStatus = 0x40;
    public const byte GetPageFrameAddress = 0x41;
    public const byte GetUnallocatedPageCount = 0x42;
    public const byte AllocatePages = 0x43;
    public const byte MapUnmapHandlePage = 0x44;
    public const byte DeallocatePages = 0x45;
    public const byte GetVersion = 0x46;
    public const byte SavePageMap = 0x47;
    public const byte RestorePageMap = 0x48;
    public const byte GetHandleCount = 0x4B;
    public const byte GetHandlePages = 0x4C;
    public const byte GetAllHandlePages = 0x4D;

    public const byte AdvancedMap = 0x50;
    public const byte AdvancedMap_MapUnmapPages = 0x00;

    public const byte ReallocatePages = 0x51;

    public const byte HandleName = 0x53;
    public const byte HandleName_Get = 0x00;
    public const byte HandleName_Set = 0x01;

    public const byte GetHardwareInformation = 0x59;
    public const byte GetHardwareInformation_UnallocatedRawPages = 0x01;

    public const byte MoveExchange = 0x57;
    public const byte MoveExchange_Move = 0x00;

    public const byte VCPI = 0xDE;
    public const byte VCPI_InstallationCheck = 0x00;
    public const byte VCPI_GetProtectedModeInterface = 0x01;
    public const byte VCPI_GetPhysicalPageAddressInFirstMegabyte = 0x06;
    public const byte VCPI_GetInterruptVectorMappings = 0x0A;
    public const byte VCPI_SwitchToProtectedMode = 0x0C;
}
