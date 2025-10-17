namespace Spice86.Core.Emulator.OperatingSystem.Programs;

using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Structures;

internal abstract class DosInternalProgram : DosProgramSegmentPrefix, IVirtualFile {
    protected DosInternalProgram(MemoryAsmWriter memoryAsmWriter,
        IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
    }

    public abstract string Name { get; set; }
}
