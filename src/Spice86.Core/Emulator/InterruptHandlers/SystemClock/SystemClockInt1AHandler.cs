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
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("GET SYSTEM CLOCK COUNTER {SystemClockCounterValue}", value);
        }

        // let's say it never overflows
        _state.AL = 0;
        _state.CX = (ushort)(value >> 16);
        _state.DX = (ushort)value;
    }

    /// <inheritdoc />
    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    public void SetSystemClockCounter() {
        uint value = (ushort)(_state.CX << 16 | _state.DX);
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("SET SYSTEM CLOCK COUNTER {SystemClockCounterValue}", value);
        }
        _timerHandler.TickCounterValue = value;
    }

    private void TandySoundSystemUnhandled() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("TANDY SOUND SYSTEM IS NOT IMPLEMENTED");
        }
    }
}