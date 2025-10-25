namespace Spice86.Core.Emulator.InterruptHandlers.SystemClock;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implementation of INT1A.
/// </summary>
public class SystemClockInt1AHandler : InterruptHandler {
    private readonly BiosDataArea _biosDataArea;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="biosDataArea">The BIOS structure where system info is stored in memory.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public SystemClockInt1AHandler(IMemory memory, BiosDataArea biosDataArea,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack,
        State state, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _biosDataArea = biosDataArea;
        FillDispatchTable();
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x1A;

    private void FillDispatchTable() {
        //AddAction(0x00, GetSystemClockCounter);
        //AddAction(0x01, SetSystemClockCounter);
        //AddAction(0x02, ReadTimeFromRTC);
        //AddAction(0x03, SetRTCTime);
        //AddAction(0x04, ReadDateFromRTC);
        //AddAction(0x05, SetRTCDate);
        //AddAction(0x81, TandySoundSystemUnhandled);
        //AddAction(0x82, TandySoundSystemUnhandled);
        //AddAction(0x83, TandySoundSystemUnhandled);
        //AddAction(0x84, TandySoundSystemUnhandled);
        //AddAction(0x85, TandySoundSystemUnhandled);
    }

    
    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        Run(operation);
    }
}