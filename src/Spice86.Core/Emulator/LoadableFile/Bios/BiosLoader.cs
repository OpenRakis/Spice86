namespace Spice86.Core.Emulator.LoadableFile.Bios;

using Spice86.Shared.Interfaces;

using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;

/// <summary>
/// Loader for bios files.<br/>
/// Bios entry point is at physical address 0xFFFF0 (F000:FFF0).
/// </summary>
public class BiosLoader : ExecutableFileLoader {
    private const ushort CodeOffset = 0xFFF0;
    private const ushort CodeSegment = 0xF000;
    public override bool DosInitializationNeeded => false;

    public BiosLoader(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
    }

    public override byte[] LoadFile(string file, string? arguments) {
        byte[] bios = ReadFile(file);
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(CodeSegment, 0);
        _memory.LoadData(physicalStartAddress, bios);
        SetEntryPoint(CodeSegment, CodeOffset);
        return bios;
    }
}