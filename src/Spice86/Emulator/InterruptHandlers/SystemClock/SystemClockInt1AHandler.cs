namespace Spice86.Emulator.InterruptHandlers.SystemClock;

using Serilog;

using Spice86.Emulator.Callback;
using Spice86.Emulator.InterruptHandlers.Timer;
using Spice86.Emulator.VM;

/// <summary>
/// Implementation of int1A.
/// </summary>
public class SystemClockInt1AHandler : InterruptHandler {
    private static readonly ILogger _logger = Log.Logger.ForContext<SystemClockInt1AHandler>();
    private readonly TimerInt8Handler timerHandler;

    public SystemClockInt1AHandler(Machine machine, TimerInt8Handler timerHandler) : base(machine) {
        this.timerHandler = timerHandler;
        _dispatchTable.Add(0x00, new Callback(0x00, () => SetSystemClockCounter()));
        _dispatchTable.Add(0x01, new Callback(0x01, () => GetSystemClockCounter()));
        _dispatchTable.Add(0x81, new Callback(0x81, () => TandySoundSystemUnhandled()));
        _dispatchTable.Add(0x82, new Callback(0x82, () => TandySoundSystemUnhandled()));
        _dispatchTable.Add(0x83, new Callback(0x83, () => TandySoundSystemUnhandled()));
        _dispatchTable.Add(0x84, new Callback(0x84, () => TandySoundSystemUnhandled()));
        _dispatchTable.Add(0x85, new Callback(0x85, () => TandySoundSystemUnhandled()));
    }

    public override int GetIndex() {
        return 0x1A;
    }

    public void GetSystemClockCounter() {
        int value = timerHandler.GetTickCounterValue();
        _logger.Information("GET SYSTEM CLOCK COUNTER {@SystemClockCounterValue}", value);

        // let's say it never overflows
        _state.SetAL(0);
        _state.SetCX(value >> 16);
        _state.SetDX(value);
    }

    public override void Run() {
        int operation = _state.GetAH();
        Run(operation);
    }

    public void SetSystemClockCounter() {
        int value = _state.GetCX() << 16 | _state.GetDX();
        _logger.Information("SET SYSTEM CLOCK COUNTER {@SystemClockCounterValue}", value);
        timerHandler.SetTickCounterValue(value);
    }

    private void TandySoundSystemUnhandled() {
        _logger.Information("TANDY SOUND SYSTEM IS NOT IMPLEMENTED");
    }
}