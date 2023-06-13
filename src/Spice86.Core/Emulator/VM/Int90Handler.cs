
namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Shared.Interfaces;

public class Int90Handler : InterruptHandler {
    private MouseDriverSavedRegisters _savedRegisters;

    public Int90Handler(Machine machine, ILoggerService loggerService, MouseDriverSavedRegisters mouseDriverSavedRegisters) : base(machine, loggerService) {
        _savedRegisters = mouseDriverSavedRegisters;
    }

    /// <inheritdoc />
    public override void Run() {
        _loggerService.Warning("Restoring state");
        _savedRegisters.Restore(_state);
        _savedRegisters.Release();
    }

    public override byte Index => 0x90;
}