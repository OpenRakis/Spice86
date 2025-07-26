namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

/// <summary>
/// XMS subfunction codes as defined by the eXtended Memory Specification (XMS) version 3.0.
/// These codes are used in the AH register to select the desired XMS operation.
/// </summary>
public enum XmsSubFunctionsCodes : byte {
    /// <summary>
    /// Get XMS Version Number (Function 00h).
    /// Returns the XMS version number (in AX as a 16-bit BCD value, e.g. 0300h for version 3.00),
    /// the driver's internal revision number (in BX), and whether the HMA exists (in DX).
    /// This call never fails and doesn't change the A20 line status.
    /// </summary>
    GetVersionNumber = 0x00,

    /// <summary>
    /// Request High Memory Area (Function 01h).
    /// Attempts to reserve the 64K-16 byte HMA for the caller.
    /// Call with DX = space needed in bytes for TSRs/drivers or FFFFh for applications.
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// If successful, the caller has exclusive access to the HMA and can 
    /// use Global Enable A20 (Function 03h) to access it.
    /// </summary>
    RequestHighMemoryArea = 0x01,

    /// <summary>
    /// Release High Memory Area (Function 02h).
    /// Releases the HMA, making it available for other programs.
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// Programs must release the HMA before exiting, and any code or data
    /// in the HMA becomes invalid after release.
    /// </summary>
    ReleaseHighMemoryArea = 0x02,

    /// <summary>
    /// Global Enable A20 (Function 03h).
    /// Enables the A20 address line, providing access to the HMA.
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// This function should only be used by programs that have control of the HMA.
    /// On many machines, toggling the A20 line is a relatively slow operation.
    /// </summary>
    GlobalEnableA20 = 0x03,

    /// <summary>
    /// Global Disable A20 (Function 04h).
    /// Disables the A20 address line.
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// This function should only be used by programs that have control of the HMA.
    /// The A20 line should be disabled before a program releases control of the system.
    /// On many machines, toggling the A20 line is a relatively slow operation.
    /// </summary>
    GlobalDisableA20 = 0x04,

    /// <summary>
    /// Local Enable A20 (Function 05h).
    /// Increments the local A20 enable count and enables the A20 address line if needed.
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// This function should only be used by programs which need direct access to extended memory.
    /// Each call must be balanced with a corresponding Local Disable A20 call.
    /// On many machines, toggling the A20 line is a relatively slow operation.
    /// </summary>
    LocalEnableA20 = 0x05,

    /// <summary>
    /// Local Disable A20 (Function 06h).
    /// Decrements the local A20 enable count and disables the A20 address line if the count reaches zero.
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// This function cancels a previous Local Enable A20 call.
    /// Previous calls to Function 05h must be canceled before releasing control of the system.
    /// On many machines, toggling the A20 line is a relatively slow operation.
    /// </summary>
    LocalDisableA20 = 0x06,

    /// <summary>
    /// Query A20 (Function 07h).
    /// Checks if the A20 line is physically enabled by testing for memory wrap.
    /// Returns the A20 status in AX (0001h if enabled, 0000h if disabled).
    /// BL contains 00h if the function succeeds.
    /// </summary>
    QueryA20 = 0x07,

    /// <summary>
    /// Query Free Extended Memory (Function 08h).
    /// Returns information about available extended memory.
    /// AX = Size of the largest free extended memory block in K-bytes
    /// DX = Total amount of free extended memory in K-bytes
    /// Note: The 64K HMA is not included in the returned values even if it is not in use.
    /// </summary>
    QueryFreeExtendedMemory = 0x08,

    /// <summary>
    /// Allocate Extended Memory Block (Function 09h).
    /// Allocates a block of extended memory of the requested size.
    /// Call with DX = Amount of extended memory being requested in K-bytes.
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// DX = 16-bit handle to the allocated block if successful.
    /// Extended memory handles are scarce resources - programs should 
    /// try to allocate as few blocks as possible.
    /// </summary>
    AllocateExtendedMemoryBlock = 0x09,

    /// <summary>
    /// Free Extended Memory Block (Function 0Ah).
    /// Frees a previously allocated extended memory block.
    /// Call with DX = Handle of the memory block to free.
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// Programs must free all allocated memory blocks before exiting.
    /// After freeing a block, its handle and data become invalid.
    /// </summary>
    FreeExtendedMemoryBlock = 0x0A,

    /// <summary>
    /// Move Extended Memory Block (Function 0Bh).
    /// Moves a block of data between memory locations as described by the Extended Memory Move Structure at DS:SI.
    /// The structure defines source, destination, and length of the transfer.
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// Can move data between conventional memory and extended memory or within either area.
    /// If source/destination handle is 0000h, the offset is interpreted as a segment:offset pair.
    /// The function provides reasonable interrupt windows during long transfers.
    /// </summary>
    MoveExtendedMemoryBlock = 0x0B,

    /// <summary>
    /// Lock Extended Memory Block (Function 0Ch).
    /// Locks an extended memory block and returns its 32-bit physical address.
    /// Call with DX = Extended memory block handle to lock.
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// DX:BX = 32-bit physical address of the locked block if successful.
    /// Locked blocks are guaranteed not to move, and the physical address is
    /// only valid while the block is locked. Blocks maintain a lock count.
    /// </summary>
    LockExtendedMemoryBlock = 0x0C,

    /// <summary>
    /// Unlock Extended Memory Block (Function 0Dh).
    /// Unlocks a previously locked extended memory block.
    /// Call with DX = Extended memory block handle to unlock.
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// Any 32-bit pointers into the block become invalid and should no longer be used.
    /// Decrements the block's lock count.
    /// </summary>
    UnlockExtendedMemoryBlock = 0x0D,

    /// <summary>
    /// Get Handle Information (Function 0Eh).
    /// Returns information about an extended memory block.
    /// Call with DX = Extended memory block handle.
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// BH = The block's lock count
    /// BL = Number of free EMB handles in the system
    /// DX = The block's length in K-bytes
    /// To get the block's base address, use Lock Extended Memory Block (Function 0Ch).
    /// </summary>
    GetHandleInformation = 0x0E,

    /// <summary>
    /// Reallocate Extended Memory Block (Function 0Fh).
    /// Changes the size of an unlocked extended memory block.
    /// Call with BX = New size for the extended memory block in K-bytes
    ///           DX = Extended memory block handle to reallocate
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// If the new size is smaller than the old block's size, all data at the upper end is lost.
    /// The block must be unlocked before it can be reallocated.
    /// </summary>
    ReallocateExtendedMemoryBlock = 0x0F,

    /// <summary>
    /// Request Upper Memory Block (Function 10h).
    /// Attempts to allocate a UMB of the requested size.
    /// Call with DX = Size of requested memory block in paragraphs
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// If successful, BX = Segment number of the allocated UMB.
    /// DX = Actual size of allocated block in paragraphs (may be larger than requested).
    /// This function is optional in XMS 3.0 and may return Function Not Implemented (80h).
    /// </summary>
    RequestUpperMemoryBlock = 0x10,

    /// <summary>
    /// Release Upper Memory Block (Function 11h).
    /// Releases a previously allocated UMB.
    /// Call with DX = Segment number of the UMB to release
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// This function is optional in XMS 3.0 and may return Function Not Implemented (80h).
    /// </summary>
    ReleaseUpperMemoryBlock = 0x11,

    /// <summary>
    /// Reallocate Upper Memory Block (Function 12h).
    /// Attempts to resize a previously allocated UMB.
    /// Call with BX = New size in paragraphs
    ///           DX = Segment of the UMB to resize
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// If successful, BX = Segment of the UMB (which may have moved)
    ///                DX = Actual size in paragraphs (may be larger than requested)
    /// This function is optional in XMS 3.0 and may return Function Not Implemented (80h).
    /// </summary>
    ReallocateUpperMemoryBlock = 0x12,

    /// <summary>
    /// Query Any Free Extended Memory (Function 88h) - 386+ only.
    /// Returns extended memory availability using 32-bit values.
    /// Returns in AX = 0001h if successful, 0000h if failed
    /// EDX:EAX = Size of largest free block in bytes (if successful)
    /// ECX:EBX = Total free memory in bytes (if successful)
    /// Similar to function 08h but returns 32-bit byte values instead of 16-bit K-byte values.
    /// </summary>
    QueryAnyFreeExtendedMemory = 0x88,

    /// <summary>
    /// Allocate Any Extended Memory (Function 89h) - 386+ only.
    /// Allocates a block of extended memory of the requested size using 32-bit values.
    /// Call with EDX:EAX = Memory size requested in bytes
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// If successful, DX = 16-bit handle for the allocated block
    ///               ECX:EBX = Actual size allocated in bytes (may be larger than requested)
    /// Similar to function 09h but takes a 32-bit byte size instead of a 16-bit K-byte size.
    /// </summary>
    AllocateAnyExtendedMemory = 0x89,

    /// <summary>
    /// Get Extended EMB Handle (Function 8Eh).
    /// Gets a handle for an extended memory block at a specified physical address.
    /// This function is primarily intended for system diagnostic and debugging utilities.
    /// Usage and parameters may vary between XMS providers.
    /// </summary>
    GetExtendedEmbHandle = 0x8E,

    /// <summary>
    /// Reallocate Any Extended Memory (Function 8Fh) - 386+ only.
    /// Changes the size of an unlocked extended memory block using 32-bit values.
    /// Call with EDX:EAX = New size for the block in bytes
    ///           DX = Extended memory block handle to reallocate
    /// Returns success in AX (0001h if successful, 0000h if failed).
    /// If successful, ECX:EBX = Actual new size in bytes (may be larger than requested)
    /// Similar to function 0Fh but uses 32-bit byte values instead of 16-bit K-byte values.
    /// </summary>
    ReallocateAnyExtendedMemory = 0x8F,
}
