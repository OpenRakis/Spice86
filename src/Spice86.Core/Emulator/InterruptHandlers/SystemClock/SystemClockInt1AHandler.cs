namespace Spice86.Core.Emulator.InterruptHandlers.SystemClock;

using Serilog.Events;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;
using System;

/// <summary>
/// Implementation of INT 1Ah - System Clock and RTC services.
/// </summary>
public class SystemClockInt1AHandler : InterruptHandler {
    private readonly BiosDataArea _biosDataArea;
    private readonly RealTimeClock _realTimeClock;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="biosDataArea">The BIOS structure where system info is stored in memory.</param>
    /// <param name="realTimeClock">The RTC/CMOS emulation.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public SystemClockInt1AHandler(IMemory memory, BiosDataArea biosDataArea, RealTimeClock realTimeClock,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack,
        State state, ILoggerService loggerService)
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
        AddAction(0x81, TandySoundSystemUnhandled);
        AddAction(0x82, TandySoundSystemUnhandled);
        AddAction(0x83, TandySoundSystemUnhandled);
        AddAction(0x84, TandySoundSystemUnhandled);
        AddAction(0x85, TandySoundSystemUnhandled);
    }

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        Run(operation);
    }

    /// <summary>
    /// INT 1Ah, AH=00h - Get system clock counter.
    /// Returns the number of timer ticks since midnight in CX:DX.
    /// AL is set to 1 if midnight passed since last call, 0 otherwise.
    /// </summary>
    public void GetSystemClockCounter() {
        uint value = _biosDataArea.TimerCounter;
        byte rollover = _biosDataArea.TimerRollover;

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1Ah, AH=00h - GET SYSTEM CLOCK COUNTER: {Value:X8}, rollover={Rollover}", value, rollover);
        }

        State.AL = rollover;
        State.CX = (ushort)(value >> 16);
        State.DX = (ushort)value;

        // Clear rollover flag after reading
        _biosDataArea.TimerRollover = 0;
    }

    /// <summary>
    /// INT 1Ah, AH=01h - Set system clock counter.
    /// Sets the timer counter from CX:DX.
    /// </summary>
    public void SetSystemClockCounter() {
        uint value = ((uint)State.CX << 16) | State.DX;

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1Ah, AH=01h - SET SYSTEM CLOCK COUNTER: {Value:X8}", value);
        }

        _biosDataArea.TimerCounter = value;
        _biosDataArea.TimerRollover = 0;
    }

    /// <summary>
    /// INT 1Ah, AH=02h - Read time from RTC.
    /// Returns time in BCD format: CH=hours, CL=minutes, DH=seconds, DL=daylight savings flag.
    /// CF is cleared on success.
    /// </summary>
    public void ReadTimeFromRTC() {
        DateTime currentTime = DateTime.Now;

        int hours = currentTime.Hour;
        int minutes = currentTime.Minute;
        int seconds = currentTime.Second;

        State.CH = (byte)((hours / 10) << 4 | (hours % 10));
        State.CL = (byte)((minutes / 10) << 4 | (minutes % 10));
        State.DH = (byte)((seconds / 10) << 4 | (seconds % 10));
        State.DL = (byte)(TimeZoneInfo.Local.IsDaylightSavingTime(currentTime) ? 1 : 0);

        State.CarryFlag = false;

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1Ah, AH=02h - READ TIME FROM RTC: {Hours:X2}:{Minutes:X2}:{Seconds:X2}", 
                State.CH, State.CL, State.DH);
        }
    }

    /// <summary>
    /// INT 1Ah, AH=03h - Set RTC time.
    /// Time is provided in BCD format: CH=hours, CL=minutes, DH=seconds, DL=daylight savings flag (ignored).
    /// Note: This is a stub implementation that reports success but doesn't actually set the time.
    /// </summary>
    public void SetRTCTime() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1Ah, AH=03h - SET RTC TIME: {Hours:X2}:{Minutes:X2}:{Seconds:X2}", 
                State.CH, State.CL, State.DH);
        }

        // Stub: report success without actually changing the time
        State.CarryFlag = false;
    }

    /// <summary>
    /// INT 1Ah, AH=04h - Read date from RTC.
    /// Returns date in BCD format: CH=century, CL=year, DH=month, DL=day.
    /// CF is cleared on success.
    /// </summary>
    public void ReadDateFromRTC() {
        DateTime currentDate = DateTime.Now;

        int century = currentDate.Year / 100;
        int year = currentDate.Year % 100;
        int month = currentDate.Month;
        int day = currentDate.Day;

        State.CH = (byte)((century / 10) << 4 | (century % 10));
        State.CL = (byte)((year / 10) << 4 | (year % 10));
        State.DH = (byte)((month / 10) << 4 | (month % 10));
        State.DL = (byte)((day / 10) << 4 | (day % 10));

        State.CarryFlag = false;

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1Ah, AH=04h - READ DATE FROM RTC: {Century:X2}{Year:X2}-{Month:X2}-{Day:X2}", 
                State.CH, State.CL, State.DH, State.DL);
        }
    }

    /// <summary>
    /// INT 1Ah, AH=05h - Set RTC date.
    /// Date is provided in BCD format: CH=century, CL=year, DH=month, DL=day.
    /// Note: This is a stub implementation that reports success but doesn't actually set the date.
    /// </summary>
    public void SetRTCDate() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1Ah, AH=05h - SET RTC DATE: {Century:X2}{Year:X2}-{Month:X2}-{Day:X2}", 
                State.CH, State.CL, State.DH, State.DL);
        }

        // Stub: report success without actually changing the date
        State.CarryFlag = false;
    }

    /// <summary>
    /// INT 1Ah, AH=81h-85h - Tandy sound system functions.
    /// Not implemented. Logs a message and returns.
    /// </summary>
    public void TandySoundSystemUnhandled() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1Ah, AH={Function:X2}h - TANDY SOUND SYSTEM NOT IMPLEMENTED", State.AH);
        }
    }
}