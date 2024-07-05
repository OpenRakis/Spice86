namespace Spice86.Core.Emulator.LoadableFile.Dos.Com;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Loads a .COM executable file into memory and generates a Program Segment Prefix (PSP) for the file.
/// </summary>
public class ComLoader : DosFileLoader {
    private const ushort ComOffset = 0x100;
    private readonly ushort _startSegment;
    private readonly PspGenerator _pspGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComLoader"/> class.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="state">The CPU state registers.</param>
    /// <param name="pspGenerator">The DOS program segment prefix generator.</param>
    /// <param name="startSegment">The starting segment of the program.</param>
    public ComLoader(IMemory memory, State state, ILoggerService loggerService, PspGenerator pspGenerator, ushort startSegment) : base(memory, state, loggerService) {
        _startSegment = startSegment;
        _state = state;
        _pspGenerator = pspGenerator;
    }

    /// <summary>
    /// Loads the specified .COM executable file into memory and generates a PSP for the file.
    /// </summary>
    /// <param name="file">The file path of the .COM executable file to load.</param>
    /// <param name="arguments">The arguments to pass to the program.</param>
    /// <returns>The bytes of the loaded .COM executable file.</returns>
    public override byte[] LoadFile(string file, string? arguments) {
        _pspGenerator.GeneratePsp(_startSegment, arguments);
        byte[] com = ReadFile(file);
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(_startSegment, ComOffset);
        _memory.LoadData(physicalStartAddress, com);

        // Make DS and ES point to the PSP
        _state.DS = _startSegment;
        _state.ES = _startSegment;
        SetEntryPoint(_startSegment, ComOffset);
        _state.InterruptFlag = true;
        return com;
    }
}