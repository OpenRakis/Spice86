namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// Expanded memory is divided into segments called logical pages. <br/>
/// These pages are typically 16K-bytes of memory. <br/>
/// Logical pages are accessed through a physical block of memory called a page frame. <br/>
/// The page frame contains multiple physical pages, pages that the <br/>
/// CPU can address directly. Physical pages are also 16K bytes of memory. <br/>
/// <para>
/// Per the LIM EMS specification, the page frame is a contiguous 64 KB window
/// in the Upper Memory Area (typically at segment 0xE000), providing access to
/// 4 physical pages at a time. Applications can map different logical pages
/// to these physical pages to access any of the 512 available expanded memory pages.
/// </para>
/// </summary>
public static class EmmMemory {
    /// <summary>
    /// Total expanded memory size: 8 MB.
    /// This value is consistent with common EMS implementations and provides
    /// 512 logical pages of 16 KB each. The LIM EMS 4.0 spec supports up to 32 MB,
    /// but 8 MB was a common practical limit.
    /// </summary>
    public const uint EmmMemorySize = 8 * 1024 * 1024;

    /// <summary>
    /// The total number of logical pages possible in 8 MB of expanded memory.
    /// Calculated as EmmMemorySize / 16384 = 512 pages.
    /// </summary>
    public const ushort TotalPages = 512;
}