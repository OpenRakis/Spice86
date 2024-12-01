namespace Spice86.Core.Emulator.LoadableFile.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Loader for BIOS files.<br/>
/// Bios entry point is at physical address 0xFFFF0 (F000:FFF0).
/// </summary>
public class BiosLoader {
    private readonly string _hostFileName;
    private readonly State _state;
    private readonly IMemory _memory;
    private readonly ILoggerService _loggerService;
    
    /// <summary>
    /// Offset of the BIOS code within the segment.
    /// </summary>
    private const ushort CodeOffset = 0xFFF0;

    /// <summary>
    /// Segment where the BIOS code is loaded.
    /// </summary>
    private const ushort CodeSegment = 0xF000;

    /// <summary>
    /// Indicates whether DOS initialization is needed for the loaded file (always false for BIOS).
    /// </summary>
    public bool DosInitializationNeeded => false;

    /// <summary>
    /// Initializes a new instance of the <see cref="BiosLoader"/> class
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="hostFileName">The absolute host path to the BIOS file.</param>
    /// <param name="loggerService">The logger service to log messages to.</param>
    public BiosLoader(IMemory memory, State state, string hostFileName, ILoggerService loggerService) {
        _hostFileName = hostFileName;
        _state = state;
        _memory = memory;
        _loggerService = loggerService;
    }
    
    /// <summary>
    /// Sets the entry point of the loaded file to the specified segment and offset values.
    /// </summary>
    /// <param name="cs">The segment value of the entry point.</param>
    /// <param name="ip">The offset value of the entry point.</param>
    private void SetEntryPoint(ushort cs, ushort ip) {
        _state.CS = cs;
        _state.IP = ip;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Program entry point is {ProgramEntry}", ConvertUtils.ToSegmentedAddressRepresentation(cs, ip));
        }
    }

    /// <summary>
    /// Loads the specified BIOS file into memory and sets the entry point to the BIOS address.
    /// </summary>
    public void LoadHostFile() {
        byte[] bios = File.ReadAllBytes(_hostFileName);
        uint physicalStartAddress = MemoryUtils.ToPhysicalAddress(CodeSegment, 0);
        _memory.LoadData(physicalStartAddress, bios);
        SetEntryPoint(CodeSegment, CodeOffset);
    }
}
