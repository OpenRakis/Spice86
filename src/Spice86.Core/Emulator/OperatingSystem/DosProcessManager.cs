namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Text;

public class DosProcessManager : DosFileLoader {
    private const ushort DTA_OR_COMMAND_LINE_OFFSET = 0x80;
    private const ushort LAST_FREE_SEGMENT_OFFSET = 0x02;
    private const ushort ENVIRONMENT_SEGMENT_OFFSET = 0x2C;
    private const ushort ComOffset = 0x100;
    private readonly ushort _startSegment;
    private readonly ushort _pspSegment;
    private readonly DosFileManager _fileManager;
    private readonly DosDriveManager _driveManager;

    /// <summary>
    /// The master environment block that all DOS PSPs inherit.
    /// </summary>
    /// <remarks>
    /// Not stored in emulated memory, so no one can modify it.
    /// </remarks>
    private readonly EnvironmentVariables _environmentVariables;

    public const ushort LastFreeSegment = MemoryMap.GraphicVideoMemorySegment - 1;

    public ushort PspSegment => _pspSegment;

    public DosProcessManager(Configuration configuration, IMemory memory,
        State state, DosFileManager dosFileManager, DosDriveManager dosDriveManager,
        IDictionary<string, string> envVars, ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _fileManager = dosFileManager;
        _driveManager = dosDriveManager;
        _environmentVariables = new();
        _startSegment = configuration.ProgramEntryPointSegment;
        _pspSegment = (ushort)(_startSegment - 0x10);

        envVars.Add("PATH", $"{_driveManager.CurrentDrive.DosVolume}{DosPathResolver.DirectorySeparatorChar}");

        foreach (KeyValuePair<string, string> envVar in envVars) {
            _environmentVariables.Add(envVar.Key, envVar.Value);
        }
    }


    /// <summary>
    /// Converts the specified command-line arguments string into the format used by DOS.
    /// </summary>
    /// <param name="arguments">The command-line arguments string.</param>
    /// <returns>The command-line arguments in the format used by DOS.</returns>
    private static byte[] ArgumentsToDosBytes(string? arguments) {
        byte[] res = new byte[128];
        string correctLengthArguments = "";
        if (string.IsNullOrWhiteSpace(arguments) == false) {
            // Cut strings longer than 127 characters.
            correctLengthArguments = arguments.Length > 127 ? arguments[..127] : arguments;
        }

        // Set the command line size.
        res[0] = (byte)correctLengthArguments.Length;

        byte[] argumentsBytes = Encoding.UTF8.GetBytes(correctLengthArguments);

        // Copy the actual characters.
        int index = 0;
        for (; index < correctLengthArguments.Length; index++) {
            res[index + 1] = argumentsBytes[index];
        }

        res[index + 1] = 0x0D; // Carriage return.
        return res;
    }

    public override byte[] LoadFile(string file, string? arguments) {
        SetupStackSpace(file, arguments);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Loading file: {File} with arguments: {Arguments}", file, arguments);
        }
        uint pspAddress = MemoryUtils.ToPhysicalAddress(PspSegment, 0);

        // Set the PSP's first 2 bytes to INT 20h.
        _memory.UInt16[pspAddress] = 0xCD20;

        // Set the PSP's last free segment value.
        _memory.UInt16[pspAddress + LAST_FREE_SEGMENT_OFFSET] = LastFreeSegment;

        // Load the command-line arguments into the PSP.
        _memory.LoadData(pspAddress + DTA_OR_COMMAND_LINE_OFFSET,
            ArgumentsToDosBytes(arguments));

        byte[] environmentBlock = new EnvironmentBlockGenerator(_environmentVariables)
            .BuildEnvironmentBlock();

        //In the PSP, the Environment Block Segment field (defined at offset 0x2C) is a word, and is a pointer.
        int envBlockPointer = PspSegment + 1;

        SegmentedAddress envBlockSegmentAddress = new SegmentedAddress((ushort)envBlockPointer, 0);

        //Copy the environment block to memory in a separated segment.
        _memory.LoadData(MemoryUtils.ToPhysicalAddress(envBlockSegmentAddress.Segment,
            envBlockSegmentAddress.Offset), environmentBlock);

        // Point the PSP's environment segment to the environment block.
        var pspEnvSegmentAddress = new SegmentedAddress(PspSegment, ENVIRONMENT_SEGMENT_OFFSET);
        _memory.SegmentedAddress[pspEnvSegmentAddress] = envBlockSegmentAddress;

        // Set the disk transfer area address to the command-line offset in the PSP.
        _fileManager.SetDiskTransferAreaAddress(PspSegment,
            DTA_OR_COMMAND_LINE_OFFSET);

        var result = Path.GetExtension(file).ToUpperInvariant() switch {
            ".COM" => LoadComFile(file),
            ".EXE" => LoadExeFile(file, PspSegment),
            _ => throw new UnrecoverableException($"Unsupported file type for DOS: {file}"),
        };

        return result;
    }

    private byte[] LoadComFile(string file) {
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

    private byte[] LoadExeFile(string file, ushort pspSegment) {
        byte[] exe = ReadFile(file);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Exe size: {ExeSize}", exe.Length);
        }
        DosExeFile exeFile = new DosExeFile(new ByteArrayReaderWriter(exe));
        if (!exeFile.IsValid) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Invalid EXE file {File}", file);
            }
            throw new UnrecoverableException($"Invalid EXE file {file}");
        }
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Read header: {ReadHeader}", exeFile);
        }

        LoadExeFileInMemoryAndApplyRelocations(exeFile, _startSegment);
        SetupCpuForExe(exeFile, _startSegment, pspSegment);
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
    private void LoadExeFileInMemoryAndApplyRelocations(DosExeFile exeFile, ushort startSegment) {
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(startSegment, 0);
        _memory.LoadData(physicalStartAddress, exeFile.ProgramImage, (int)exeFile.ProgramSize);
        foreach (SegmentedAddress address in exeFile.RelocationTable) {
            // Read value from memory, add the start segment offset and write back
            uint addressToEdit = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset)
                + physicalStartAddress;
            _memory.UInt16[addressToEdit] += startSegment;
        }
    }

    /// <summary>
    /// Sets up the CPU to execute the loaded program.
    /// </summary>
    /// <param name="exeFile">The EXE file that was loaded.</param>
    /// <param name="startSegment">The starting segment address of the program.</param>
    /// <param name="pspSegment">The segment address of the program's PSP (Program Segment Prefix).</param>
    private void SetupCpuForExe(DosExeFile exeFile, ushort startSegment, ushort pspSegment) {
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

    private void SetupStackSpace(string file, string? arguments) {
        // Reserve 0x200 bytes of stack space
        _state.SP -= 0x200;

        // Calculate the physical address where the stack begins
        uint stackPhysicalAddress = MemoryUtils.ToPhysicalAddress(_state.SS, _state.SP);

        // Write filename to stack at offset 0x20
        string fileNameOnly = Path.GetFileName(file);
        _memory.SetZeroTerminatedString(stackPhysicalAddress + 0x20, fileNameOnly, 128);

        // Setup command line at offset 0x100
        byte[] cmdLine = ArgumentsToDosBytes(arguments);
        _memory.LoadData(stackPhysicalAddress + 0x100, cmdLine);

        // Setup FCB1 and FCB2 in PSP (File Control Blocks at offsets 0x5C and 0x6C)
        if (!string.IsNullOrEmpty(arguments)) {
            string[] parts = arguments.Split(new[] { ' ', '\t', ',', ';', '=' },
                StringSplitOptions.RemoveEmptyEntries);

            // Initialize FCBs by clearing them first (36 bytes each)
            _memory.Memset8(MemoryUtils.ToPhysicalAddress(PspSegment, 0x5C), 0, 36);
            _memory.Memset8(MemoryUtils.ToPhysicalAddress(PspSegment, 0x6C), 0, 36);

            // Parse first two parameters for FCBs if they exist
            if (parts.Length > 0) {
                PopulateFcb(parts[0], PspSegment, 0x5C);
            }
            if (parts.Length > 1) {
                PopulateFcb(parts[1], PspSegment, 0x6C);
            }
        }

        _state.SP += 0x200; // Restore SP after setting up the stack
    }

    private void PopulateFcb(string param, ushort segment, ushort offset) {
        // Basic FCB setup - just the filename part
        if (param.StartsWith('/') && param.Length > 1) {
            param = param[1..];
        }

        // Extract drive letter if present
        byte driveNumber = 0; // Default drive
        if (param.Length > 1 && param[1] == ':') {
            driveNumber = (byte)(char.ToUpperInvariant(param[0]) - 'A' + 1);
            param = param[2..];
        }

        // Set drive number in FCB (first byte)
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        _memory.UInt8[fcbAddress] = driveNumber;

        // Convert filename to 8.3 format and write to FCB
        string fileName = Path.GetFileNameWithoutExtension(param);
        fileName = fileName.Length > 8 ? fileName[..8] : fileName.PadRight(8, ' ');

        // Write filename (8 chars)
        byte[] fileNameBytes = Encoding.ASCII.GetBytes(fileName);
        _memory.LoadData(fcbAddress + 1, fileNameBytes, Math.Min(fileNameBytes.Length, 8));

        // Write extension (3 chars) if present
        string extension = Path.GetExtension(param);
        if (!string.IsNullOrEmpty(extension)) {
            extension = extension[1..];
            extension = extension.Length > 3 ? extension[..3] : extension.PadRight(3, ' ');
            byte[] extBytes = Encoding.ASCII.GetBytes(extension);
            _memory.LoadData(fcbAddress + 9, extBytes, Math.Min(extBytes.Length, 3));
        }
    }
}