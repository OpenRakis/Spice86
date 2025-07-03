    namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;

/// <summary>
/// Error codes returned by XMS functions in the BL register when AX=0000h and the high bit of BL is set.
/// These codes are defined by the eXtended Memory Specification (XMS) version 3.0.
/// </summary>
public enum XmsErrorCodes : byte {
    /// <summary>
    /// The requested function is not implemented by the XMS driver.
    /// </summary>
    NotImplemented = 0x80,

    /// <summary>
    /// A VDISK device is detected in the system, which is incompatible with XMS operations.
    /// </summary>
    VdiskDetected = 0x81,

    /// <summary>
    /// An error occurred while manipulating the A20 line.
    /// </summary>
    A20Error = 0x82,

    /// <summary>
    /// A general driver error has occurred.
    /// </summary>
    GeneralDriverError = 0x8E,

    /// <summary>
    /// An unrecoverable driver error has occurred.
    /// </summary>
    UnrecoverableDriverError = 0x8F,

    /// <summary>
    /// The High Memory Area (HMA) does not exist on this system.
    /// </summary>
    HmaDoesNotExist = 0x90,

    /// <summary>
    /// The High Memory Area (HMA) is already in use by another program or driver.
    /// </summary>
    HmaAlreadyInUse = 0x91,

    /// <summary>
    /// The requested HMA size (DX) is less than the /HMAMIN= parameter specified at driver load.
    /// </summary>
    HmaSizeTooSmall = 0x92,

    /// <summary>
    /// The High Memory Area (HMA) is not currently allocated.
    /// </summary>
    HmaNotAllocated = 0x93,

    /// <summary>
    /// The A20 line is still enabled when it should be disabled.
    /// </summary>
    A20StillEnabled = 0x94,

    /// <summary>
    /// All available extended memory is currently allocated; no free memory blocks remain.
    /// </summary>
    AllExtendedMemoryAllocated = 0xA0,

    /// <summary>
    /// All available extended memory handles are in use; no new handles can be allocated.
    /// </summary>
    AllHandlesInUse = 0xA1,

    /// <summary>
    /// The specified handle is invalid or does not refer to an allocated memory block.
    /// </summary>
    InvalidHandle = 0xA2,

    /// <summary>
    /// The source handle specified in a memory move operation is invalid.
    /// </summary>
    InvalidSourceHandle = 0xA3,

    /// <summary>
    /// The source offset specified in a memory move operation is invalid.
    /// </summary>
    InvalidSourceOffset = 0xA4,

    /// <summary>
    /// The destination handle specified in a memory move operation is invalid.
    /// </summary>
    InvalidDestinationHandle = 0xA5,

    /// <summary>
    /// The destination offset specified in a memory move operation is invalid.
    /// </summary>
    InvalidDestinationOffset = 0xA6,

    /// <summary>
    /// The length specified in a memory move operation is invalid.
    /// </summary>
    InvalidLength = 0xA7,

    /// <summary>
    /// The source and destination regions in a memory move operation have an invalid overlap.
    /// </summary>
    InvalidOverlap = 0xA8,

    /// <summary>
    /// A parity error occurred during a memory operation.
    /// </summary>
    ParityError = 0xA9,

    /// <summary>
    /// The specified memory block is not currently locked.
    /// </summary>
    BlockNotLocked = 0xAA,

    /// <summary>
    /// The specified memory block is currently locked and cannot be freed or reallocated.
    /// </summary>
    BlockLocked = 0xAB,

    /// <summary>
    /// The block's lock count has reached its maximum value and cannot be incremented further.
    /// </summary>
    LockCountOverflow = 0xAC,

    /// <summary>
    /// The attempt to lock the memory block failed.
    /// </summary>
    LockFailed = 0xAD,

    /// <summary>
    /// A smaller Upper Memory Block (UMB) is available than requested.
    /// </summary>
    SmallerUmbAvailable = 0xB0,

    /// <summary>
    /// No Upper Memory Blocks (UMBs) are available for allocation.
    /// </summary>
    NoUmbsAvailable = 0xB1,

    /// <summary>
    /// The specified UMB segment number is invalid.
    /// </summary>
    InvalidUmbSegment = 0xB2
}
