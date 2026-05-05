namespace Spice86.Core.Emulator.InterruptHandlers.SystemClock;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Function;
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
        AddAction(0x02, () => ReadTimeFromRTC(true));
        AddAction(0x03, () => SetRTCTime(true));
        AddAction(0x04, () => ReadDateFromRTC(true));
        AddAction(0x05, () => SetRTCDate(true));
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
    /// <param name="calledFromVm">Whether this was invoked by the VM (carry flag must be propagated to interrupt stack).</param>
    private void ReadTimeFromRTC(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1A, AH=02h - Read Time from RTC");
        }

        DateTimeOffset now = _realTimeClock.Clock.CurrentDateTime;
        State.CH = BcdConverter.ToBcd((byte)now.Hour);
        State.CL = BcdConverter.ToBcd((byte)now.Minute);
        State.DH = BcdConverter.ToBcd((byte)now.Second);
        State.DL = 0; // Standard time (not daylight savings)
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// INT 1A, AH=03h - Set RTC Time.
    /// The requested time is silently ignored as modifying the host system time is not permitted.
    /// Returns CF=0 (success) to match real BIOS behavior, where writes to a read-only RTC succeed
    /// but have no effect, ensuring DOS programs that check CF do not treat this as an error.
    /// </summary>
    /// <param name="calledFromVm">Whether this was invoked by the VM (carry flag must be propagated to interrupt stack).</param>
    private void SetRTCTime(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1A, AH=03h - Set RTC Time (ignored, host time is read-only)");
        }
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// INT 1A, AH=04h - Read Date from RTC.
    /// Returns date in BCD format from the Real-Time Clock.
    /// </summary>
    /// <param name="calledFromVm">Whether this was invoked by the VM (carry flag must be propagated to interrupt stack).</param>
    private void ReadDateFromRTC(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1A, AH=04h - Read Date from RTC");
        }

        DateTimeOffset now = _realTimeClock.Clock.CurrentDateTime;
        State.CH = BcdConverter.ToBcd((byte)(now.Year / 100));
        State.CL = BcdConverter.ToBcd((byte)(now.Year % 100));
        State.DH = BcdConverter.ToBcd((byte)now.Month);
        State.DL = BcdConverter.ToBcd((byte)now.Day);
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// INT 1A, AH=05h - Set RTC Date.
    /// The requested date is silently ignored as modifying the host system date is not permitted.
    /// Returns CF=0 (success) to match real BIOS behavior, where writes to a read-only RTC succeed
    /// but have no effect, ensuring DOS programs that check CF do not treat this as an error.
    /// </summary>
    /// <param name="calledFromVm">Whether this was invoked by the VM (carry flag must be propagated to interrupt stack).</param>
    private void SetRTCDate(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 1A, AH=05h - Set RTC Date (ignored, host date is read-only)");
        }
        SetCarryFlag(false, calledFromVm);
    }
}