namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems; 

/// <summary>
/// A representation of an EMM Physical or Logical Page
/// </summary>
public class EmmPage {
    /// <summary>
    /// The page's memory content
    /// </summary>
    public EmmPageMemory PageMemory { get; set; } = new();

    /// <summary>
    /// The page number. Initially set as EmmNullPage.
    /// </summary>
    public ushort PageNumber { get; set; } = ExpandedMemoryManager.EmmNullPage;
}