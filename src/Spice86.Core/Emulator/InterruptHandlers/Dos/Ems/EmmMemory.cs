namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using System.Linq;

/// <summary>
/// Expanded memory is divided into segments called logical pages. <br/>
/// These pages are typically 16K-bytes of memory. <br/>
/// Logical pages are accessed through a physical block of memory called a page frame. <br/>
/// The page frame contains multiple physical pages, pages that the <br/>
/// microprocessor can address directly.  Physical pages are also typically 16K bytes of memory.
/// </summary>
public class EmmMemory {
    /// <summary>
    /// 8 MB of Expanded Memory (LIM 3.2 specs)
    /// </summary>
    public const uint EmmMemorySize = 8 * 1024 * 1024;

    /// <summary>
    /// All the logical pages stored in EMM memory.
    /// </summary>
    public EmmPage[] LogicalPages { get; init; } = new EmmPage[EmmMemorySize / ExpandedMemoryManager.EmmPageSize];

    public EmmMemory() {
        for (ushort i = 0; i < LogicalPages.Length; i++) {
            LogicalPages[i] = new() {
                PageNumber = i
            };
        }
    }

    /// <summary>
    /// The total number of pages in expanded memory.
    /// </summary>
    public ushort TotalPages => (ushort)LogicalPages.Length;

    public ushort GetFreePages() {
        return (ushort)LogicalPages.Count(static x => x.PageNumber is ExpandedMemoryManager.EmmNullPage);
    }
}