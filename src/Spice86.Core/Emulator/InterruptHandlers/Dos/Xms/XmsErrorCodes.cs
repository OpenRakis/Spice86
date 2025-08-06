namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

/// <summary>
/// XMS error codes as defined by the eXtended Memory Specification (XMS) version 3.0.
/// These error codes are returned in BL register when an XMS function fails (AX=0000h).
/// All error codes have their high bit set (values 80h-FFh).
/// </summary>
public enum XmsErrorCodes : byte {
    /// <summary>
    /// Operation completed successfully.
    /// This value (0) indicates no error occurred during the XMS operation.
    /// This is not actually returned as an error code, but is used internally
    /// to represent successful operations.
    /// </summary>
    Ok = 0x00,
    
    /// <summary>
    /// Function not implemented.
    /// Returned when a requested XMS function is not supported by the XMS driver.
    /// Only UMB functions are optional.
    /// </summary>
    NotImplemented = 0x80,
    
    /// <summary>
    /// VDISK device detected.
    /// Returned when a VDISK device is detected in the system, which may
    /// conflict with XMS operations. VDISK was an early disk-caching
    /// utility that used extended memory.
    /// </summary>
    VDiskDetected = 0x81,
    
    /// <summary>
    /// A20 error.
    /// Returned when an error occurs while attempting to enable or disable
    /// the A20 address line. This may be due to hardware limitations
    /// or conflicts with other software controlling the A20 line.
    /// </summary>
    A20LineError = 0x82,
    
    /// <summary>
    /// General driver error.
    /// A general, non-specific error occurred in the XMS driver.
    /// This is typically a catch-all for errors not covered by other codes.
    /// </summary>
    GeneralDriverError = 0x8e,
    
    /// <summary>
    /// HMA does not exist.
    /// Returned when a High Memory Area operation is attempted but the
    /// HMA is not available on the system. This could be due to hardware 
    /// limitations or configuration issues.
    /// </summary>
    HmaDoesNotExist = 0x90,
    
    /// <summary>
    /// HMA is already in use.
    /// Returned when a program attempts to allocate the HMA (Function 01h)
    /// but another program has already allocated it. The HMA can only be
    /// used by one program at a time.
    /// </summary>
    HmaInUse = 0x91,
    
    /// <summary>
    /// HMA requested size is too small.
    /// Returned when the size requested for the HMA allocation (in DX register)
    /// is less than the minimum size specified by the /HMAMIN= parameter in
    /// the XMS driver configuration. This helps ensure efficient use of the HMA.
    /// </summary>
    HmaRequestNotBigEnough = 0x92, 
    
    /// <summary>
    /// HMA not allocated.
    /// Returned when an operation that requires the HMA to be allocated
    /// (such as Function 02h - Release HMA) is attempted, but the HMA
    /// has not been allocated to the caller.
    /// </summary>
    HmaNotAllocated = 0x93,
    
    /// <summary>
    /// A20 line still enabled.
    /// Returned when attempting to disable the A20 line (Function 04h or 06h)
    /// but the operation fails and the A20 line remains enabled.
    /// This may be due to hardware issues or other software keeping A20 enabled.
    /// </summary>
    A20StillEnabled = 0x94,
    
    /// <summary>
    /// All extended memory is allocated.
    /// Returned when attempting to allocate extended memory (Functions 09h or 89h)
    /// but there is no free extended memory available in the system.
    /// </summary>
    XmsOutOfMemory = 0xA0,
    
    /// <summary>
    /// All available extended memory handles are in use.
    /// Returned when attempting to allocate extended memory (Functions 09h or 89h)
    /// but the XMS driver has reached its limit of available handles.
    /// XMS drivers have a finite number of handles (typically 32-64).
    /// </summary>
    XmsOutOfHandles = 0xA1,
    
    /// <summary>
    /// Invalid handle.
    /// Returned when an operation is attempted with an invalid or 
    /// already-freed extended memory handle. This can happen with
    /// functions that require a valid handle (0Ah, 0Ch, 0Dh, 0Eh, 0Fh).
    /// </summary>
    XmsInvalidHandle = 0xA2,
    
    /// <summary>
    /// Invalid source handle.
    /// Returned by the Move Extended Memory Block function (0Bh) when
    /// the source handle specified in the move structure is invalid.
    /// </summary>
    XmsInvalidSrcHandle = 0xA3,
    
    /// <summary>
    /// Invalid source offset.
    /// Returned by the Move Extended Memory Block function (0Bh) when
    /// the source offset specified in the move structure is beyond the
    /// bounds of the source memory block.
    /// </summary>
    XmsInvalidSrcOffset = 0xA4,
    
    /// <summary>
    /// Invalid destination handle.
    /// Returned by the Move Extended Memory Block function (0Bh) when
    /// the destination handle specified in the move structure is invalid.
    /// </summary>
    XmsInvalidDestHandle = 0xA5,
    
    /// <summary>
    /// Invalid destination offset.
    /// Returned by the Move Extended Memory Block function (0Bh) when
    /// the destination offset specified in the move structure is beyond
    /// the bounds of the destination memory block.
    /// </summary>
    XmsInvalidDestOffset = 0xA6,
    
    /// <summary>
    /// Invalid length.
    /// Returned by the Move Extended Memory Block function (0Bh) when
    /// the length specified in the move structure is invalid (e.g., zero,
    /// not even, or exceeds source or destination block boundaries).
    /// </summary>
    XmsInvalidLength = 0xA7,
    
    /// <summary>
    /// Invalid memory block overlap.
    /// Returned by the Move Extended Memory Block function (0Bh) when
    /// the source and destination regions overlap in a way that would
    /// cause data corruption during the move operation.
    /// </summary>
    XmsInvalidOverlap = 0xA8,
    
    /// <summary>
    /// Parity error.
    /// Returned when a memory parity error is detected during an XMS operation.
    /// This indicates a hardware problem with the memory.
    /// </summary>
    XmsParityError = 0xA9,
    
    /// <summary>
    /// Block not locked.
    /// Returned when attempting to unlock a block (Function 0Dh) that
    /// is not currently locked. Each lock operation must be paired with
    /// a corresponding unlock operation.
    /// </summary>
    XmsBlockNotLocked = 0xAA,
    
    /// <summary>
    /// Block locked.
    /// Returned when attempting to perform an operation that requires
    /// an unlocked block (such as Function 0Fh - Reallocate) on a
    /// locked memory block. The block must be unlocked first.
    /// </summary>
    XmsBlockLocked = 0xAB,
    
    /// <summary>
    /// Lock count overflow.
    /// Returned when attempting to lock a block (Function 0Ch) that
    /// has already been locked the maximum number of times (lock count
    /// would overflow). XMS typically uses a 16-bit lock counter.
    /// </summary>
    XmsLockCountOverflow = 0xAC,
    
    /// <summary>
    /// Lock failed.
    /// Returned when a block lock operation (Function 0Ch) fails for
    /// a reason other than a lock count overflow.
    /// </summary>
    XmsLockFailed = 0xAD,
    
    /// <summary>
    /// UMB only smaller block available.
    /// Returned when requesting an Upper Memory Block (Function 10h) but
    /// only a smaller block than requested is available. The largest
    /// available size is returned in DX.
    /// </summary>
    UmbOnlySmallerBlock = 0xB0,
    
    /// <summary>
    /// No UMBs available.
    /// Returned when requesting an Upper Memory Block (Function 10h) but
    /// no upper memory blocks are available in the system. The system may
    /// not support UMBs at all, or all UMBs may already be allocated.
    /// </summary>
    UmbNoBlocksAvailable = 0xB1,
    
    /// <summary>
    /// Invalid UMB segment.
    /// Returned when attempting to release or reallocate an Upper Memory Block
    /// (Functions 11h or 12h) using an invalid segment address. The specified
    /// segment does not correspond to a currently allocated UMB.
    /// </summary>
    UmbInvalidSegment = 0xB2
}