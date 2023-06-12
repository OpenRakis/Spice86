namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// BIOS service for knowing the size of conventional memory on startup.
/// See: https://stanislavs.org/helppc/int_12.html
/// </summary>
public class BiosMemoryInt12Handler : InterruptHandler {

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public BiosMemoryInt12Handler(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        _memory.SetUint16(MemoryUtils.ToPhysicalAddress(MemoryMap.BiosMemorySizeSegment, 0), 640);
        FillDispatchTable();
    }

    /// <inheritdoc/>
    public override byte Index => 0x12;
    
    private void FillDispatchTable() {
        _dispatchTable.Add(0x20, new Callback(0x20, GetBiosMemorySize));
    }
    
    /// <inheritdoc/>
    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    /// <summary>
    /// Returns the size of conventional memory in kilobytes (640), set on system startup.
    /// </summary>
    public void GetBiosMemorySize() {
        _state.AX = _memory.GetUint16(MemoryUtils.ToPhysicalAddress(MemoryMap.BiosMemorySizeSegment, 0));
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("{ClassName} INT {Int:X2} 20 {MethodName}: {MemorySize} KB",
                nameof(BiosMemoryInt12Handler), Index, nameof(GetBiosMemorySize), _state.AX);
        }
    }
}