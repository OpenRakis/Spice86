namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

/// <summary>
/// XMS subfunction codes as defined by the eXtended Memory Specification (XMS) version 3.0.
/// These codes are used in the AH register to select the desired XMS operation.
/// </summary>
public enum XmsSubFunctionsCodes : byte {
    /// <summary>
    /// Get XMS Version Number.
    /// Returns the XMS version, driver revision, and HMA existence.
    /// </summary>
    GetVersionNumber = 0x00,

    /// <summary>
    /// Request High Memory Area (HMA).
    /// Attempts to reserve the 64K-16 byte HMA for the caller.
    /// </summary>
    RequestHighMemoryArea = 0x01,

    /// <summary>
    /// Release High Memory Area (HMA).
    /// Releases the HMA, making it available for other programs.
    /// </summary>
    ReleaseHighMemoryArea = 0x02,

    /// <summary>
    /// Global Enable A20.
    /// Attempts to enable the A20 line globally.
    /// </summary>
    GlobalEnableA20 = 0x03,

    /// <summary>
    /// Global Disable A20.
    /// Attempts to disable the A20 line globally.
    /// </summary>
    GlobalDisableA20 = 0x04,

    /// <summary>
    /// Local Enable A20.
    /// Increments the local A20 enable count and enables A20 if needed.
    /// </summary>
    LocalEnableA20 = 0x05,

    /// <summary>
    /// Local Disable A20.
    /// Decrements the local A20 enable count and disables A20 if needed.
    /// </summary>
    LocalDisableA20 = 0x06,

    /// <summary>
    /// Query A20.
    /// Checks if the A20 line is physically enabled.
    /// </summary>
    QueryA20 = 0x07,

    /// <summary>
    /// Query Free Extended Memory.
    /// Returns the size of the largest free block and total free memory in K-bytes.
    /// </summary>
    QueryFreeExtendedMemory = 0x08,

    /// <summary>
    /// Allocate Extended Memory Block.
    /// Allocates a block of extended memory of the requested size.
    /// </summary>
    AllocateExtendedMemoryBlock = 0x09,

    /// <summary>
    /// Free Extended Memory Block.
    /// Frees a previously allocated extended memory block.
    /// </summary>
    FreeExtendedMemoryBlock = 0x0A,

    /// <summary>
    /// Move Extended Memory Block.
    /// Moves a block of memory as described by the Extended Memory Move Structure at DS:SI.
    /// </summary>
    MoveExtendedMemoryBlock = 0x0B,

    /// <summary>
    /// Lock Extended Memory Block.
    /// Locks a block and returns its 32-bit linear address.
    /// </summary>
    LockExtendedMemoryBlock = 0x0C,

    /// <summary>
    /// Unlock Extended Memory Block.
    /// Unlocks a previously locked block.
    /// </summary>
    UnlockExtendedMemoryBlock = 0x0D,

    /// <summary>
    /// Get Handle Information.
    /// Returns lock count, free handles, and block size for a handle.
    /// </summary>
    GetHandleInformation = 0x0E,

    /// <summary>
    /// Reallocate Extended Memory Block.
    /// Changes the size of an unlocked extended memory block.
    /// </summary>
    ReallocateExtendedMemoryBlock = 0x0F,

    /// <summary>
    /// Request Upper Memory Block (UMB).
    /// Attempts to allocate a UMB of the requested size.
    /// </summary>
    RequestUpperMemoryBlock = 0x10,

    /// <summary>
    /// Release Upper Memory Block (UMB).
    /// Releases a previously allocated UMB.
    /// </summary>
    ReleaseUpperMemoryBlock = 0x11,

    /// <summary>
    /// Reallocate Upper Memory Block (UMB).
    /// Attempts to reallocate a UMB to a new size.
    /// </summary>
    ReallocateUpperMemoryBlock = 0x12,

    /// <summary>
    /// Query Any Free Extended Memory (386+ only).
    /// Returns the size of the largest free block and total free memory in K-bytes using 32-bit values.
    /// </summary>
    QueryAnyFreeExtendedMemory = 0x88,

    /// <summary>
    /// Allocate Any Extended Memory (386+ only).
    /// Allocates a block of extended memory of the requested size using 32-bit values.
    /// </summary>
    AllocateAnyExtendedMemory = 0x89
}
