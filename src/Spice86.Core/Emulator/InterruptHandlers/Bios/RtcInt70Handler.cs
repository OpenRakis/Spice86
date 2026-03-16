namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// INT 70h - RTC Alarm/Periodic Interrupt Handler (IRQ 8).
/// Handles RTC periodic interrupts (typically 1024 Hz) for BIOS WAIT function (INT 15h, AH=83h).
/// Decrements wait counter, sets user flag on expiration, and detects alarm interrupts.
/// </summary>
public sealed class RtcInt70Handler : InterruptHandler {
    private readonly DualPic _dualPic;
    private readonly BiosDataArea _biosDataArea;
    private readonly IOPortDispatcher _ioPortDispatcher;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public RtcInt70Handler(IMemory memory, IFunctionHandlerProvider functionHandlerProvider,
        Stack stack, State state, DualPic dualPic, BiosDataArea biosDataArea,
        IOPortDispatcher ioPortDispatcher, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _dualPic = dualPic;
        _biosDataArea = biosDataArea;
        _ioPortDispatcher = ioPortDispatcher;
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x70;

    /// <inheritdoc />
    public override void Run() {
        HandleRtcInterrupt();
    }


    /// <summary>
    /// Handles RTC interrupt by reading Status Register C to determine interrupt type (periodic or alarm),
    /// then dispatches to appropriate handler. Acknowledges interrupt to PIC when done.
    /// </summary>
    public void HandleRtcInterrupt() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 70h - RTC Alarm/Periodic Interrupt Handler");
        }

        _ioPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterC);
        byte statusC = _ioPortDispatcher.ReadByte(CmosPorts.Data);

        if ((statusC & 0x60) == 0) {
            AcknowledgeInterrupt();
            return;
        }

        _ioPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterB);
        byte statusB = _ioPortDispatcher.ReadByte(CmosPorts.Data);

        byte activeInterrupts = (byte)(statusC & statusB);

        if ((activeInterrupts & 0x40) != 0) {
            HandlePeriodicInterrupt();
        }

        if ((activeInterrupts & 0x20) != 0) {
            HandleAlarmInterrupt();
        }

        AcknowledgeInterrupt();
    }

    /// <summary>
    /// Handles RTC periodic interrupt for BIOS WAIT function (INT 15h, AH=83h).
    /// Decrements wait timeout counter by interrupt period (~976μs). When timeout expires, calls CompleteWait().
    /// </summary>
    public void HandlePeriodicInterrupt() {
        if (_biosDataArea.RtcWaitFlag == 0) {
            return;
        }

        // RTC periodic interrupt is typically 1024 Hz, so each period is ~976.5625 microseconds.
        // Use explicit calculation for clarity and accuracy.
        const uint InterruptIntervalMicroseconds = 1_000_000 / 1024;
        uint count = _biosDataArea.UserWaitTimeout;

        if (count > InterruptIntervalMicroseconds) {
            _biosDataArea.UserWaitTimeout = count - InterruptIntervalMicroseconds;
        } else {
            CompleteWait();
        }
    }

    /// <summary>
    /// Completes BIOS WAIT operation by clearing wait flag and setting bit 7 of user flag byte at ES:BX.
    /// Does NOT clear PIE bit - periodic interrupts remain enabled. Only explicit cancel (INT 15h, AH=83h, AL=01h) clears PIE.
    /// </summary>
    public void CompleteWait() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("RTC wait completed");
        }

        _biosDataArea.UserWaitTimeout = 0;
        _biosDataArea.RtcWaitFlag = 0;

        SegmentedAddress userFlagAddress = _biosDataArea.UserWaitCompleteFlag;
        if (userFlagAddress != SegmentedAddress.ZERO) {
            byte currentValue = Memory.UInt8[userFlagAddress.Segment, userFlagAddress.Offset];
            Memory.UInt8[userFlagAddress.Segment, userFlagAddress.Offset] = (byte)(currentValue | 0x80);

            if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                LoggerService.Verbose("Set user wait flag at {Segment:X4}:{Offset:X4} to 0x{Value:X2}",
                    userFlagAddress.Segment, userFlagAddress.Offset, currentValue | 0x80);
            }
        }
    }

    /// <summary>
    /// Handles RTC alarm interrupt. Logs detection and notes that INT 4Ah callback should be implemented by program if needed.
    /// </summary>
    public void HandleAlarmInterrupt() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("RTC alarm interrupt detected");
        }

        _ioPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterD);
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("RTC alarm interrupt - INT 4Ah callback should be implemented by program if needed");
        }
    }

    /// <summary>
    /// Acknowledges RTC interrupt to PIC by reading Status Register D and sending EOI to IRQ 8.
    /// </summary>
    public void AcknowledgeInterrupt() {
        _ioPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterD);

        _dualPic.AcknowledgeInterrupt(8);

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("RTC interrupt acknowledged");
        }
    }
}