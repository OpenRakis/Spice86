namespace Spice86.Core.Emulator.InterruptHandlers.SystemClock;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implementation of INT1A.
/// </summary>
public class SystemClockInt1AHandler : InterruptHandler {
    private readonly TimerInt8Handler _timerHandler;
    private readonly Clock _clock; //it's only a fake RTC, but good enough for the time being

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="timerHandler">The timer interrupt handler.</param>
    /// <param name="clock"></param>
    public SystemClockInt1AHandler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider, Stack stack,
        State state, ILoggerService loggerService, TimerInt8Handler timerHandler, Clock clock)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _timerHandler = timerHandler;
        _clock = clock;
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

    public void ReadTimeFromRTC() {
        DateTime currentTime = _clock.GetVirtualDateTime();

        int hours = currentTime.Hour;
        int minutes = currentTime.Minute;
        int seconds = currentTime.Second;
        
        State.CH = (byte)((hours / 10) << 4 | (hours % 10));
        State.CL = (byte)((minutes / 10) << 4 | (minutes % 10));
        State.DH = (byte)((seconds / 10) << 4 | (seconds % 10));
        State.DL = (byte)(TimeZoneInfo.Local.IsDaylightSavingTime(currentTime) ? 1 : 0);
    
        // Clear carry flag to indicate success
        State.CarryFlag = false;
    }
    
    public void SetRTCTime() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SET RTC TIME");
        }
        
        int hoursBcd = State.CH;
        int minutesBcd = State.CL;
        int secondsBcd = State.DH;
    
        int hours = ((hoursBcd >> 4) * 10) + (hoursBcd & 0x0F);
        int minutes = ((minutesBcd >> 4) * 10) + (minutesBcd & 0x0F);
        int seconds = ((secondsBcd >> 4) * 10) + (secondsBcd & 0x0F);
        
        State.CarryFlag = !_clock.SetTime((byte)hours, (byte)minutes, (byte)seconds, 0);
    }
    
    public void ReadDateFromRTC() {
        (int y, int month, int day) = _clock.GetVirtualDateTime();
        
        int century = y / 100;
        int year = y % 100;

        State.CH = (byte)((century / 10) << 4 | (century % 10));
        State.CL = (byte)((year / 10) << 4 | (year % 10));
        State.DH = (byte)((month / 10) << 4 | (month % 10));
        State.DL = (byte)((day / 10) << 4 | (day % 10));
        
        // Clear carry flag to indicate success
        State.CarryFlag = false;
    }
    
    public void SetRTCDate() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SET RTC DATE");
        }
        
        int centuryBcd = State.CH;
        int yearBcd = State.CL;
        int monthBcd = State.DH;
        int dayBcd = State.DL;
        
        int century = ((centuryBcd >> 4) * 10) + (centuryBcd & 0x0F);
        int year = ((yearBcd >> 4) * 10) + (yearBcd & 0x0F);
        int month = ((monthBcd >> 4) * 10) + (monthBcd & 0x0F);
        int day = ((dayBcd >> 4) * 10) + (dayBcd & 0x0F);
        
        int fullYear = (century * 100) + year;

        State.CarryFlag = !_clock.SetDate((byte)day, (byte)month, (byte)fullYear);
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