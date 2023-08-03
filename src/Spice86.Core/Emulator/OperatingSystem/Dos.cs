namespace Spice86.Core.Emulator.OperatingSystem;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.Core.Emulator.Memory.Indexable;

using System.Linq;
using System.Text;

/// <summary>
/// Represents the DOS kernel.
/// </summary>
public class Dos {
    private const int DeviceDriverHeaderLength = 18;
    private readonly IMemory _memory;
    private readonly Cpu _cpu;
    private readonly State _state;
    private readonly IVgaFunctionality _vgaFunctionality;
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
    /// Gets the country ID from the CountryInfo table
    /// </summary>
    public byte CurrentCountryId => DosTables.CountryInfo.Country;

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
    public DosTables DosTables { get; } = new();
    
    /// <summary>
    /// Gets the current DOS master environment variables.
    /// </summary>
    public EnvironmentVariables EnvironmentVariables { get; } = new EnvironmentVariables();
    
    /// <summary>
    /// The EMS device driver.
    /// </summary>
    public ExpandedMemoryManager? Ems { get; private set; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The emulator memory.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="vgaFunctionality">The high-level VGA functions.</param>
    /// <param name="cDriveFolderPath">The host path to be mounted as C:.</param>
    /// <param name="executablePath">The host path to the DOS executable to be launched.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="keyboardInt16Handler">The keyboard interrupt controller.</param>
    public Dos(IMemory memory, Cpu cpu, KeyboardInt16Handler keyboardInt16Handler, IVgaFunctionality vgaFunctionality, string? cDriveFolderPath, string? executablePath, ILoggerService loggerService) {
        _loggerService = loggerService;
        _memory = memory;
        _cpu = cpu;
        _state = cpu.State;
        _vgaFunctionality = vgaFunctionality;
        AddDefaultDevices();
        FileManager = new DosFileManager(_memory, cDriveFolderPath, executablePath, _loggerService, this.Devices);
        MemoryManager = new DosMemoryManager(_memory, _loggerService);
        DosInt20Handler = new DosInt20Handler(_memory, _cpu, _loggerService);
        DosInt21Handler = new DosInt21Handler(_memory, _cpu, keyboardInt16Handler, _vgaFunctionality, this, _loggerService);
        DosInt2FHandler = new DosInt2fHandler(_memory, _cpu, _loggerService);
    }

    internal void Initialize(IBlasterEnvVarProvider blasterEnvVarProvider, State state, bool enableEms) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Initializing DOS");
        }
        
        OpenDefaultFileHandles();
        SetEnvironmentVariables(blasterEnvVarProvider);

        if (enableEms) {
            Ems = new(_memory, _cpu, this, _loggerService);
        }
    }

    private void SetEnvironmentVariables(IBlasterEnvVarProvider blasterEnvVarProvider) => EnvironmentVariables["BLASTER"] = blasterEnvVarProvider.BlasterString;

    private void OpenDefaultFileHandles() {
        if (Devices.Find(device => device is CharacterDevice { Name: "CON" }) is CharacterDevice con) {
            FileManager.OpenDevice(con, "r", "STDIN");
            FileManager.OpenDevice(con, "w", "STDOUT");
            FileManager.OpenDevice(con, "w", "STDERR");
        }

        if (Devices.Find(device => device is CharacterDevice { Name: "AUX" }) is CharacterDevice aux) {
            FileManager.OpenDevice(aux, "rw", "STDAUX");
        }

        if (Devices.Find(device => device is CharacterDevice { Name: "PRN" }) is CharacterDevice prn) {
            FileManager.OpenDevice(prn, "w", "STDPRN");
        }
    }

    private void AddDefaultDevices() {
        AddDevice(new ConsoleDevice(_state, _vgaFunctionality, DeviceAttributes.CurrentStdin | DeviceAttributes.CurrentStdout, "CON", _loggerService));
        AddDevice(new CharacterDevice(DeviceAttributes.Character, "AUX", _loggerService));
        AddDevice(new CharacterDevice(DeviceAttributes.Character, "PRN", _loggerService));
        AddDevice(new CharacterDevice(DeviceAttributes.Character | DeviceAttributes.CurrentClock, "CLOCK", _loggerService));
        AddDevice(new BlockDevice(DeviceAttributes.FatDevice, 1));
    }

    /// <summary>
    /// Add a device to memory so that the information can be read by both DOS and programs. 
    /// </summary>
    /// <param name="device">The character or block device to add</param>
    /// <param name="segment">The segment at which the </param>
    /// <param name="offset"></param>
    public void AddDevice(IVirtualDevice device, ushort? segment = null, ushort? offset = null) {
        // Store the location of the header
        device.Segment = segment ?? MemoryMap.DeviceDriverSegment;
        device.Offset = offset ?? (ushort)(Devices.Count * DeviceDriverHeaderLength);
        // Write the DOS device driver header to memory
        ushort index = device.Offset;
        _memory.UInt16[device.Segment, index] = 0xFFFF;
        index += 2;
        _memory.UInt16[device.Segment, index] = 0xFFFF;
        index += 2;
        _memory.UInt16[device.Segment, index] = (ushort)device.Attributes;
        index += 2;
        _memory.UInt16[device.Segment, index] = device.StrategyEntryPoint;
        index += 2;
        _memory.UInt16[device.Segment, index] = device.InterruptEntryPoint;
        index += 2;
        if (device.Attributes.HasFlag(DeviceAttributes.Character)) {
            var characterDevice = (CharacterDevice)device;
            _memory.LoadData(MemoryUtils.ToPhysicalAddress(device.Segment, index),
                Encoding.ASCII.GetBytes( $"{characterDevice.Name,-8}"));
        } else {
            var blockDevice = (BlockDevice)device;
            _memory.UInt8[device.Segment, index] = blockDevice.UnitCount;
            index++;
            _memory.LoadData(MemoryUtils.ToPhysicalAddress(device.Segment, index),
                Encoding.ASCII.GetBytes($"{blockDevice.Signature, -7}"));
        }

        // Make the previous device point to this one
        if (Devices.Count > 0) {
            IVirtualDevice previousDevice = Devices[^1];
            _memory.SegmentedAddressValue[previousDevice.Segment, previousDevice.Offset] =
                (device.Segment, device.Offset);
        }

        // Handle changing of current input, output or clock devices.
        if (device.Attributes.HasFlag(DeviceAttributes.CurrentStdin) ||
            device.Attributes.HasFlag(DeviceAttributes.CurrentStdout)) {
            CurrentConsoleDevice = (CharacterDevice)device;
        }
        if (device.Attributes.HasFlag(DeviceAttributes.CurrentClock)) {
            CurrentClockDevice = (CharacterDevice)device;
        }

        Devices.Add(device);
    }
}