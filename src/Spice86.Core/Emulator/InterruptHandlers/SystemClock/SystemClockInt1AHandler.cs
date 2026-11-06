using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.InterruptHandlers.SystemClock;


using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// INT 1Ah - BIOS Time Services.
/// System clock counter and RTC date/time functions.
/// </summary>
public class SystemClockInt1AHandler : InterruptHandler {
    private readonly BiosDataArea _biosDataArea;
    private readonly RealTimeClock _realTimeClock;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public SystemClockInt1AHandler(IMemory memory, BiosDataArea biosDataArea,
        RealTimeClock realTimeClock, IFunctionHandlerProvider functionHandlerProvider,
        Stack stack, State state, ILogger loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _biosDataArea = biosDataArea;
        _realTimeClock = realTimeClock;
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x1A;


    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        Run(operation);
    }

    /// <summary>Returns the BIOS tick counter in CX:DX and the midnight flag in AL.</summary>
    public (byte al, ushort cx, ushort dx) ReadSystemClockCounter() {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("INT 1A, AH=00h - Get System Clock Counter");
        }

        uint ticks = _biosDataArea.TimerCounter;
        byte al = _biosDataArea.TimerRollover;
        _biosDataArea.TimerRollover = 0;
        return (al, (ushort)(ticks >> 16), (ushort)(ticks & 0xFFFF));
    }

    /// <summary>
    /// INT 1A, AH=00h - Get System Clock Counter.
    /// Returns the number of clock ticks since midnight.
    /// Clock ticks at 18.2 Hz (approximately 1,193,180 / 65,536 times per second).
    /// </summary>
    [InterruptService(0x00, Reads = CpuValues.Ah, Writes = CpuValues.Al | CpuValues.Cx | CpuValues.Dx, Memory = ServiceMemoryEffect.ReadWrite, ExecutionPath = ServiceExecutionPath.Direct)]
    public void GetSystemClockCounter() {
        (State.AL, State.CX, State.DX) = ReadSystemClockCounter();
    }

    /// <summary>Sets the BIOS tick counter to CX:DX.</summary>
    public void SetSystemClockCounter(ushort cx, ushort dx) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("INT 1A, AH=01h - Set System Clock Counter");
        }

        _biosDataArea.TimerCounter = ((uint)cx << 16) | dx;
        _biosDataArea.TimerRollover = 0;
    }

    /// <summary>
    /// INT 1A, AH=01h - Set System Clock Counter.
    /// Sets the system clock counter to the specified value.
    /// </summary>
    [InterruptService(0x01, Reads = CpuValues.Ah | CpuValues.Cx | CpuValues.Dx, Writes = CpuValues.None, Memory = ServiceMemoryEffect.Write, ExecutionPath = ServiceExecutionPath.Direct)]
    public void SetSystemClockCounter() {
        SetSystemClockCounter(State.CX, State.DX);
    }

    /// <summary>Returns the real-time clock time in CH, CL and DH (BCD); CF is clear.</summary>
    public (ushort cx, ushort dx, bool carryFlag) ReadTimeFromRTC() {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("INT 1A, AH=02h - Read Time from RTC");
        }

        DateTimeOffset now = _realTimeClock.Clock.CurrentDateTime;
        ushort cx = (ushort)((BcdConverter.ToBcd((byte)now.Hour) << 8)
            | BcdConverter.ToBcd((byte)now.Minute));
        // DL is 0: standard time (not daylight savings)
        ushort dx = (ushort)(BcdConverter.ToBcd((byte)now.Second) << 8);
        return (cx, dx, false);
    }

    /// <summary>
    /// INT 1A, AH=02h - Read Time from RTC.
    /// Returns time in BCD format from the Real-Time Clock.
    /// </summary>
    [InterruptService(0x02, Reads = CpuValues.Ah, Writes = CpuValues.Cx | CpuValues.Dx | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void ReadTimeFromRTC(bool calledFromVm) {
        (ushort cx, ushort dx, bool carryFlag) = ReadTimeFromRTC();
        State.CX = cx;
        State.DX = dx;
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Returns whether setting the RTC time failed: it always does, because
    /// modifying the host system time is not permitted.</summary>
    public bool SetRTCTime() {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("INT 1A, AH=03h - Set RTC Time (not permitted, returning error)");
        }
        return true;
    }

    /// <summary>
    /// INT 1A, AH=03h - Set RTC Time.
    /// Returns error as modifying the host system time is not permitted for security and consistency reasons.
    /// Programs should not rely on being able to set the system time in an emulated environment.
    /// </summary>
    [InterruptService(0x03, Reads = CpuValues.Ah, Writes = CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void SetRTCTime(bool calledFromVm) {
        SetCarryFlag(SetRTCTime(), calledFromVm);
    }

    /// <summary>Returns the real-time clock date in CH, CL, DH and DL (BCD); CF is clear.</summary>
    public (ushort cx, ushort dx, bool carryFlag) ReadDateFromRTC() {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("INT 1A, AH=04h - Read Date from RTC");
        }

        DateTimeOffset now = _realTimeClock.Clock.CurrentDateTime;
        ushort cx = (ushort)((BcdConverter.ToBcd((byte)(now.Year / 100)) << 8)
            | BcdConverter.ToBcd((byte)(now.Year % 100)));
        ushort dx = (ushort)((BcdConverter.ToBcd((byte)now.Month) << 8)
            | BcdConverter.ToBcd((byte)now.Day));
        return (cx, dx, false);
    }

    /// <summary>
    /// INT 1A, AH=04h - Read Date from RTC.
    /// Returns date in BCD format from the Real-Time Clock.
    /// </summary>
    [InterruptService(0x04, Reads = CpuValues.Ah, Writes = CpuValues.Cx | CpuValues.Dx | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void ReadDateFromRTC(bool calledFromVm) {
        (ushort cx, ushort dx, bool carryFlag) = ReadDateFromRTC();
        State.CX = cx;
        State.DX = dx;
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Returns whether setting the RTC date failed: it always does, because
    /// modifying the host system date is not permitted.</summary>
    public bool SetRTCDate() {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("INT 1A, AH=05h - Set RTC Date (not permitted, returning error)");
        }
        return true;
    }

    /// <summary>
    /// INT 1A, AH=05h - Set RTC Date.
    /// Returns error as modifying the host system date is not permitted for security and consistency reasons.
    /// Programs should not rely on being able to set the system date in an emulated environment.
    /// </summary>
    [InterruptService(0x05, Reads = CpuValues.Ah, Writes = CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void SetRTCDate(bool calledFromVm) {
        SetCarryFlag(SetRTCDate(), calledFromVm);
    }
}
