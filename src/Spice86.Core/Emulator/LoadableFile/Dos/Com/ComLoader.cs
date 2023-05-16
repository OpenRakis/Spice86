namespace Spice86.Core.Emulator.LoadableFile.Dos.Com;

using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Loads a .COM executable file into memory and generates a Program Segment Prefix (PSP) for the file.
/// </summary>
public class ComLoader : DosFileLoader {
    private const ushort ComOffset = 0x100;
    private readonly ushort _startSegment;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComLoader"/> class with the specified parameters.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="startSegment">The starting segment of the program.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public ComLoader(Machine machine, ushort startSegment, ILoggerService loggerService) : base(machine, loggerService) {
        _startSegment = startSegment;
    }

    /// <summary>
    /// Loads the specified .COM executable file into memory and generates a PSP for the file.
    /// </summary>
    /// <param name="file">The file path of the .COM executable file to load.</param>
    /// <param name="arguments">The arguments to pass to the program.</param>
    /// <returns>The bytes of the loaded .COM executable file.</returns>
    public override byte[] LoadFile(string file, string? arguments) {
        new PspGenerator(_machine).GeneratePsp(_startSegment, arguments);
        byte[] com = ReadFile(file);
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(_startSegment, ComOffset);
        _memory.LoadData(physicalStartAddress, com);
        State state = _cpu.State;

        // Make DS and ES point to the PSP
        state.DS = _startSegment;
        state.ES = _startSegment;
        SetEntryPoint(_startSegment, ComOffset);
        state.InterruptFlag = true;
        return com;
    }
}