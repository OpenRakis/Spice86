namespace Spice86.Core.Emulator.InterruptHandlers.SystemClock;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implementation of INT1A.
/// </summary>
public class SystemClockInt1AHandler : InterruptHandler {
    private readonly TimerInt8Handler _timerHandler;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="timerHandler">The timer interrupt handler.</param>
    public SystemClockInt1AHandler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state, ILoggerService loggerService, TimerInt8Handler timerHandler)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _timerHandler = timerHandler;
        AddAction(0x00, SetSystemClockCounter);
        AddAction(0x01, GetSystemClockCounter);
        AddAction(0x81, TandySoundSystemUnhandled);
        AddAction(0x82, TandySoundSystemUnhandled);
        AddAction(0x83, TandySoundSystemUnhandled);
        AddAction(0x84, TandySoundSystemUnhandled);
        AddAction(0x85, TandySoundSystemUnhandled);
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x1A;

    /// <summary>
    /// Gets the system clock counter in AX and DX. It is used by operating systems to measure time since the system started.
    /// <remarks>
    /// Never overflows.
    /// </remarks>
    /// </summary>
    public void GetSystemClockCounter() {
        uint value = _timerHandler.TickCounterValue;
        if (LoggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET SYSTEM CLOCK COUNTER {SystemClockCounterValue}", value);
        }

        // let's say it never overflows
        State.AL = 0;
        State.CX = (ushort)(value >> 16);
        State.DX = (ushort)value;
    }

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        Run(operation);
    }

    /// <summary>
    /// Sets the system clock counter from the value in DX.
    /// </summary>
    public void SetSystemClockCounter() {
        uint value = (ushort)(State.CX << 16 | State.DX);
        if (LoggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            LoggerService.Verbose("SET SYSTEM CLOCK COUNTER {SystemClockCounterValue}", value);
        }
        _timerHandler.TickCounterValue = value;
    }

    /// <summary>
    /// Tandy sound system is not implemented. Does nothing.
    /// </summary>
    public void TandySoundSystemUnhandled() {
        if (LoggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            LoggerService.Verbose("TANDY SOUND SYSTEM IS NOT IMPLEMENTED");
        }
    }
}