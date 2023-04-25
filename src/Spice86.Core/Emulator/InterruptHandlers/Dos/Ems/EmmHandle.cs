namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// Represents a handle for allocated EMM memory to a DOS program.
/// </summary>
public class EmmHandle {
    /// <summary>
    /// The EMM handle number.
    /// </summary>
    public ushort HandleNumber { get; set; } = ExpandedMemoryManager.EmmNullHandle;
    
    private const string NullHandleName = "";
    
    /// <summary>
    /// Gets or sets the handle name.
    /// </summary>
    public string Name { get; set; } = NullHandleName;

    /// <summary>
    /// The logical pages assigned to the handle.
    /// </summary>
    public IDictionary<ushort, EmmPage> PageMap { get; } = new Dictionary<ushort, EmmPage>();
    
    /// <summary>
    /// Whether the EMM handler saved the page map into its internal data structures, or not.
    /// </summary>
    public bool SavedPageMap { get; set; }

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
