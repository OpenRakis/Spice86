namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// A representation of an EMM Physical or Logical Page
/// </summary>
public interface IEmmPage {
    
    /// <summary>
    /// The page's memory content
    /// </summary>
    IMemoryDevice PageMemory { get; set; }

    /// <summary>
    /// The page number. Initially set as EmmNullPage.
    /// </summary>
    ushort PageNumber { get; set; }
}