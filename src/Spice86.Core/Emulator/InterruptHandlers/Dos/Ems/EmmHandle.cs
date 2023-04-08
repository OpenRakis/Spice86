namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// Represents a handle for allocated EMS memory.
/// </summary>
public sealed class EmmHandle {
    public ushort Pages { get; set; } = ExpandedMemoryManager.EmmNullHandle;
    
    public int MemHandle { get; set; }
    
    /// <summary>
    /// 4 16 KB pages in PageFrame
    /// </summary>
    public const byte EmmMaxPhysicalPages = 4;

    private const string NullHandleName = "";

    public EmmHandle() {
        for (int i = 0; i < PageMap.Length; i++) {
            PageMap[i] = new EmmMapping();
        }
    }

    /// <summary>
    /// Gets or sets the handle name.
    /// </summary>
    public string Name { get; set; } = NullHandleName;

    /// <summary>
    /// Gets or sets the saved page map for the handle.
    /// </summary>
    public EmmMapping[] PageMap { get; } = new EmmMapping[EmmMaxPhysicalPages];

    public bool SavePageMap { get; set; }

    /// <summary>
    /// Returns a string containing the handle name.
    /// </summary>
    /// <returns>String containing the handle name.</returns>
    public override string ToString() {
        if (!string.IsNullOrWhiteSpace(Name) && Name != NullHandleName) {
            return Name;
        } else {
            return "Untitled";
        }
    }
}
