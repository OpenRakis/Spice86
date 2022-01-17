namespace Spice86.Emulator.Loadablefile.Bios;

using Spice86.Emulator.LoadableFile;
using Spice86.Emulator.Memory;
using Spice86.Emulator.VM;
/// <summary>
/// Loader for bios files.<br/>
/// Bios entry point is at physical address 0xFFFF0 (F000:FFF0).
/// </summary>
public class BiosLoader : ExecutableFileLoader {
    private static readonly int CODE_OFFSET = 0xFFF0;
    private static readonly int CODE_SEGMENT = 0xF000;
    public BiosLoader(Machine machine) : base(machine) {
    }

    public override byte[] LoadFile(string file, string arguments) {
        byte[] bios = this.ReadFile(file);
        int physicalStartAddress = MemoryUtils.ToPhysicalAddress(CODE_SEGMENT, 0);
        _memory.LoadData(physicalStartAddress, bios);
        this.SetEntryPoint(CODE_SEGMENT, CODE_OFFSET);
        return bios;
    }
}
