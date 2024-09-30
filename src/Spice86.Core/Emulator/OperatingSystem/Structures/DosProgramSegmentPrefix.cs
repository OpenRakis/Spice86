namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Utils;

public sealed class DosProgramSegmentPrefix : MemoryBasedDataStructure {
    public DosProgramSegmentPrefix(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    public void MakeNew(ushort memSize) {

    }

    public void CloseFiles() {

    }

    /// <summary>
    /// Gets the <see cref="BaseAddress"/> of the PSP as a segment.
    /// </summary>
    public ushort Segment => MemoryUtils.ToSegment(BaseAddress);

    public void SaveVectors() {

    }

    public void RestoreVectors() {

    }

    public static ushort RootPspSegment { get; }
}
