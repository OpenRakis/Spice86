namespace Spice86.Core.Emulator.OperatingSystem.Programs;

using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory.ReaderWriter;

internal class CommandInterpreter : DosInternalProgram {
    public CommandInterpreter(MemoryAsmWriter memoryAsmWriter, IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(memoryAsmWriter, byteReaderWriter, baseAddress) {
        
    }

    public override string Name {
        get => "COMMAND.COM";
        set => throw new InvalidOperationException("Cannot rename a built-in operating system file");
    }
}
