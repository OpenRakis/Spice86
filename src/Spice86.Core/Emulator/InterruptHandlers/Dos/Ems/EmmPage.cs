namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// A representation of a logical EMM page.
/// </summary>
public class EmmPage {
    public EmmPage(uint size) {
        PageMemory = new Ram(size);
    }
    
    /// <summary>
    /// THe page's memory content.
    /// </summary>
    public Ram PageMemory { get; set; }

    /// <summary>
    /// The logical page number, for book keeping inside our dictionaries.
    /// </summary>
    public ushort PageNumber { get; set; } = ExpandedMemoryManager.EmmNullPage;
}