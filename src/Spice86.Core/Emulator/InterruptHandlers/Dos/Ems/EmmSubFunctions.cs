namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// Constants for some Expanded Memory Manager (EMM) subFunction IDs as per LIM 3.2 specifications.
/// </summary>
public static class EmmSubFunctions {
    
    /// <summary>
    /// SubFunction ID for using physical page numbers. In LIM 3.2, this is used to map physical pages into a logical page frame.
    /// </summary>
    public const byte UsePhysicalPageNumbers = 0x00;

    /// <summary>
    /// SubFunction ID for using segmented addresses. In LIM 3.2, this is used to map a logical page into a segmented address.
    /// </summary>
    public const byte UseSegmentedAddress = 0x01;

    /// <summary>
    /// SubFunction ID for getting the handle name. In LIM 3.2, this is used to retrieve the name associated with a handle.
    /// </summary>
    public const byte HandleNameGet = 0x00;

    /// <summary>
    /// SubFunction ID for setting the handle name. In LIM 3.2, this is used to associate a name with a handle.
    /// </summary>
    public const byte HandleNameSet = 0x01;

    /// <summary>
    /// SubFunction ID for getting unallocated raw pages. In LIM 3.2, this is used to retrieve the number of unallocated raw pages.
    /// </summary>
    public const byte GetUnallocatedRawPages = 0x01;

    /// <summary>
    /// SubFunction ID for getting the hardware configuration array. In LIM 3.2, this is used to retrieve the hardware configuration array.
    /// </summary>
    public const byte GetHardwareConfigurationArray = 0x00;
}