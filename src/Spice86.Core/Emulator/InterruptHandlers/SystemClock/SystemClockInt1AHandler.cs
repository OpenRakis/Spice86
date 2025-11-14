namespace Spice86.Core.Emulator.InterruptHandlers.SystemClock;

using Serilog.Events;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implementation of INT1A - BIOS Time Services.
/// Provides access to system clock counter and RTC (Real-Time Clock) functions.
/// </summary>
public class SystemClockInt1AHandler : InterruptHandler {
    private readonly BiosDataArea _biosDataArea;
    private readonly RealTimeClock _realTimeClock;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="biosDataArea">The BIOS structure where system info is stored in memory.</param>
    /// <param name="realTimeClock">The RTC/CMOS device for reading and setting date/time.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public SystemClockInt1AHandler(IMemory memory, BiosDataArea biosDataArea,
        RealTimeClock realTimeClock, IFunctionHandlerProvider functionHandlerProvider, 
        Stack stack, State state, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _biosDataArea = biosDataArea;
        _realTimeClock = realTimeClock;
        FillDispatchTable();
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x1A;

    private void FillDispatchTable() {
        AddAction(0x00, GetSystemClockCounter);
        AddAction(0x01, SetSystemClockCounter);
        AddAction(0x02, ReadTimeFromRTC);
        AddAction(0x03, SetRTCTime);
        AddAction(0x04, ReadDateFromRTC);
        AddAction(0x05, SetRTCDate);
    }

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        Run(operation);
    }

    /// <summary>
    /// INT 1A, AH=00h - Get System Clock Counter.
    /// Returns the number of clock ticks since midnight.
    /// Clock ticks at 18.2 Hz (approximately 1,193,180 / 65,536 times per second).
    /// </summary>
    private void GetSystemClockCounter() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1A, AH=00h - Get System Clock Counter");
        }

        uint ticks = _biosDataArea.TimerCounter;
        State.CX = (ushort)(ticks >> 16);
        State.DX = (ushort)(ticks & 0xFFFF);
        State.AL = _biosDataArea.TimerRollover;
        
        // Clear rollover flag after reading
        _biosDataArea.TimerRollover = 0;
    }

    /// <summary>
    /// INT 1A, AH=01h - Set System Clock Counter.
    /// Sets the system clock counter to the specified value.
    /// </summary>
    private void SetSystemClockCounter() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1A, AH=01h - Set System Clock Counter");
        }

        uint ticks = ((uint)State.CX << 16) | State.DX;
        _biosDataArea.TimerCounter = ticks;
        _biosDataArea.TimerRollover = 0;
    }

    /// <summary>
    /// INT 1A, AH=02h - Read Time from RTC.
    /// Returns time in BCD format from the Real-Time Clock.
    /// </summary>
    private void ReadTimeFromRTC() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1A, AH=02h - Read Time from RTC");
        }

        DateTime now = DateTime.Now;
        State.CH = BcdConverter.ToBcd((byte)now.Hour);
        State.CL = BcdConverter.ToBcd((byte)now.Minute);
        State.DH = BcdConverter.ToBcd((byte)now.Second);
        State.DL = 0; // Standard time (not daylight savings)
        State.CarryFlag = false;
    }

    /// <summary>
    /// INT 1A, AH=03h - Set RTC Time.
    /// This is a stub implementation - returns error as setting system time is not supported.
    /// </summary>
    private void SetRTCTime() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1A, AH=03h - Set RTC Time (stub - not implemented, returning error)");
        }
        // Return error - we don't support changing the system time
        State.CarryFlag = true;
    }

    /// <summary>
    /// INT 1A, AH=04h - Read Date from RTC.
    /// Returns date in BCD format from the Real-Time Clock.
    /// </summary>
    private void ReadDateFromRTC() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1A, AH=04h - Read Date from RTC");
        }

        DateTime now = DateTime.Now;
        State.CH = BcdConverter.ToBcd((byte)(now.Year / 100));
        State.CL = BcdConverter.ToBcd((byte)(now.Year % 100));
        State.DH = BcdConverter.ToBcd((byte)now.Month);
        State.DL = BcdConverter.ToBcd((byte)now.Day);
        State.CarryFlag = false;
    }

    /// <summary>
    /// INT 1A, AH=05h - Set RTC Date.
    /// This is a stub implementation - returns error as setting system date is not supported.
    /// </summary>
    private void SetRTCDate() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1A, AH=05h - Set RTC Date (stub - not implemented, returning error)");
        }
        // Return error - we don't support changing the system date
        State.CarryFlag = true;
    }
}