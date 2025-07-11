namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.LoadableFile.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Text;

/// <summary>
/// Represents the DOS kernel.
/// </summary>
public sealed class Dos : DosFileLoader {
    //in DOSBox, this is the 'DOS_INFOBLOCK_SEG'
    private const int DosSysVarSegment = 0x80;
    private const ushort DTA_OR_COMMAND_LINE_OFFSET = 0x80;
    private const ushort LAST_FREE_SEGMENT_OFFSET = 0x02;
    private const ushort ENVIRONMENT_SEGMENT_OFFSET = 0x2C;
    private readonly BiosDataArea _biosDataArea;
    private readonly IVgaFunctionality _vgaFunctionality;
    private readonly BiosKeyboardBuffer _biosKeyboardBuffer;
    private const ushort ComOffset = 0x100;
    private readonly ushort _startSegment;
    private readonly ushort _pspSegment;

    /// <summary>
    /// Gets the INT 20h DOS services.
    /// </summary>
    public DosInt20Handler DosInt20Handler { get; }

    /// <summary>
    /// Gets the INT 21h DOS services.
    /// </summary>
    public DosInt21Handler DosInt21Handler { get; }

    /// <summary>
    /// Gets the INT 2Fh DOS services.
    /// </summary>
    public DosInt2fHandler DosInt2FHandler { get; }

    /// <summary>
    /// Gets the INT 25H DOS Disk services.
    /// </summary>
    public DosDiskInt25Handler DosInt25Handler { get; }

    /// <summary>
    /// Gets the INT26H DOS Disk services stub.
    /// </summary>
    public DosDiskInt26Handler DosInt26Handler { get; }

    /// <summary>
    /// Gets the INT 28h DOS services.
    /// </summary>
    public DosInt28Handler DosInt28Handler { get; }

    /// <summary>
    /// The class that handles DOS drives, as a sorted dictionnary.
    /// </summary>
    public DosDriveManager DosDriveManager { get; }

    /// <summary>
    /// Gets the list of virtual devices.
    /// </summary>
    public readonly List<IVirtualDevice> Devices = new();

    /// <summary>
    /// Gets or sets the current clock device.
    /// </summary>
    public CharacterDevice CurrentClockDevice { get; set; } = null!;

    /// <summary>
    /// Gets or sets the current console device.
    /// </summary>
    public CharacterDevice CurrentConsoleDevice { get; set; } = null!;

    /// <summary>
    /// Gets the DOS memory manager.
    /// </summary>
    public DosMemoryManager MemoryManager { get; }

    /// <summary>
    /// Gets the DOS file manager.
    /// </summary>
    public DosFileManager FileManager { get; }

    /// <summary>
    /// Gets the global DOS memory structures.
    /// </summary>
    public CountryInfo CountryInfo { get; } = new();

    /// <summary>
    /// Gets the current DOS master environment variables.
    /// </summary>
    public EnvironmentVariables EnvironmentVariables { get; } = new EnvironmentVariables();

    /// <summary>
    /// The movable data transfer area for DOS applications.
    /// </summary>
    public DosSwappableDataArea DosSwappableDataArea { get; }

    /// <summary>
    /// The DOS System information. Read by DOSINFO.
    /// </summary>
    /// <remarks>
    /// AKA the 'List of lists'
    /// </remarks>
    public DosSysVars DosSysVars { get; }

    /// <summary>
    /// The EMS device driver.
    /// </summary>
    public ExpandedMemoryManager? Ems { get; private set; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="configuration">An object that describes what to run and how.</param>
    /// <param name="memory">The emulator memory.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="emulationLoopRecall">The class used to wait for interrupts without blocking the emulation loop.</param>
    /// <param name="vgaFunctionality">The high-level VGA functions.</param>
    /// <param name="envVars">The DOS environment variables.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="biosKeyboardBuffer">The BIOS keyboard buffer structure in emulated memory.</param>
    /// <param name="keyboardInt16Handler">The BIOS interrupt handler that writes/reads the BIOS Keyboard Buffer.</param>
    /// <param name="biosDataArea">The memory mapped BIOS values and settings.</param>
    public Dos(Configuration configuration, IMemory memory, IFunctionHandlerProvider functionHandlerProvider,
        Stack stack, State state, EmulationLoopRecall emulationLoopRecall,
        BiosKeyboardBuffer biosKeyboardBuffer, KeyboardInt16Handler keyboardInt16Handler,
        BiosDataArea biosDataArea, IVgaFunctionality vgaFunctionality,
        IDictionary<string, string> envVars, ILoggerService loggerService)
        : base(memory, state, loggerService) {
        _startSegment = configuration.ProgramEntryPointSegment;
        _pspSegment = (ushort)(_startSegment - 0x10);
        _biosKeyboardBuffer = biosKeyboardBuffer;
        _memory = memory;
        _biosDataArea = biosDataArea;
        _state = state;
        _vgaFunctionality = vgaFunctionality;

        DosDriveManager = new(_loggerService, configuration.CDrive, configuration.Exe);

        envVars.Add("PATH", $"{DosDriveManager.CurrentDrive.DosVolume}{DosPathResolver.DirectorySeparatorChar}");

        foreach (KeyValuePair<string, string> envVar in envVars) {
            EnvironmentVariables.Add(envVar.Key, envVar.Value);
        }

        VirtualFileBase[] dosDevices = AddDefaultDevices(keyboardInt16Handler);
        DosSysVars = new DosSysVars((NullDevice)dosDevices[0], memory,
            MemoryUtils.ToPhysicalAddress(DosSysVarSegment, 0x0));

        DosSysVars.ConsoleDeviceHeaderPointer = ((IVirtualDevice)dosDevices[1]).Header.BaseAddress;

        // like DOSBox, we don't have one.
        DosSysVars.ClockDeviceHeaderPointer = 0x0;

        DosSwappableDataArea = new(_memory,
            MemoryUtils.ToPhysicalAddress(0xb2, 0));

        DosStringDecoder dosStringDecoder = new(memory, state);

        FileManager = new DosFileManager(_memory, dosStringDecoder, DosDriveManager,
            _loggerService, this.Devices);

        const ushort lastFreeSegment = MemoryMap.GraphicVideoMemorySegment - 1;

        uint pspAddress = MemoryUtils.ToPhysicalAddress(_pspSegment, 0);

        // Set the PSP's first 2 bytes to INT 20h.
        _memory.UInt16[pspAddress] = 0xCD20;

        // Set the PSP's last free segment value.
        _memory.UInt16[pspAddress + LAST_FREE_SEGMENT_OFFSET] = lastFreeSegment;

        // Load the command-line arguments into the PSP.
        _memory.LoadData(pspAddress + DTA_OR_COMMAND_LINE_OFFSET,
            ArgumentsToDosBytes(configuration.ExeArgs));

        byte[] environmentBlock = new EnvironmentBlockGenerator(EnvironmentVariables)
            .BuildEnvironmentBlock();

        //In the PSP, the Environment Block Segment field (defined at offset 0x2C) is a word, and is a pointer.
        int envBlockPointer = _pspSegment + 1;

        SegmentedAddress envBlockSegmentAddress = new SegmentedAddress((ushort)envBlockPointer, 0);

        //Copy the environment block to memory in a separated segment.
        _memory.LoadData(MemoryUtils.ToPhysicalAddress(envBlockSegmentAddress.Segment,
            envBlockSegmentAddress.Offset), environmentBlock);

        // Point the PSP's environment segment to the environment block.
        var pspEnvSegmentAddress = new SegmentedAddress(_pspSegment, ENVIRONMENT_SEGMENT_OFFSET);
        _memory.SegmentedAddress[pspEnvSegmentAddress] = envBlockSegmentAddress;

        // Set the disk transfer area address to the command-line offset in the PSP.
        FileManager.SetDiskTransferAreaAddress(_pspSegment,
            DTA_OR_COMMAND_LINE_OFFSET);

        MemoryManager = new DosMemoryManager(_memory, loggerService, _pspSegment, lastFreeSegment);
        DosInt20Handler = new DosInt20Handler(_memory, functionHandlerProvider, stack, state, _loggerService);
        DosInt21Handler = new DosInt21Handler(_memory, functionHandlerProvider, stack, state,
            keyboardInt16Handler, CountryInfo, dosStringDecoder,
            MemoryManager, FileManager, DosDriveManager, _loggerService);
        DosInt2FHandler = new DosInt2fHandler(_memory, functionHandlerProvider, stack, state, _loggerService);
        DosInt25Handler = new DosDiskInt25Handler(_memory, DosDriveManager, functionHandlerProvider, stack, state, _loggerService);
        DosInt26Handler = new DosDiskInt26Handler(_memory, DosDriveManager, functionHandlerProvider, stack, state, _loggerService);
        DosInt28Handler = new DosInt28Handler(_memory, functionHandlerProvider, stack, state, _loggerService);

        bool initializeDos = configuration.InitializeDOS is true or null;
        if (!initializeDos) {
            return;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Initializing DOS");
        }

        OpenDefaultFileHandles(dosDevices);

        if (configuration.Ems) {
            Ems = new(_memory, functionHandlerProvider, stack, state, _loggerService);
            AddDevice(Ems, ExpandedMemoryManager.DosDeviceSegment, 0);
        }
    }

    private void OpenDefaultFileHandles(VirtualFileBase[] fileDevices) {
        foreach (VirtualFileBase fileDevice in fileDevices) {
            FileManager.OpenDevice(fileDevice);
        }
    }

    private uint GetDefaultNewDeviceBaseAddress()
        => new SegmentedAddress(MemoryMap.DeviceDriverSegment, (ushort)(Devices.Count * DosDeviceHeader.HeaderLength)).Linear;

    private VirtualFileBase[] AddDefaultDevices(KeyboardInt16Handler keyboardInt16Handler) {
        var nulDevice = new NullDevice(_loggerService, _memory, GetDefaultNewDeviceBaseAddress());
        AddDevice(nulDevice);
        var consoleDevice = new ConsoleDevice(_memory, GetDefaultNewDeviceBaseAddress(),
            _loggerService, _state,
            _biosDataArea, keyboardInt16Handler, _vgaFunctionality,
            _biosKeyboardBuffer);
        AddDevice(consoleDevice);
        var printerDevice = new PrinterDevice(_loggerService, _memory, GetDefaultNewDeviceBaseAddress());
        AddDevice(printerDevice);
        var auxDevice = new AuxDevice(_loggerService, _memory, GetDefaultNewDeviceBaseAddress());
        AddDevice(auxDevice);
        for (int i = 0; i < DosDriveManager.Count; i++) {
            AddDevice(new BlockDevice(_memory, GetDefaultNewDeviceBaseAddress(),
                DeviceAttributes.FatDevice, 1));
        }
        return [nulDevice, consoleDevice, printerDevice];
    }

    /// <summary>
    /// Add a device to memory so that the information can be read by both DOS and programs.
    /// </summary>
    /// <param name="device">The DOS Device driver to add.</param>
    /// <param name="segment">The segment part of the segmented address for the DOS device header.</param>
    /// <param name="offset">The offset part of the segmented address for the DOS device header.</param>
    private void AddDevice(IVirtualDevice device, ushort? segment = null, ushort? offset = null) {
        DosDeviceHeader header = device.Header;
        // Store the location of the header
        segment ??= MemoryMap.DeviceDriverSegment;
        offset ??= (ushort)(Devices.Count * DosDeviceHeader.HeaderLength);
        // Write the DOS device driver header to memory
        ushort index = (ushort)(offset.Value + 10); //10 bytes in our DosDeviceHeader structure.
        if (header.Attributes.HasFlag(DeviceAttributes.Character)) {
            _memory.LoadData(MemoryUtils.ToPhysicalAddress(segment.Value, index),
                Encoding.ASCII.GetBytes( $"{device.Name,-8}"));
        } else if(device is BlockDevice blockDevice) {
            _memory.UInt8[segment.Value, index] = blockDevice.UnitCount;
            index++;
            _memory.LoadData(MemoryUtils.ToPhysicalAddress(segment.Value, index),
                Encoding.ASCII.GetBytes($"{blockDevice.Signature, -7}"));
        }

        // Make the previous device point to this one
        if (Devices.Count > 0) {
            IVirtualDevice previousDevice = Devices[^1];
            _memory.SegmentedAddress[previousDevice.Header.BaseAddress] =
                new SegmentedAddress(segment.Value, offset.Value);
        }

        // Handle changing of current input, output or clock devices.
        if (header.Attributes.HasFlag(DeviceAttributes.CurrentStdin) ||
            header.Attributes.HasFlag(DeviceAttributes.CurrentStdout)) {
            CurrentConsoleDevice = (CharacterDevice)device;
        }
        if (header.Attributes.HasFlag(DeviceAttributes.CurrentClock)) {
            CurrentClockDevice = (CharacterDevice)device;
        }

        Devices.Add(device);
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
        return Path.GetExtension(file).ToUpperInvariant() switch {
            ".COM" => LoadComFile(file),
            ".EXE" => LoadExeFile(file, _pspSegment),
            _ => throw new UnrecoverableException($"Unsupported file type for DOS: {file}"),
        };
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
}