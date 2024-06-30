namespace Spice86.Core.Emulator.LoadableFile.Dos.Exe;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Loads a DOS 16-bit executable (EXE) file into memory and sets up the CPU to execute it.
/// </summary>
public class ExeLoader : DosFileLoader {
    private readonly ILoggerService _loggerService;
    private readonly ushort _startSegment;
    private readonly EnvironmentVariables _environmentVariables;
    private readonly DosFileManager _dosFileManager;
    private readonly DosMemoryManager _dosMemoryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExeLoader"/> class with the specified machine, logger service, and starting segment.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="loggerService">The logger service to use for logging.</param>
    /// <param name="state">The CPU's state registers.</param>
    /// <param name="environmentVariables">The master environment block, from the DOS kernel.</param>
    /// <param name="dosFileManager">The DOS file manager.</param>
    /// <param name="dosMemoryManager">The DOS memory manager.</param>
    /// <param name="startSegment">The starting segment for the executable.</param>
    public ExeLoader(IMemory memory, State state, ILoggerService loggerService, EnvironmentVariables environmentVariables, DosFileManager dosFileManager, DosMemoryManager dosMemoryManager, ushort startSegment) : base(memory, state, loggerService) {
        _loggerService = loggerService;
        _startSegment = startSegment;
        _state = state;
        _environmentVariables = environmentVariables;
        _dosFileManager = dosFileManager;
        _dosMemoryManager = dosMemoryManager;
    }

    /// <summary>
    /// Loads the specified EXE file into memory and sets up the CPU to execute it.
    /// </summary>
    /// <param name="file">The path to the EXE file to load.</param>
    /// <param name="arguments">Optional command-line arguments to pass to the program.</param>
    /// <returns>The raw bytes of the loaded EXE file.</returns>
    public override byte[] LoadFile(string file, string? arguments) {
        byte[] exe = ReadFile(file);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Exe size: {ExeSize}", exe.Length);
        }
        ExeFile exeFile = new ExeFile(new ByteArrayReaderWriter(exe));
        if (!exeFile.IsValid) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Invalid EXE file {File}", file);
            }
            throw new UnrecoverableException($"Invalid EXE file {file}");
        }
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Read header: {ReadHeader}", exeFile);
        }

        LoadExeFileInMemory(exeFile, _startSegment);
        ushort pspSegment = (ushort)(_startSegment - 0x10);
        SetupCpuForExe(exeFile, _startSegment, pspSegment);
        new PspGenerator(_memory, _environmentVariables, _dosMemoryManager, _dosFileManager).GeneratePsp(pspSegment, arguments);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Initial CPU State: {CpuState}", _state);
        }
        return exe;
    }

    /// <summary>
    /// Loads the program image and applies any necessary relocations to it.
    /// </summary>
    /// <param name="exeFile">The EXE file to load.</param>
    /// <param name="startSegment">The starting segment for the program.</param>
    private void LoadExeFileInMemory(ExeFile exeFile, ushort startSegment) {
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(startSegment, 0);
        _memory.LoadData(physicalStartAddress, exeFile.ProgramImage);
        foreach (SegmentedAddress address in exeFile.RelocationTable) {
            // Read value from memory, add the start segment offset and write back
            uint addressToEdit = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset) + physicalStartAddress;
            _memory.UInt16[addressToEdit] += startSegment;
        }
    }

    /// <summary>
    /// Sets up the CPU to execute the loaded program.
    /// </summary>
    /// <param name="exeFile">The EXE file that was loaded.</param>
    /// <param name="startSegment">The starting segment address of the program.</param>
    /// <param name="pspSegment">The segment address of the program's PSP (Program Segment Prefix).</param>
    private void SetupCpuForExe(ExeFile exeFile, ushort startSegment, ushort pspSegment) {
        // MS-DOS uses the values in the file header to set the SP and SS registers and
        // adjusts the initial value of the SS register by adding the start-segment
        // address to it.
        _state.SS = (ushort)(exeFile.InitSS + startSegment);
        _state.SP = exeFile.InitSP;

        // Make DS and ES point to the PSP
        _state.DS = pspSegment;
        _state.ES = pspSegment;

        _state.InterruptFlag = true;

        // Finally, MS-DOS reads the initial CS and IP values from the program's file
        // header, adjusts the CS register value by adding the start-segment address to
        // it, and transfers control to the program at the adjusted address.
        SetEntryPoint((ushort)(exeFile.InitCS + startSegment), exeFile.InitIP);
    }
}