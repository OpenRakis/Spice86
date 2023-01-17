namespace Spice86.Core.Emulator.Memory;

public enum EmsFunctions {
    HandleName_Get = 0x00,
    HandleName_Set = 0x01,
    VCPI_GetPhysicalPageAddressInFirstMegabyte = 0x06,
    VCPI_GetInterruptVectorMappings = 0x0A,
    VCPI_SwitchToProtectedMode = 0x0C,
    GetStatus = 0x40,
    GetPageFrameAddress = 0x41,
    GetUnallocatedPageCount = 0x42,
    AllocatePages = 0x43,
    MapUnmapHandlePage = 0x44,
    DeallocatePages = 0x45,
    GetVersion = 0x46,
    SavePageMap = 0x47,
    RestorePageMap = 0x48,
    GetHandleCount = 0x4B,
    GetHandlePages = 0x4C,
    GetAllHandlePages = 0x4D,
    AdvancedMap = 0x50,
    ReallocatePages = 0x51,
    HandleName = 0x53,
    MoveExchange = 0x57,
    GetHardwareInformation = 0x59,
    VCPI = 0xDE
}
