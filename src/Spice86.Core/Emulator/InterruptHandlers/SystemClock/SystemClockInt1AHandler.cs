namespace Spice86.Core.Emulator.InterruptHandlers.SystemClock;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
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
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="timerHandler">The timer interrupt handler.</param>
    public SystemClockInt1AHandler(IMemory memory, Cpu cpu, ILoggerService loggerService, TimerInt8Handler timerHandler) : base(memory, cpu, loggerService) {
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

    public void SetSystemClockCounter() {
        uint value = (ushort)(State.CX << 16 | State.DX);
        if (LoggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            LoggerService.Verbose("SET SYSTEM CLOCK COUNTER {SystemClockCounterValue}", value);
        }
        _timerHandler.TickCounterValue = value;
    }

    private void TandySoundSystemUnhandled() {
        if (LoggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            LoggerService.Verbose("TANDY SOUND SYSTEM IS NOT IMPLEMENTED");
        }
    }
}