namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using System.Linq;

/// <summary>
/// Expanded memory is divided into segments called logical pages. <br/>
/// These pages are typically 16K-bytes of memory. <br/>
/// Logical pages are accessed through a physical block of memory called a page frame. <br/>
/// The page frame contains multiple physical pages, pages that the <br/>
/// CPU can address directly.  Physical pages are also 16K bytes of memory.
/// </summary>
public class EmmMemory {
    /// <summary>
    /// 8 MB of Expanded Memory (LIM 3.2 specs)
    /// </summary>
    public const uint EmmMemorySize = 8 * 1024 * 1024;

    /// <summary>
    /// All the logical pages stored in EMM memory.
    /// </summary>
    public IDictionary<EmmHandle, EmmPage> LogicalPages { get; init; } = new Dictionary<EmmHandle, EmmPage>();

    /// <summary>
    /// The total number of pages possible in 8 MB of RAM of expanded memory.
    /// </summary>
    public const ushort TotalPages = 512;

    public ushort GetFreePages() {
        return (ushort)LogicalPages.Count(static x => x.Value.PageNumber is ExpandedMemoryManager.EmmNullPage);
    }

    public ushort GetNextFreeLogicalPageId(EmmHandle handle) {
        return (ushort) (handle.PageMap.Count + 1);
    }

    public ushort AllocateLogicalPage(EmmHandle handle) {
        var emmPage = new EmmPage(ExpandedMemoryManager.EmmPageSize);
        LogicalPages.TryAdd(handle, emmPage);
        ushort index = GetNextFreeLogicalPageId(handle);
        LogicalPages[handle].PageNumber = index;
        return index;
    }

    public void FreeLogicalPages(EmmHandle handle) {
        LogicalPages.Remove(handle);
    }
}