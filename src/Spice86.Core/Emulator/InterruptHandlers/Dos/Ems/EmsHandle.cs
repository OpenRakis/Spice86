namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// Represents a handle for allocated EMS memory.
/// </summary>
public sealed record EmsHandle {
    private readonly List<byte[]?> pages;
    private static readonly string nullHandleName = new((char)0, 8);
    private readonly byte[]?[] savedPageMap = new byte[ExpandedMemoryManager.MaximumPhysicalPages][];

    public EmsHandle()
    {
        this.pages = new List<byte[]?>();
    }
    public EmsHandle(int pagesRequested)
    {
        this.pages = new List<byte[]?>(pagesRequested);
        for (int i = 0; i < pagesRequested; i++)
            pages.Add(null);
    }

    /// <summary>
    /// Gets the number of pages currently allocated to the handle.
    /// </summary>
    public int PagesAllocated => pages.Count;
    /// <summary>
    /// Gets or sets the handle name.
    /// </summary>
    public string Name { get; set; } = nullHandleName;
    /// <summary>
    /// Gets or sets the saved page map for the handle.
    /// </summary>
    public byte[]?[]? SavedPageMap { get; set; }

    /// <summary>
    /// Attempts to get a logical page allocated to the handle.
    /// </summary>
    /// <param name="logicalPageIndex">Index of logical page to get.</param>
    /// <returns>Logical page with specified index if successful; otherwise null.</returns>
    public byte[]? GetLogicalPage(int logicalPageIndex)
    {
        if (logicalPageIndex >= 0 && logicalPageIndex < pages.Count)
        {
            if (pages[logicalPageIndex] == null)
                pages[logicalPageIndex] = new byte[ExpandedMemoryManager.PageSize];
            return pages[logicalPageIndex];
        }
        else
        {
            return null;
        }
    }
    /// <summary>
    /// Changes the number of logical pages allocated to the handle.
    /// </summary>
    /// <param name="pagesRequested">New total number of logical pages allocated to the handle.</param>
    public void Reallocate(int pagesRequested)
    {
        if (pagesRequested < 0)
            throw new ArgumentOutOfRangeException(nameof(pagesRequested));

        if (pagesRequested < pages.Count)
            pages.RemoveRange(pagesRequested, pages.Count - pagesRequested);
        else if (pagesRequested > pages.Count)
        {
            for (int i = 0; i < pagesRequested - pages.Count; i++)
                pages.Add(null);
        }
    }
    /// <summary>
    /// Returns a string containing the handle name.
    /// </summary>
    /// <returns>String containing the handle name.</returns>
    public override string ToString()
    {
        if (!string.IsNullOrEmpty(this.Name) && this.Name != nullHandleName)
            return this.Name;
        else
            return "Untitled";
    }
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(this.pages.Count);
        foreach (var page in this.pages)
        {
            writer.Write(page != null);
            if (page != null)
                writer.Write(page);
        }

        writer.Write(this.Name);

        foreach (var pageMap in this.savedPageMap)
        {
            writer.Write(pageMap != null);
            if (pageMap != null)
            {
                int index = this.pages.IndexOf(pageMap);
                if (index == -1)
                    throw new InvalidOperationException();
                writer.Write(index);
            }
        }
    }
    /// <summary>
    /// Returns the index of a page if it is contained in the handle.
    /// </summary>
    /// <param name="page">Page to search for.</param>
    /// <returns>Index of the specified page if found; otherwise -1.</returns>
    public int IndexOfPage(byte[] page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return this.pages.IndexOf(page);
    }
}
