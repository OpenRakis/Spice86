namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// Constants for some Expanded Memory Manager (EMM) subfunction IDs.
/// <para>
/// Note: Functions 0x50 (Map/Unmap Multiple Handle Pages), 0x51 (Reallocate Pages),
/// 0x53 (Get/Set Handle Name), 0x58 (Get Mappable Physical Address Array),
/// and 0x59 (Get Expanded Memory Hardware Information) are part of LIM EMS 4.0,
/// not LIM EMS 3.2. The base implementation (functions 0x40-0x4E) follows LIM EMS 3.2.
/// </para>
/// </summary>
public static class EmmSubFunctionsCodes {

    /// <summary>
    /// Subfunction ID for using physical page numbers (EMS 4.0 function 0x50).
    /// Used to map physical pages into the page frame using physical page numbers.
    /// </summary>
    public const byte UsePhysicalPageNumbers = 0x00;

    /// <summary>
    /// Subfunction ID for using segmented addresses (EMS 4.0 function 0x50).
    /// Used to map a logical page into the page frame using segment addresses.
    /// </summary>
    public const byte UseSegmentedAddress = 0x01;

    /// <summary>
    /// Subfunction ID for getting the handle name (EMS 4.0 function 0x53).
    /// Used to retrieve the 8-character name associated with an EMM handle.
    /// </summary>
    public const byte HandleNameGet = 0x00;

    /// <summary>
    /// Subfunction ID for setting the handle name (EMS 4.0 function 0x53).
    /// Used to associate an 8-character name with an EMM handle.
    /// </summary>
    public const byte HandleNameSet = 0x01;

    /// <summary>
    /// Subfunction ID for getting unallocated raw pages (EMS 4.0 function 0x59).
    /// Used to retrieve the number of unallocated raw pages.
    /// In this LIM standard implementation, raw pages are the same as standard pages (16 KB).
    /// </summary>
    public const byte GetUnallocatedRawPages = 0x01;

    /// <summary>
    /// Subfunction ID for getting the hardware configuration array (EMS 4.0 function 0x59).
    /// Used to retrieve hardware configuration information including raw page size,
    /// alternate register sets, DMA channels, and LIM type.
    /// </summary>
    public const byte GetHardwareConfigurationArray = 0x00;
}