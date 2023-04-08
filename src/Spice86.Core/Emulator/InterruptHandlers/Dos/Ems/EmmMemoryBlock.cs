namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

public record MemoryBlock {
    /// <summary>
    /// Memory size in MB
    /// </summary>
    public int MemorySize { get; private set; }
    
    public MemoryBlock(ushort memorySizeInMb) {
        MemorySize = memorySizeInMb;
        Pages = MemorySize * 1024 * 1024 / 4096;
        MemoryHandles = new int[Pages];
    }
    /// <summary>
    /// Number of pages. Initially set at MemorySize * 1024 * 1024 / 4096
    /// </summary>
    public int Pages { get; init; }

    /// <summary>
    /// IDs of EMM Memory Handles
    /// </summary>
    public int[] MemoryHandles { get; init; }
}