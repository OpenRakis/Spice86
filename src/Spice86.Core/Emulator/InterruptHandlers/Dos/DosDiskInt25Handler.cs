namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

public class DosDiskInt25Handler : InterruptHandler {
    private readonly DosDriveManager _dosDriveManager;

    public DosDiskInt25Handler(IMemory memory, DosDriveManager dosDriveManager,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _dosDriveManager = dosDriveManager;
    }

    public override byte VectorNumber => 0x25;

    public override void Run() {
        if(State.AL >= DosDriveManager.MaxDriveCount || !_dosDriveManager.HasDriveAtIndex(State.AL)) {
            State.AX = 0x8002;
            State.CarryFlag = true;
        } else {
            if (State.CX == 1 && State.DX == 0) {
                if (State.AL >= 2) {
                    // write some BPB data into buffer for MicroProse installers
                    Memory.UInt16[State.DS, (ushort)(State.BX + 0x1c)] = 0x3f; // hidden sectors
                }
            } else if(LoggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                LoggerService.Warning("Interrupt 25 called but not as disk detection, {DriveIndex}", State.AL);
            }
            State.CarryFlag = false;
            State.AX = 0;
        }
    }
}
