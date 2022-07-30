namespace Spice86.Core.Emulator.LoadableFile.Dos.Com;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

public class ComLoader : ExecutableFileLoader {
    private const ushort ComOffset = 0x100;
    private readonly ushort _startSegment;

    public ComLoader(Machine machine, ushort startSegment) : base(machine) {
        _startSegment = startSegment;
    }

    public override byte[] LoadFile(string file, string? arguments) {
        new PspGenerator(_machine).GeneratePsp(_startSegment, arguments);
        byte[] com = ReadFile(file);
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(_startSegment, ComOffset);
        _memory.LoadData(physicalStartAddress, com);
        State? state = _cpu.State;

        // Make DS and ES point to the PSP
        state.DS = _startSegment;
        state.ES = _startSegment;
        SetEntryPoint(_startSegment, ComOffset);
        return com;
    }
}