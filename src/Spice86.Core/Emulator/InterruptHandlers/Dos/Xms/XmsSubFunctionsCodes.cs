namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

/// <summary>
/// XMS subfunction codes as defined by the eXtended Memory Specification (XMS) version 3.0.
/// These codes are used in the AH register to select the desired XMS operation.
/// </summary>
public enum XmsSubFunctionsCodes : byte {
    GetVersionNumber = 0x00,
    RequestHighMemoryArea = 0x01,
    ReleaseHighMemoryArea = 0x02,
    GlobalEnableA20 = 0x03,
    GlobalDisableA20 = 0x04,
    LocalEnableA20 = 0x05,
    LocalDisableA20 = 0x06,
    QueryA20 = 0x07,
    QueryFreeExtendedMemory = 0x08,
    AllocateExtendedMemoryBlock = 0x09,
    FreeExtendedMemoryBlock = 0x0A,
    MoveExtendedMemoryBlock = 0x0B,
    LockExtendedMemoryBlock = 0x0C,
    UnlockExtendedMemoryBlock = 0x0D,
    GetHandleInformation = 0x0E,
    ReallocateExtendedMemoryBlock = 0x0F,
    RequestUpperMemoryBlock = 0x10,
    ReleaseUpperMemoryBlock = 0x11,
    ReallocateUpperMemoryBlock = 0x12,
    QueryAnyFreeExtendedMemory = 0x88,
    AllocateAnyExtendedMemory = 0x89,
    GetExtendedEmbHandle = 0x8E,
    ReallocateAnyExtendedMemory = 0x8F,
}
