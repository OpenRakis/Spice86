namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Text;

/// <summary>
/// Emulates DOS (Disk Operating System) kernel services for real mode programs.
/// </summary>
/// <remarks>
/// This class provides implementations of DOS system calls typically accessed through software interrupts:
/// <list type="bullet">
/// <item><b>INT 20h</b>: Program termination</item>
/// <item><b>INT 21h</b>: Primary DOS services (file I/O, memory management, process control, etc.)</item>
/// <item><b>INT 2Fh</b>: Multiplexer interrupt (TSR communication, SHARE, MSCDEX, etc.)</item>
/// </list>
/// <para>
/// The DOS implementation includes support for:
/// <list type="bullet">
/// <item>File system operations (open, read, write, close, seek)</item>
/// <item>Memory management (MCB chain, allocation/deallocation)</item>
/// <item>Process control (EXEC, terminate, return codes)</item>
/// <item>Extended memory services (EMS, XMS)</item>
/// <item>Device drivers and character I/O</item>
/// </list>
/// </para>
/// </remarks>
public sealed class Dos {
    //in DOSBox, this is the 'DOS_INFOBLOCK_SEG'
    private const int DosSysVarSegment = 0x80;
    private readonly BiosDataArea _biosDataArea;
    private readonly IVgaFunctionality _vgaFunctionality;
    private readonly BiosKeyboardBuffer _biosKeyboardBuffer;
    private readonly IMemory _memory;
    private readonly ILoggerService _loggerService;

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
    /// Gets the DOS process manager.
    /// </summary>
    public DosProcessManager ProcessManager { get; }

    /// <summary>
    /// Gets the global DOS region settings structure.
    /// </summary>
    public CountryInfo CountryInfo { get; }

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
    /// The DOS tables including CDS and DBCS structures.
    /// </summary>
    public DosTables DosTables { get; }

    /// <summary>
    /// The EMS device driver.
    /// </summary>
    public ExpandedMemoryManager? Ems { get; private set; }

    public ExtendedMemoryManager? Xms { get; private set; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="configuration">An object that describes what to run and how.</param>
    /// <param name="memory">The emulator memory.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="biosKeyboardBuffer">The BIOS keyboard buffer structure in emulated memory.</param>
    /// <param name="keyboardInt16Handler">The BIOS interrupt handler that writes/reads the BIOS Keyboard Buffer.</param>
    /// <param name="biosDataArea">The memory mapped BIOS values and settings.</param>
    /// <param name="vgaFunctionality">The high-level VGA functions.</param>
    /// <param name="envVars">The DOS environment variables.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="ioPortDispatcher">The I/O port dispatcher for accessing hardware ports.</param>
    /// <param name="dosTables">The DOS tables structure.</param>
    /// <param name="xms">Optional XMS manager to expose through DOS.</param>
    public Dos(Configuration configuration, IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        BiosKeyboardBuffer biosKeyboardBuffer, KeyboardInt16Handler keyboardInt16Handler,
        BiosDataArea biosDataArea, IVgaFunctionality vgaFunctionality,
        IDictionary<string, string> envVars, ILoggerService loggerService,
        IOPortDispatcher ioPortDispatcher, DosTables dosTables,
        ExtendedMemoryManager? xms = null) {
        _loggerService = loggerService;
        Xms = xms;
        _biosKeyboardBuffer = biosKeyboardBuffer;
        _memory = memory;
        _biosDataArea = biosDataArea;
        _vgaFunctionality = vgaFunctionality;

        DosDriveManager = new(_loggerService, configuration.CDrive, configuration.Exe);

        VirtualFileBase[] dosDevices = AddDefaultDevices(state, keyboardInt16Handler);
        DosSysVars = new DosSysVars(configuration, (NullDevice)dosDevices[0], memory,
            MemoryUtils.ToPhysicalAddress(DosSysVarSegment, 0x0));

        DosSysVars.ConsoleDeviceHeaderPointer = ((IVirtualDevice)dosDevices[1]).Header.BaseAddress;

        // Initialize DOS tables (CDS and DBCS structures)
        DosTables = dosTables;
        DosTables.Initialize(memory);

        // Set up the CDS pointer in DosSysVars
        if (DosTables.CurrentDirectoryStructure is not null) {
            DosSysVars.CurrentDirectoryStructureListPointer = DosTables.CurrentDirectoryStructure.BaseAddress;
            DosSysVars.CurrentDirectoryStructureCount = 26; // Support A-Z drives
        }

        DosSwappableDataArea = new(_memory,
            MemoryUtils.ToPhysicalAddress(0xb2, 0));

        DosStringDecoder dosStringDecoder = new(memory, state);

        CountryInfo = new();
        FileManager = new DosFileManager(_memory, dosStringDecoder, DosDriveManager,
            _loggerService, Devices);
        DosProgramSegmentPrefixTracker pspTracker = new(configuration, _memory, loggerService);
        MemoryManager = new DosMemoryManager(_memory, pspTracker, loggerService);
        ProcessManager = new(_memory, state, pspTracker, MemoryManager, FileManager, DosDriveManager, envVars, loggerService);
        DosInt20Handler = new DosInt20Handler(_memory, functionHandlerProvider, stack, state, ProcessManager, _loggerService);
        DosInt21Handler = new DosInt21Handler(_memory, pspTracker, functionHandlerProvider, stack, state,
            keyboardInt16Handler, CountryInfo, dosStringDecoder,
            MemoryManager, FileManager, DosDriveManager, ProcessManager, ioPortDispatcher, DosTables, _loggerService);
        DosInt2FHandler = new DosInt2fHandler(_memory,
            functionHandlerProvider, stack, state, _loggerService, xms);
        DosInt25Handler = new DosDiskInt25Handler(_memory, DosDriveManager,
            functionHandlerProvider, stack, state, _loggerService);
        DosInt26Handler = new DosDiskInt26Handler(_memory, DosDriveManager,
            functionHandlerProvider, stack, state, _loggerService);
        DosInt28Handler = new DosInt28Handler(_memory, functionHandlerProvider,
            stack, state, _loggerService);

        if (configuration.InitializeDOS is false) {
            return;
        }
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Initializing DOS");
        }
        OpenDefaultFileHandles(dosDevices);

        if (configuration.Xms is not false && xms is not null) {
            Xms = xms;
            AddDevice(xms, ExtendedMemoryManager.DosDeviceSegment, 0);
        }

        if (configuration.Ems is not false) {
            Ems = new(_memory, functionHandlerProvider, stack, state, _loggerService);
            AddDevice(Ems.AsCharacterDevice(), ExpandedMemoryManager.DosDeviceSegment, 0);
        }
    }

    private void OpenDefaultFileHandles(VirtualFileBase[] fileDevices) {
        foreach (VirtualFileBase fileDevice in fileDevices) {
            FileManager.OpenDevice(fileDevice);
        }
    }

    private uint GetDefaultNewDeviceBaseAddress()
        => new SegmentedAddress(MemoryMap.DeviceDriversSegment, (ushort)(Devices.Count * DosDeviceHeader.HeaderLength)).Linear;

    private VirtualFileBase[] AddDefaultDevices(State state, KeyboardInt16Handler keyboardInt16Handler) {
        var nulDevice = new NullDevice(_loggerService, _memory, GetDefaultNewDeviceBaseAddress());
        AddDevice(nulDevice);
        var consoleDevice = new ConsoleDevice(_memory, GetDefaultNewDeviceBaseAddress(),
            _loggerService, state,
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

        // Initialize the header with proper values using MemoryBasedDataStructure accessors
        header.StrategyEntryPoint = 0;  // Internal devices don't use real strategy routines
        header.InterruptEntryPoint = 0; // Internal devices don't use real interrupt routines

        // Write device-specific data (name for character devices, unit count for block devices)
        if (header.Attributes.HasFlag(DeviceAttributes.Character)) {
            header.Name = device.Name;
        } else if (device is BlockDevice blockDevice) {
            header.UnitCount = blockDevice.UnitCount;
            // Write signature if present (7 bytes starting at offset 0x0B)
            if (!string.IsNullOrEmpty(blockDevice.Signature)) {
                byte[] sigBytes = Encoding.ASCII.GetBytes(blockDevice.Signature.PadRight(7)[..7]);
                _memory.LoadData(header.BaseAddress + 0x0B, sigBytes);
            }
        }

        // Link the previous device to this one
        if (Devices.Count > 0) {
            IVirtualDevice previousDevice = Devices[^1];
            previousDevice.Header.NextDevicePointer = new SegmentedAddress(
                (ushort)(header.BaseAddress >> 4),
                (ushort)(header.BaseAddress & 0x0F)
            );
        }

        // Handle changing of current input, output or clock devices
        if (header.Attributes.HasFlag(DeviceAttributes.CurrentStdin) ||
            header.Attributes.HasFlag(DeviceAttributes.CurrentStdout)) {
            CurrentConsoleDevice = (CharacterDevice)device;
        }
        if (header.Attributes.HasFlag(DeviceAttributes.CurrentClock)) {
            CurrentClockDevice = (CharacterDevice)device;
        }
        Devices.Add(device);
    }
}