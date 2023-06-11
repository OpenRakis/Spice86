namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// INT 26H: Absolute Disk Write
/// </summary>
public class DosInt26Handler : InterruptHandler {
    public DosInt26Handler(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        FillDispatchTable();
    }

    /// <inheritdoc />
    public override byte Index => 0x26;

    /// <inheritdoc />
    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x0, new Callback(0x0, () => Default(true)));
    }

    public void Default(bool calledFromVm) {
        //Always return success.
        SetCarryFlag(false, calledFromVm);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Absolute disk write called. Not implemented, but returned success");
        }
        _state.AX = 0;
    }
}