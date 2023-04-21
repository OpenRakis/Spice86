namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// Represents a handle for allocated EMM memory to a DOS program.
/// </summary>
public sealed class EmmHandle {
    /// <summary>
    /// The EMM page or raw page number. 
    /// </summary>
    public ushort PageNumber { get; set; } = ExpandedMemoryManager.EmmNullPage;

    /// <summary>
    /// The EMM handle number.
    /// </summary>
    public ushort HandleNumber { get; set; } = ExpandedMemoryManager.EmmNullHandle;
    
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
    public EmmMapping[] PageMap { get; } = new EmmMapping[ExpandedMemoryManager.EmmMaxPhysicalPages];

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
