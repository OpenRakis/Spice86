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


    private void HandleRtcInterrupt() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 70h - RTC Alarm/Periodic Interrupt Handler");
        }

        // Read Status Register C (0x0C) to check interrupt source and clear flags
        // Reading this register clears the interrupt flags
        _ioPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterC);
        byte statusC = _ioPortDispatcher.ReadByte(CmosPorts.Data);

        // Check if this is a valid RTC interrupt (bit 6 = periodic, bit 5 = alarm)
        if ((statusC & 0x60) == 0) {
            // Not a valid RTC interrupt, just acknowledge and exit
            AcknowledgeInterrupt();
            return;
        }

        // Read Status Register B (0x0B) to check which interrupts are enabled
        _ioPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterB);
        byte statusB = _ioPortDispatcher.ReadByte(CmosPorts.Data);

        // Only process interrupts that are both flagged and enabled
        byte activeInterrupts = (byte)(statusC & statusB);

        // Handle periodic interrupt (bit 6)
        if ((activeInterrupts & 0x40) != 0) {
            HandlePeriodicInterrupt();
        }

        // Handle alarm interrupt (bit 5)
        if ((activeInterrupts & 0x20) != 0) {
            HandleAlarmInterrupt();
        }

        // Acknowledge the interrupt and exit
        AcknowledgeInterrupt();
    }


    private void HandlePeriodicInterrupt() {
        // Check if a wait is active
        if (_biosDataArea.RtcWaitFlag == 0) {
            return;
        }

        const uint InterruptIntervalMicroseconds = 997;
        uint count = _biosDataArea.UserWaitTimeout;

        if (count > InterruptIntervalMicroseconds) {
            // Still waiting - decrement the counter
            _biosDataArea.UserWaitTimeout = count - InterruptIntervalMicroseconds;
        } else {
            // Wait has expired
            CompleteWait();
        }
    }


    private void CompleteWait() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("RTC wait completed");
        }

        // Clear the wait counter and flag
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

        _ioPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterB);
        byte statusB = _ioPortDispatcher.ReadByte(CmosPorts.Data);
        _ioPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterB);
        _ioPortDispatcher.WriteByte(CmosPorts.Data, (byte)(statusB & ~0x40));
    }


    private void HandleAlarmInterrupt() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("RTC alarm interrupt detected");
        }

        _ioPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterD);
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("RTC alarm interrupt - INT 4Ah callback should be implemented by program if needed");
        }
    }


    private void AcknowledgeInterrupt() {
        // Point to default read-only register D and enable NMI
        _ioPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterD);

        // Send EOI to both PICs (IRQ 8 is on secondary PIC)
        _dualPic.AcknowledgeInterrupt(8);

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("RTC interrupt acknowledged");
        }
    }
}