namespace Spice86.Core.Emulator.LoadableFile.Dos.Com;

using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Loads a .COM executable file into memory and generates a Program Segment Prefix (PSP) for the file.
/// </summary>
public class ComLoader : DosFileLoader {
    private const ushort ComOffset = 0x100;
    private readonly ushort _startSegment;
    private readonly EnvironmentVariables _environmentVariables;
    private readonly DosFileManager _dosFileManager;
    private readonly DosMemoryManager _dosMemoryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComLoader"/> class with the specified parameters.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="state">The CPU state registers.</param>
    /// <param name="environmentVariables">The master environment block, from the DOS kernel.</param>
    /// <param name="dosFileManager">The DOS file manager.</param>
    /// <param name="dosMemoryManager">The DOS memory manager.</param>
    /// <param name="startSegment">The starting segment of the program.</param>
    public ComLoader(IMemory memory, State state, ILoggerService loggerService, EnvironmentVariables environmentVariables, DosFileManager dosFileManager, DosMemoryManager dosMemoryManager, ushort startSegment) : base(memory, state, loggerService) {
        _startSegment = startSegment;
        _environmentVariables = environmentVariables;
        _dosFileManager = dosFileManager;
        _dosMemoryManager = dosMemoryManager;
        _state = state;
    }

    /// <summary>
    /// Loads the specified .COM executable file into memory and generates a PSP for the file.
    /// </summary>
    /// <param name="file">The file path of the .COM executable file to load.</param>
    /// <param name="arguments">The arguments to pass to the program.</param>
    /// <returns>The bytes of the loaded .COM executable file.</returns>
    public override byte[] LoadFile(string file, string? arguments) {
        new PspGenerator(_memory, _environmentVariables, _dosMemoryManager, _dosFileManager).GeneratePsp(_startSegment, arguments);
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