namespace Spice86.Core.Emulator.Memory;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Represents a handle for allocated EMS memory.
/// </summary>
internal sealed class EmsHandle {
    private readonly int[] savedPageMap = new int[] { -1, -1, -1, -1 };

    private static readonly string nullHandleName = new((char)0, 8);

    public EmsHandle() {
        LogicalPages = new List<ushort>();
    }

    public EmsHandle(IEnumerable<ushort> pages) {
        this.LogicalPages = pages.ToList();
    }

    /// <summary>
    /// Gets the number of pages currently allocated to the handle.
    /// </summary>
    public int PagesAllocated => LogicalPages.Count;

    /// <summary>
    /// Gets the logical pages allocated to the handle.
    /// </summary>
    public List<ushort> LogicalPages { get; }

    /// <summary>
    /// Gets or sets the handle name.
    /// </summary>
    public string Name { get; set; } = nullHandleName;

    /// <summary>
    /// Gets or sets the saved page map for the handle.
    /// </summary>
    public Span<int> SavedPageMap => savedPageMap;

    /// <summary>
    /// Returns a string containing the handle name.
    /// </summary>
    /// <returns>String containing the handle name.</returns>
    public override string ToString() {
        if (!string.IsNullOrEmpty(Name) && Name != nullHandleName) {
            return Name;
        } else {
            return "Untitled";
        }
    }
}
