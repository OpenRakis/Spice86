namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// Expanded memory is divided into segments called logical pages. <br/>
/// These pages are typically 16K-bytes of memory. <br/>
/// Logical pages are accessed through a physical block of memory called a page frame. <br/>
/// The page frame contains multiple physical pages, pages that the <br/>
/// CPU can address directly.  Physical pages are also 16K bytes of memory. <br/>
/// This static class only defines a few constants.
/// </summary>
public static class EmmMemory {
    /// <summary>
    /// 8 MB of Expanded Memory (LIM 3.2 specs)
    /// </summary>
    public const uint EmmMemorySize = 8 * 1024 * 1024;

    /// <summary>
    /// The total number of logical pages possible in 8 MB of RAM of expanded memory.
    /// </summary>
    public const ushort TotalPages = 512;
}