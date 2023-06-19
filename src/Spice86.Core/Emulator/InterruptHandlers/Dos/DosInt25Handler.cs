namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// INT 25H: Absolute Disk Read
/// </summary>
public class DosInt25Handler : InterruptHandler {
    public DosInt25Handler(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        FillDispatchTable();
    }

    /// <inheritdoc />
    public override byte Index => 0x25;

    /// <inheritdoc />
    public override void Run() {
        byte operation = _state.AH;
        if (_machine.Dos.DosSwappableArea.InDos > 0) {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("Cannot call DOS kernel while a critical DOS call is in progress !");
            }
            return;
        }
        _machine.Dos.DosSwappableArea.InDos++;
        Run(operation);
        _machine.Dos.DosSwappableArea.InDos--;
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x0, new Callback(0x0, () => Default(true)));
    }

    public void Default(bool calledFromVm) {
        // return success for disk detection
        if (_state.CX == 1 || _state.DX == 1) {
            SetCarryFlag(false, calledFromVm);
            _state.AX = 0;
        } else {
            if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                _loggerService.Error("int 25 called but not for disk detection. Not implemented. {DriveNumber}", _state.AX);
            }
        }
    }
}