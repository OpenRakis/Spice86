namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Minimal INT 2Ah handler (DOS Multiplex/Busy) to avoid uninitialized vector faults.
/// FreeDOS leaves many subfunctions unimplemented; we return immediately.
/// </summary>
public class DosInt2AHandler : InterruptHandler {
    public DosInt2AHandler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider,
        Stack stack, State state, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
    }

    public override byte VectorNumber => 0x2A;

    public override void Run() {
        // Stub: simply return. Carry is left unchanged intentionally; FreeDOS uses no-op for many subfunctions.
        LoggerService.Verbose("INT 2Ah stub invoked");
    }
}
