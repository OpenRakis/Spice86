namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// All the possible status returned in _state.AH by the Expanded Memory Manager.
/// </summary>
public static class EmmStatus {
    /// <summary>
    /// The function completed successfully.
    /// </summary>
    public const byte EmmNoError = 0x00;
    /// <summary>
    /// The EMM handle number was not recognized.
    /// </summary>
    public const byte EmmInvalidHandle = 0x83;
    /// <summary>
    /// The EMM function was not recognized or is not implemented.
    /// </summary>
    public const byte EmmFunctionNotSupported = 0x84;
    /// <summary>
    /// The EMM is out of handles.
    /// </summary>
    public const byte EmmOutOfHandles = 0x85;
    /// <summary>
    /// The EMM could not save the page map.
    /// </summary>
    public const byte EmmSaveMapError = 0x86;
    /// <summary>
    /// The EMM does not have enough pages to satisfy the request.
    /// </summary>
    public const byte EmmNotEnoughPages = 0x87;
    /// <summary>
    /// The emulated program tried to allocate zero pages.
    /// </summary>
    public const byte EmmTriedToAllocateZeroPages = 0x89;
    /// <summary>
    /// The EMM logical page number was out of range.
    /// </summary>
    public const byte EmmLogicalPageOutOfRange = 0x8a;
    /// <summary>
    /// The EMM physical page number was out of range.
    /// </summary>
    public const byte EmmIllegalPhysicalPage = 0x8b;
    /// <summary>
    /// The EMM map was succesfully saved.
    /// </summary>
    public const byte EmmPageMapSaved = 0x8d;
    /// <summary>
    /// The subfunction was not recognized or is not implemented.
    /// </summary>
    public const byte EmmInvalidSubFunction = 0x8f;
}