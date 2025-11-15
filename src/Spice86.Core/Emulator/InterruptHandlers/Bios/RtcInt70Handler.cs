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
/// <para>
/// This handler services the Real-Time Clock periodic and alarm interrupts.
/// The periodic interrupt fires at a configurable rate (typically 1024 Hz) and is used
/// to implement the BIOS WAIT function (INT 15h, AH=83h).
/// </para>
/// <para>
/// The handler:
/// - Decrements the wait counter for INT 15h, AH=83h
/// - Sets the user flag when the wait expires
/// - Disables the periodic interrupt when the wait completes
/// - Detects alarm interrupts (INT 4Ah callback not implemented)
/// </para>
/// <para>
/// Based on the IBM BIOS RTC_INT procedure which handles both periodic
/// and alarm interrupts from the CMOS timer.
/// </para>
/// </summary>
public sealed class RtcInt70Handler : InterruptHandler {
    private readonly DualPic _dualPic;
    private readonly BiosDataArea _biosDataArea;
    private readonly IOPortDispatcher _ioPortDispatcher;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus for accessing user flags.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="dualPic">The PIC for interrupt acknowledgment.</param>
    /// <param name="biosDataArea">The BIOS data area for wait flag and counter access.</param>
    /// <param name="ioPortDispatcher">The I/O port dispatcher for CMOS register access.</param>
    /// <param name="loggerService">The logger service.</param>
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
    /// Handles the RTC interrupt by processing periodic and alarm events.
    /// </summary>
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

    /// <summary>
    /// Handles the periodic interrupt by decrementing the wait counter.
    /// <para>
    /// The periodic interrupt fires at approximately 1024 Hz (976.56 μs per interrupt).
    /// DOSBox uses 997 μs as the decrement value for better accuracy.
    /// </para>
    /// </summary>
    private void HandlePeriodicInterrupt() {
        // Check if a wait is active
        if (_biosDataArea.RtcWaitFlag == 0) {
            return;
        }

        // Decrement the wait counter (DOSBox uses 997 microseconds per interrupt)
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

    /// <summary>
    /// Completes the wait operation by disabling the periodic interrupt
    /// and setting the user flag.
    /// </summary>
    private void CompleteWait() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("RTC wait completed");
        }

        // Clear the wait counter and flag
        _biosDataArea.UserWaitTimeout = 0;
        _biosDataArea.RtcWaitFlag = 0;

        // Set the user flag by ORing with 0x80 (DOSBox pattern)
        SegmentedAddress userFlagAddress = _biosDataArea.UserWaitCompleteFlag;
        if (userFlagAddress != SegmentedAddress.ZERO) {
            // Only set the flag if not a null pointer (0000:0000)
            byte currentValue = Memory.UInt8[userFlagAddress.Segment, userFlagAddress.Offset];
            Memory.UInt8[userFlagAddress.Segment, userFlagAddress.Offset] = (byte)(currentValue | 0x80);
            
            if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                LoggerService.Verbose("Set user wait flag at {Segment:X4}:{Offset:X4} to 0x{Value:X2}",
                    userFlagAddress.Segment, userFlagAddress.Offset, currentValue | 0x80);
            }
        }

        // Disable periodic interrupt (clear bit 6 of Status Register B)
        _ioPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterB);
        byte statusB = _ioPortDispatcher.ReadByte(CmosPorts.Data);
        _ioPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterB);
        _ioPortDispatcher.WriteByte(CmosPorts.Data, (byte)(statusB & ~0x40));
    }

    /// <summary>
    /// Handles the alarm interrupt by invoking INT 4Ah.
    /// <para>
    /// Programs can hook INT 4Ah to receive alarm callbacks.
    /// Note: This is a stub implementation that just acknowledges the alarm.
    /// The actual callback mechanism would require CPU instruction execution which
    /// is handled outside this interrupt handler's scope.
    /// </para>
    /// </summary>
    private void HandleAlarmInterrupt() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("RTC alarm interrupt detected");
        }

        // The BIOS code points CMOS to default register D before enabling interrupts
        // and calling INT 4Ah
        _ioPortDispatcher.WriteByte(CmosPorts.Address, CmosRegisterAddresses.StatusRegisterD);

        // Note: The actual INT 4Ah callback would be invoked by the BIOS assembly code.
        // Since we're implementing this in C#, we can't easily trigger the software interrupt
        // from within an interrupt handler context. Programs that need alarm support should
        // install their own INT 70h handler that calls INT 4Ah.
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("RTC alarm interrupt - INT 4Ah callback should be implemented by program if needed");
        }
    }

    /// <summary>
    /// Acknowledges the RTC interrupt by pointing to default register
    /// and sending EOI to both PICs.
    /// </summary>
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
