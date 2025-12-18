namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Minimal INT 2Ah multiplex handler. FreeDOS initializes this vector; we provide a no-op/IRET so callers don't fault.
/// </summary>
public class DosInt2AHandler : InterruptHandler {
    public DosInt2AHandler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider,
        Stack stack, State state, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
    }

    public override byte VectorNumber => 0x2A;

    public override void Run() {
        // FreeDOS returns immediately for many subfunctions; keep it as a no-op/IRET.
    }
}
