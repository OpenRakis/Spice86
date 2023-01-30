namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.VM;

/// <summary>
/// Represents the main memory of the IBM PC.
/// Size must be at least 1 MB.
/// </summary>
public class MainMemory : Memory {
    /// <summary>
    /// Size of conventional memory in bytes.
    /// </summary>
    public const uint ConvMemorySize = 1024 * 1024;

    public MainMemory(Machine machine, uint sizeInKb) : base(machine, sizeInKb)
    {
        if (sizeInKb * 1024 < ConvMemorySize) {
            throw new ArgumentException("Memory size must be at least 1 MB.");
        }
    }
}