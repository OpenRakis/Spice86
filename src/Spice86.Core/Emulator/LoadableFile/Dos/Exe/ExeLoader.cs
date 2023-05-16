namespace Spice86.Core.Emulator.LoadableFile.Dos.Exe;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Loads a DOS 16-bit executable (EXE) file into memory and sets up the CPU to execute it.
/// </summary>
public class ExeLoader : DosFileLoader {
    private readonly ILoggerService _loggerService;
    private readonly ushort _startSegment;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExeLoader"/> class with the specified machine, logger service, and starting segment.
    /// </summary>
    /// <param name="machine">The machine instance to load the executable into.</param>
    /// <param name="loggerService">The logger service to use for logging.</param>
    /// <param name="startSegment">The starting segment for the executable.</param>
    public ExeLoader(Machine machine, ILoggerService loggerService, ushort startSegment) : base(machine, loggerService) {
        _loggerService = loggerService;
        _startSegment = startSegment;
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
        ExeFile exeFile = new ExeFile(exe);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Read header: {ReadHeader}", exeFile);
        }

        LoadExeFileInMemory(exeFile, _startSegment);
        ushort pspSegment = (ushort)(_startSegment - 0x10);
        SetupCpuForExe(exeFile, _startSegment, pspSegment);
        new PspGenerator(_machine).GeneratePsp(pspSegment, arguments);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Initial CPU State: {CpuState}", _cpu.State);
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
        for (int i = 0; i < exeFile.RelocationTable.Count; i++) {
            SegmentedAddress address = exeFile.RelocationTable[i];
            // Read value from memory, add the start segment offset and write back
            uint addressToEdit = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset) + physicalStartAddress;
            int segmentToRelocate = _memory.GetUint16(addressToEdit);
            segmentToRelocate += startSegment;
            _memory.SetUint16(addressToEdit, (ushort)segmentToRelocate);
        }
    }

    /// <summary>
    /// Sets up the CPU to execute the loaded program.
    /// </summary>
    /// <param name="exeFile">The EXE file that was loaded.</param>
    /// <param name="startSegment">The starting segment address of the program.</param>
    /// <param name="pspSegment">The segment address of the program's PSP (Program Segment Prefix).</param>
    private void SetupCpuForExe(ExeFile exeFile, ushort startSegment, ushort pspSegment) {
        State state = _cpu.State;

        // MS-DOS uses the values in the file header to set the SP and SS registers and
        // adjusts the initial value of the SS register by adding the start-segment
        // address to it.
        state.SS = (ushort)(exeFile.InitSS + startSegment);
        state.SP = exeFile.InitSP;

        // Make DS and ES point to the PSP
        state.DS = pspSegment;
        state.ES = pspSegment;

        state.InterruptFlag = true;

        // Finally, MS-DOS reads the initial CS and IP values from the program's file
        // header, adjusts the CS register value by adding the start-segment address to
        // it, and transfers control to the program at the adjusted address.
        SetEntryPoint((ushort)(exeFile.InitCS + startSegment), exeFile.InitIP);
    }
}