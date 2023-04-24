namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

/// <summary>
/// The link between the EMM handle and a logical page.
/// </summary>
public class EmmMapping {
    /// <summary>
    /// The logical page number
    /// </summary>
    public ushort LogicalPageNumber { get; init; } = ExpandedMemoryManager.EmmNullPage;
}