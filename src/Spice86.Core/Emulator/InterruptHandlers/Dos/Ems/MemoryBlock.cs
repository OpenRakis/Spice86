namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;

using Spice86.Core.Emulator.OperatingSystem;

/// <summary>
/// TODO: Merge this with <see cref="DosMemoryControlBlock"/>
/// </summary>
public record MemoryBlock {
    public int MemorySize { get; private set; }
    
    public MemoryBlock(ushort memorySizeInMb) {
        MemorySize = memorySizeInMb;
        Pages = (MemorySize * 1024 * 1024) / 4096;
        MemoryHandles = new int[Pages];
    }
    public int Pages { get; init; }

    public int[] MemoryHandles { get; init; }
}