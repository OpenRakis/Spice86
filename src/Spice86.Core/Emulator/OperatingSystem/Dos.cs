namespace Spice86.Core.Emulator.OperatingSystem;

using System.Linq;
using System.Text;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Represents the DOS kernel.
/// </summary>
public class Dos {
    private const int DeviceDriverHeaderLength = 18;
    private readonly Machine _machine;
    private readonly ILoggerService _logger;
    
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
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="logger">The logger service implementation.</param>
    public Dos(Machine machine, ILoggerService logger) {
        _machine = machine;
        _logger = logger;
        FileManager = new DosFileManager(_machine.Memory, _logger, this);
        MemoryManager = new DosMemoryManager(_machine.Memory, _logger);
        DosInt20Handler = new DosInt20Handler(_machine, _logger);
        DosInt21Handler = new DosInt21Handler(_machine, _logger, this);
        DosInt2FHandler = new DosInt2fHandler(_machine, _logger);
    }

    internal void Initialize(IBlasterEnvVarProvider blasterEnvVarProvider) {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("Initializing DOS");
        }

        _machine.RegisterCallbackHandler(DosInt20Handler);
        _machine.RegisterCallbackHandler(DosInt21Handler);
        _machine.RegisterCallbackHandler(DosInt2FHandler);

        AddDefaultDevices();
        OpenDefaultFileHandles();
        SetEnvironmentVariables(blasterEnvVarProvider);
    }

    private void SetEnvironmentVariables(IBlasterEnvVarProvider blasterEnvVarProvider) {
        EnvironmentVariables["BLASTER"] = blasterEnvVarProvider.BlasterString;
    }

    private void OpenDefaultFileHandles() {
        if (Devices.FirstOrDefault(device => device is CharacterDevice { Name: "CON" }) is CharacterDevice con) {
            FileManager.OpenDevice(con, "r", "STDIN");
            FileManager.OpenDevice(con, "w", "STDOUT");
            FileManager.OpenDevice(con, "w", "STDERR");
        }

        if (Devices.FirstOrDefault(device => device is CharacterDevice { Name: "AUX" }) is CharacterDevice aux) {
            FileManager.OpenDevice(aux, "rw", "STDAUX");
        }

        if (Devices.FirstOrDefault(device => device is CharacterDevice { Name: "PRN" }) is CharacterDevice prn) {
            FileManager.OpenDevice(prn, "w", "STDPRN");
        }
    }

    private void AddDefaultDevices() {
        AddDevice(new ConsoleDevice(DeviceAttributes.CurrentStdin | DeviceAttributes.CurrentStdout, "CON", _machine, _logger));
        AddDevice(new CharacterDevice(DeviceAttributes.Character, "AUX", _logger));
        AddDevice(new CharacterDevice(DeviceAttributes.Character, "PRN", _logger));
        AddDevice(new CharacterDevice(DeviceAttributes.Character | DeviceAttributes.CurrentClock, "CLOCK", _logger));
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
        _machine.Memory.UInt16[device.Segment, index] = 0xFFFF;
        index += 2;
        _machine.Memory.UInt16[device.Segment, index] = 0xFFFF;
        index += 2;
        _machine.Memory.UInt16[device.Segment, index] = (ushort)device.Attributes;
        index += 2;
        _machine.Memory.UInt16[device.Segment, index] = device.StrategyEntryPoint;
        index += 2;
        _machine.Memory.UInt16[device.Segment, index] = device.InterruptEntryPoint;
        index += 2;
        if (device.Attributes.HasFlag(DeviceAttributes.Character)) {
            var characterDevice = (CharacterDevice)device;
            _machine.Memory.LoadData(MemoryUtils.ToPhysicalAddress(device.Segment, index),
                Encoding.ASCII.GetBytes( $"{characterDevice.Name,-8}"));
        } else {
            var blockDevice = (BlockDevice)device;
            _machine.Memory.UInt8[device.Segment, index] = blockDevice.UnitCount;
            index += 1;
            _machine.Memory.LoadData(MemoryUtils.ToPhysicalAddress(device.Segment, index),
                Encoding.ASCII.GetBytes($"{blockDevice.Signature, -7}"));
        }

        // Make the previous device point to this one
        if (Devices.Count > 0) {
            IVirtualDevice previousDevice = Devices[^1];
            _machine.Memory.UInt16[previousDevice.Segment, previousDevice.Offset] = device.Offset;
            _machine.Memory.UInt16[previousDevice.Segment, (ushort)(previousDevice.Offset + 2)] = device.Segment;
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