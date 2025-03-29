namespace Spice86.Core.Emulator.LoadableFile.Dos.Exe;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// Representation of an COM file as it is stored on disk, loaded into memory.
/// </summary>
public class ComFile : MemoryBasedDataStructure {
    /// <summary>
    /// The offset part of the segment:offset address of the COM file in memory.
    /// </summary>
    public const ushort ComOffset = 0x100;

    public ComFile(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }
}
