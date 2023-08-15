namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Represents a DTA (Disk Transfer Area) in memory.
/// </summary>
public class DosDiskTransferArea : MemoryBasedDataStructure {
    /// <summary>
    /// Initializes a new instance of the <see cref="DosDiskTransferArea"/> class.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus used for accessing the DTA.</param>
    /// <param name="baseAddress">The base address of the DTA within memory.</param>
    public DosDiskTransferArea(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
        CurrentStructure = new MemoryBasedDataStructure(byteReaderWriter, baseAddress);
    }
    
    /// <summary>
    /// The structure the DTA points to.
    /// <remarks>For the DOS INT21H FindFirst/FindNext, this is a <see cref="FileMatch"/> structure</remarks>
    /// </summary>
    public MemoryBasedDataStructure CurrentStructure { get; set; }
}