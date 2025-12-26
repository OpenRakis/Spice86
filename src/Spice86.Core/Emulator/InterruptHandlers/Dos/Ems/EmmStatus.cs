namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// Status codes returned by the Expanded Memory Manager in the AH register.
/// These codes follow the LIM EMS specification for error and success reporting.
/// Status values 00h indicates success; values 80h-8Fh indicate various error conditions.
/// </summary>
public static class EmmStatus {
    /// <summary>
    /// The function completed successfully (status 00h).
    /// </summary>
    public const byte EmmNoError = 0x00;
    
    /// <summary>
    /// EMM software malfunction (status 80h).
    /// Returned when an internal EMM error occurs.
    /// </summary>
    public const byte EmmSoftwareMalfunction = 0x80;
    
    /// <summary>
    /// EMM hardware malfunction (status 81h).
    /// Returned when the EMM detects a hardware failure.
    /// </summary>
    public const byte EmmHardwareMalfunction = 0x81;
    
    /// <summary>
    /// EMM busy (status 82h).
    /// The EMM is currently being used by another process.
    /// </summary>
    public const byte EmmBusy = 0x82;
    
    /// <summary>
    /// Invalid handle (status 83h).
    /// The EMM handle number was not recognized or is not currently allocated.
    /// </summary>
    public const byte EmmInvalidHandle = 0x83;
    
    /// <summary>
    /// Function not defined (status 84h).
    /// The EMM function code was not recognized or is not implemented.
    /// </summary>
    public const byte EmmFunctionNotSupported = 0x84;
    
    /// <summary>
    /// No more handles available (status 85h).
    /// All EMM handles are currently in use. Maximum is typically 255 handles.
    /// </summary>
    public const byte EmmOutOfHandles = 0x85;
    
    /// <summary>
    /// Save/restore page map error (status 86h).
    /// An error occurred during save or restore page map operations.
    /// This may indicate a context conflict.
    /// </summary>
    public const byte EmmSaveMapError = 0x86;
    
    /// <summary>
    /// Not enough pages (status 87h).
    /// The EMM does not have enough free pages to satisfy the allocation request.
    /// </summary>
    public const byte EmmNotEnoughPages = 0x87;
    
    /// <summary>
    /// Not enough pages for requested count (status 88h).
    /// Similar to 87h but used in specific allocation contexts.
    /// </summary>
    public const byte EmmNotEnoughPagesForCount = 0x88;
    
    /// <summary>
    /// Zero pages requested (status 89h).
    /// The application tried to allocate zero logical pages to a handle.
    /// Some EMS functions require at least one page.
    /// </summary>
    public const byte EmmTriedToAllocateZeroPages = 0x89;
    
    /// <summary>
    /// Logical page out of range (status 8Ah).
    /// The logical page number is outside the range of pages allocated to the handle.
    /// </summary>
    public const byte EmmLogicalPageOutOfRange = 0x8a;
    
    /// <summary>
    /// Illegal physical page (status 8Bh).
    /// The physical page number is outside the valid range (typically 0-3 for the 64KB page frame).
    /// </summary>
    public const byte EmmIllegalPhysicalPage = 0x8b;
    
    /// <summary>
    /// Page map save area full (status 8Ch).
    /// No more room in the page map save area.
    /// </summary>
    public const byte EmmPageMapSaveAreaFull = 0x8c;
    
    /// <summary>
    /// Save area already has map (status 8Dh).
    /// A page map is already saved for this handle. Must restore before saving again.
    /// </summary>
    public const byte EmmPageMapSaved = 0x8d;
    
    /// <summary>
    /// No saved page map (status 8Eh).
    /// There is no page mapping register state in the save area for the specified handle.
    /// The Save Page Map function was not called before attempting restore.
    /// </summary>
    public const byte EmmPageNotSavedFirst = 0x8e;
    
    /// <summary>
    /// Invalid subfunction (status 8Fh).
    /// The subfunction (in AL register) was not recognized or is not implemented.
    /// </summary>
    public const byte EmmInvalidSubFunction = 0x8f;
}