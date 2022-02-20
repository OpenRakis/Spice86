namespace Spice86.Emulator.Loadablefile.Bios;

using Spice86.Emulator.LoadableFile;
using Spice86.Emulator.Memory;
using Spice86.Emulator.VM;
/// <summary>
/// Loader for bios files.<br/>
/// Bios entry point is at physical address 0xFFFF0 (F000:FFF0).
/// </summary>
public class BiosLoader : ExecutableFileLoader {
    private const ushort CodeOffset = 0xFFF0;
    private const ushort CodeSegment = 0xF000;
    public BiosLoader(Machine machine) : base(machine) {
    }

    public override byte[] LoadFile(string file, string? arguments) {
        byte[] bios = this.ReadFile(file);
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(CodeSegment, 0);
        _memory.LoadData(physicalStartAddress, bios);
        this.SetEntryPoint(CodeSegment, CodeOffset);
        return bios;
    }
}
