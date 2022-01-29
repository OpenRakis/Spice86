namespace Spice86.Emulator.Loadablefile.Dos.Exe;

using Serilog;

using Spice86.Emulator.LoadableFile;
using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;
using Spice86.Emulator.CPU;

/// <summary>
/// Loads a DOS 16 bits EXE file in memory.
/// </summary>
public class ExeLoader : ExecutableFileLoader {
    private static readonly ILogger _logger = Log.Logger.ForContext<ExeLoader>();
    private readonly ushort startSegment;

    public ExeLoader(Machine machine, ushort startSegment) : base(machine) {
        this.startSegment = startSegment;
    }

    public override byte[] LoadFile(string file, string? arguments) {
        byte[] exe = this.ReadFile(file);
        _logger.Debug("Exe size: {@ExeSize}", exe.Length);
        ExeFile exeFile = new ExeFile(exe);
        _logger.Debug("Read header: {@ReadHeader}", exeFile);
        LoadExeFileInMemory(exeFile, startSegment);
        ushort pspSegment = (ushort)(startSegment - 0x10);
        SetupCpuForExe(exeFile, startSegment, pspSegment);
        new PspGenerator(_machine).GeneratePsp(pspSegment, arguments);
        _logger.Debug("Initial CPU State: {@CpuState}", _cpu.GetState());
        return exe;
    }

    private void LoadExeFileInMemory(ExeFile exeFile, ushort startSegment) {
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(startSegment, 0);
        _memory.LoadData(physicalStartAddress, exeFile.GetProgramImage());
        foreach (SegmentedAddress address in exeFile.GetRelocationTable()) {
            // Read value from memory, add the start segment offset and write back
            uint addressToEdit = MemoryUtils.ToPhysicalAddress(address.GetSegment(), address.GetOffset()) + physicalStartAddress;
            int segmentToRelocate = _memory.GetUint16(addressToEdit);
            segmentToRelocate += startSegment;
            _memory.SetUint16(addressToEdit, (ushort)segmentToRelocate);
        }
    }

    private void SetupCpuForExe(ExeFile exeFile, ushort startSegment, ushort pspSegment) {
        State state = _cpu.GetState();

        // MS-DOS uses the values in the file header to set the SP and SS registers and
        // adjusts the initial value of the SS register by adding the start-segment
        // address to it.
        state.SetSS((ushort)(exeFile.GetInitSS() + startSegment));
        state.SetSP(exeFile.GetInitSP());

        // Make DS and ES point to the PSP
        state.SetDS(pspSegment);
        state.SetES(pspSegment);

        // Finally, MS-DOS reads the initial CS and IP values from the program's file
        // header, adjusts the CS register value by adding the start-segment address to
        // it, and transfers control to the program at the adjusted address.
        SetEntryPoint((ushort)(exeFile.GetInitCS() + startSegment), exeFile.GetInitIP());
    }
}