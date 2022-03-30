namespace Spice86.Emulator.LoadableFile.Dos.Exe;

using Serilog;

using Spice86.Emulator.LoadableFile;
using Spice86.Emulator.VM;
using Spice86.Emulator.Memory;
using Spice86.Emulator.CPU;
using Spice86.Emulator.Errors;

/// <summary>
/// Loads a DOS 16 bits EXE file in memory.
/// </summary>
public class ExeLoader : ExecutableFileLoader {
    private static readonly ILogger _logger = Program.Logger.ForContext<ExeLoader>();
    private readonly ushort _startSegment;

    public ExeLoader(Machine machine, ushort startSegment) : base(machine) {
        _startSegment = startSegment;
    }

    public override byte[] LoadFile(string file, string? arguments) {
        byte[] exe = this.ReadFile(file);
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _logger.Debug("Exe size: {@ExeSize}", exe.Length);
        }
        var exeFile = new ExeFile(exe);
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _logger.Debug("Read header: {@ReadHeader}", exeFile);
        }

        // Each process gets its own copy of the system environment strings,
        // so these need to be allocated in a separate block first.
        //byte[]  environmentBlock = new EnvironmentBlockGenerator(_machine).BuildEnvironmentBlock();

        //var fullImagePath = file;

        //ushort requestedSize = (ushort)((environmentBlock.Length >> 4) + 1 + fullImagePath.Length + 1);
        //InterruptHandlers.Dos.DosMemoryControlBlock? environmentSegment = _machine.DosMemoryManager.AllocateMemoryBlock(requestedSize);
        //if (environmentSegment is null || environmentSegment.IsValid == false)
        //    throw new InvalidVMOperationException(_machine, "Could not allocate an environnement block");

        // Copy the environment block to emulated memory.
        //environmentSegment.SetZeroTerminatedString(0,_machine.EnvironmentVariables.EnvironmentString, requestedSize);

        LoadExeFileInMemory(exeFile, _startSegment);
        ushort pspSegment = (ushort)(_startSegment - 0x10);
        SetupCpuForExe(exeFile, _startSegment, pspSegment);
        new PspGenerator(_machine).GeneratePsp(pspSegment, arguments);
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _logger.Debug("Initial CPU State: {@CpuState}", _cpu.State);
        }
        return exe;
    }

    private void LoadExeFileInMemory(ExeFile exeFile, ushort startSegment) {
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(startSegment, 0);
        _memory.LoadData(physicalStartAddress, exeFile.ProgramImage);
        foreach (SegmentedAddress address in exeFile.RelocationTable) {
            // Read value from memory, add the start segment offset and write back
            uint addressToEdit = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset) + physicalStartAddress;
            int segmentToRelocate = _memory.GetUint16(addressToEdit);
            segmentToRelocate += startSegment;
            _memory.SetUint16(addressToEdit, (ushort)segmentToRelocate);
        }
    }

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

        // Finally, MS-DOS reads the initial CS and IP values from the program's file
        // header, adjusts the CS register value by adding the start-segment address to
        // it, and transfers control to the program at the adjusted address.
        SetEntryPoint((ushort)(exeFile.InitCS + startSegment), exeFile.InitIP);
    }
}