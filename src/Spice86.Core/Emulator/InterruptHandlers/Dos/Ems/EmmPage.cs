namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// A representation of an EMM Physical or Logical Page
/// </summary>
public class EmmPage {
    public EmmPage() {
        PageMemory = new EmmPageMemory();
    }
    
    /// <summary>
    /// The page's memory content
    /// </summary>
    public IMemoryDevice PageMemory { get; set; }

    /// <summary>
    /// The page number. Initially set as EmmNullPage.
    /// </summary>
    public ushort PageNumber { get; set; } = ExpandedMemoryManager.EmmNullPage;
}