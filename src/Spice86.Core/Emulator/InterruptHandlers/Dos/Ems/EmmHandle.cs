namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// Represents a handle for allocated EMS memory.
/// </summary>
public sealed class EmmHandle {
    public ushort Pages { get; set; }
    
    public int MemHandle { get; set; }
    
    /* 4 16kb pages in pageframe */
    public const byte EmmMaxPhysicalPages = 4;
    
    private readonly EmmMapping[] _pageMap = new EmmMapping[EmmMaxPhysicalPages];

    private static readonly string _nullHandleName = new((char)0, 8);

    public EmmHandle() {
        for (int i = 0; i < _pageMap.Length; i++) {
            _pageMap[i] = new EmmMapping();
        }
    }

    /// <summary>
    /// Gets or sets the handle name.
    /// </summary>
    public string Name { get; set; } = _nullHandleName;

    /// <summary>
    /// Gets or sets the saved page map for the handle.
    /// </summary>
    public EmmMapping[] PageMap => _pageMap;
    
    public bool SavePagedMap { get; set; }

    /// <summary>
    /// Returns a string containing the handle name.
    /// </summary>
    /// <returns>String containing the handle name.</returns>
    public override string ToString() {
        if (!string.IsNullOrEmpty(Name) && Name != _nullHandleName) {
            return Name;
        } else {
            return "Untitled";
        }
    }
}
