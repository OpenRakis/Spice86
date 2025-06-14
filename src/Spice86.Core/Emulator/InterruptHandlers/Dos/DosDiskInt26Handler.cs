namespace Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Interfaces;

public class DosDiskInt26Handler : InterruptHandler {
    private readonly DosDriveManager _dosDriveManager;

    public DosDiskInt26Handler(IMemory memory, DosDriveManager dosDriveManager,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _dosDriveManager = dosDriveManager;
    }

    public override byte VectorNumber => 0x26;

    public override void Run() {
        if (LoggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            LoggerService.Warning("DOS INT26H was called, hope for the best!");
        }
        if (State.AL >= DosDriveManager.MaxDriveCount || !_dosDriveManager.HasDriveAtIndex(State.AL)) {
            State.AX = 0x8002;
            State.CarryFlag = true;
        } else {
            State.CarryFlag = false;
            State.AX = 0;
        }
    }
}
