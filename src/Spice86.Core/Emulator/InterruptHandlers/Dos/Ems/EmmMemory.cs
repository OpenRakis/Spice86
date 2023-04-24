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
    public IDictionary<ushort, EmmPage> LogicalPages { get; init; } = new Dictionary<ushort, EmmPage>();

    public EmmMemory() {
        for (ushort i = 0; i < EmmMemorySize / ExpandedMemoryManager.EmmPageSize; i++) {
            EmmPage page = new(ExpandedMemoryManager.EmmPageSize);
            LogicalPages.Add(i, page);
        }
    }

    /// <summary>
    /// The total number of pages in expanded memory.
    /// </summary>
    public ushort TotalPages => (ushort)LogicalPages.Count;

    public ushort GetFreePages() {
        return (ushort)LogicalPages.Count(static x => x.Value.PageNumber is ExpandedMemoryManager.EmmNullPage);
    }

    public ushort GetNextFreeLogicalPageId() {
        return LogicalPages.First(x => x.Value.PageNumber is ExpandedMemoryManager.EmmNullPage).Key;
    }

    public ushort AllocateLogicalPage() {
        ushort index = GetNextFreeLogicalPageId();
        LogicalPages[index].PageNumber = index;
        return index;
    }

    public void FreeLogicalPages(EmmHandle handle) {
        foreach (EmmMapping mapping in handle.PageMap) {
            LogicalPages[mapping.LogicalPageNumber].PageNumber = ExpandedMemoryManager.EmmNullPage;
        }
    }
}