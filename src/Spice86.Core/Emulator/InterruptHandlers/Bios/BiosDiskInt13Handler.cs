namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// BIOS services for interacting with storage media (floppy disk, ...).
/// See: https://stanislavs.org/helppc/int_13.html
/// </summary>
public class BiosDiskInt13Handler : InterruptHandler {

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public BiosDiskInt13Handler(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        FillDispatchTable();
    }

    /// <inheritdoc/>
    public override byte Index => 0x13;
    
    private void FillDispatchTable() {
        _dispatchTable.Add(0x0, new Callback(0x0, ResetDiskSystem));
    }
    
    /// <inheritdoc/>
    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    /// <summary>
    /// Resets the floppy drive or hard disk at the mechanical level.
    /// <remarks>Only sets AL to 0 to report success.</remarks>
    /// </summary>
    public void ResetDiskSystem() {
        _state.AL = 0;
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("{ClassName} INT {Int:X2} 00 {MethodName}: Not implemented, returning 0",
            nameof(BiosDiskInt13Handler), Index, nameof(ResetDiskSystem));
        }
    }
}