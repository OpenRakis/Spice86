namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Serilog.Events;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

/// <summary>
///     Provides the shared controller logic for the primary and secondary Intel 8259 PIC channels.
/// </summary>
/// <remarks>
///     Maintains register mirrors, applies deterministic mask and service updates, and defers CPU signalling to derived
///     classes.
/// </remarks>
/// <param name="logger">Logging facility used for diagnostic messages.</param>
internal abstract class Intel8259Channel(ILoggerService logger) {
    /// <summary>
    ///     The IRQ index that presently has priority (0-7) or 8 when idle.
    /// </summary>
    public byte ActiveIrqLine;

    /// <summary>
    ///     Zero-based cursor indicating which initialization command word (0-3) is being processed.
    /// </summary>
    public nuint CurrentIcwIndex;

    /// <summary>
    ///     Bitmask of IRQs currently in service.
    /// </summary>
    public byte InServiceRegister;

    /// <summary>
    ///     Cached inversion of <see cref="InServiceRegister" /> used to speed up bit tests.
    /// </summary>
    public byte InServiceRegisterInverted;

    /// <summary>
    ///     Software mask bits from OCW1 where a set bit suppresses the corresponding IRQ.
    /// </summary>
    public byte InterruptMaskRegister;

    /// <summary>
    ///     Cached inversion of <see cref="InterruptMaskRegister" /> avoiding repeated recomputation.
    /// </summary>
    public byte InterruptMaskRegisterInverted;

    /// <summary>
    ///     Latched requests that have arrived but are not yet being serviced; each bit maps to an IRQ line.
    /// </summary>
    public byte InterruptRequestRegister;

    /// <summary>
    ///     Base interrupt vector from ICW2 added to IRQ numbers when signalling the CPU.
    /// </summary>
    public byte InterruptVectorBase;

    /// <summary>
    ///     Indicates whether auto end-of-interrupt is enabled.
    /// </summary>
    public bool IsAutoEndOfInterruptEnabled;

    /// <summary>
    ///     When set, the following command port reads return the in-service register instead of the request register.
    /// </summary>
    public bool IsIssrRequested;

    /// <summary>
    ///     Indicates whether the controller is configured for a single (non-cascaded) operation.
    /// </summary>
    public bool IsSingleModeConfigured;

    /// <summary>
    ///     Indicates whether a special mask mode is enabled.
    /// </summary>
    public bool IsSpecialMaskModeEnabled;

    /// <summary>
    ///     Remaining number of initialization words expected after ICW1.
    /// </summary>
    public nuint RemainingInitializationWords;

    /// <summary>
    ///     Indicates whether rotate-on-auto-EOI was requested.
    /// </summary>
    public bool ShouldRotateOnAutoEoi;

    private byte _lowestPriorityIrq;

    private ILoggerService Logger => logger;

    /// <summary>
    ///     Signals the CPU or upstream controller that an IRQ is pending.
    /// </summary>
    protected abstract void Activate();

    /// <summary>
    ///     Clears the signal indicating that no IRQ is currently pending.
    /// </summary>
    protected abstract void Deactivate();

    /// <summary>
    ///     Computes the current highest-priority IRQ index based on the configured lowest priority.
    /// </summary>
    private byte HighestPriorityIrq => (byte)((_lowestPriorityIrq + 1) & 0x07);

    /// <summary>
    ///     Converts an IRQ index into a relative priority position where zero represents the highest priority.
    /// </summary>
    /// <param name="irq">IRQ line to evaluate.</param>
    /// <returns>Relative priority slot in the range 0–7.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte GetPriorityPosition(byte irq) {
        return (byte)((irq - HighestPriorityIrq) & 0x07);
    }

    /// <summary>
    ///     Determines whether the candidate IRQ outranks the currently active IRQ based on the rotation state.
    /// </summary>
    /// <param name="candidate">IRQ line to test.</param>
    /// <param name="current">Currently active IRQ line, or 8 when idle.</param>
    /// <returns><see langword="true" /> when the candidate has a higher priority; otherwise <see langword="false" />.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasHigherPriority(byte candidate, byte current) {
        if (current == 8) {
            return true;
        }

        return GetPriorityPosition(candidate) < GetPriorityPosition(current);
    }

    /// <summary>
    ///     Resolves the highest-priority IRQ bit from a pending mask, respecting the rotation baseline.
    /// </summary>
    /// <param name="pendingMask">Bitmask of eligible IRQs.</param>
    /// <returns>The IRQ index or 8 when no candidate is found.</returns>
    private byte GetHighestPriorityIrq(byte pendingMask) {
        if (pendingMask == 0) {
            return 8;
        }

        byte irq = HighestPriorityIrq;
        for (int step = 0; step < 8; step++) {
            byte mask = (byte)(1 << irq);
            if ((pendingMask & mask) != 0) {
                return irq;
            }

            irq = (byte)((irq + 1) & 0x07);
        }

        return 8;
    }

    /// <summary>
    ///     Finds the next IRQ that should be serviced, or 8 when none qualify.
    /// </summary>
    internal byte GetNextPendingIrq() {
        byte pending = (byte)(InterruptRequestRegister & InterruptMaskRegisterInverted & InServiceRegisterInverted);
        if (pending == 0) {
            return 8;
        }

        byte candidate = GetHighestPriorityIrq(pending);
        if (candidate == 8) {
            return 8;
        }

        if (IsSpecialMaskModeEnabled || HasHigherPriority(candidate, ActiveIrqLine)) {
            return candidate;
        }

        return 8;
    }

    /// <summary>
    ///     Updates the lowest-priority slot used to determine subsequent rotation order.
    /// </summary>
    /// <param name="value">IRQ index to treat as lowest priority.</param>
    internal void SetLowestPriorityIrq(byte value) {
        _lowestPriorityIrq = (byte)(value & 0x07);
        if (Logger.IsEnabled(LogEventLevel.Debug)) {
            Logger.Debug("PIC: Lowest priority set to IRQ {Irq}", _lowestPriorityIrq);
        }
    }

    /// <summary>
    ///     Updates the interrupt mask register and re-evaluates IRQs when newly masked bits intersect pending requests.
    /// </summary>
    /// <param name="value">Mask value written by the controller.</param>
    public void SetInterruptMaskRegister(byte value) {
        byte change = (byte)(InterruptMaskRegister ^ value); // Bits that have changed become 1.
        InterruptMaskRegister = value;
        InterruptMaskRegisterInverted = (byte)~value;
        Logger.Debug("PIC: IMR updated to {Imr:X2} (changed {Changed:X2})", value, change);

        // Test if changed bits are set in irr and are not being served at the moment.
        // Those bits have an impact on whether the cpu emulation should be paused or not.
        if ((InterruptRequestRegister & change & InServiceRegisterInverted) != 0) {
            CheckForIrq();
        }
    }

    /// <summary>
    ///     Re-evaluates pending IRQs after an end-of-interrupt is issued.
    /// </summary>
    public void CheckAfterEoi() {
        // Update the active IRQ as an EOI is likely to change that.
        UpdateActiveIrqLine();
        if ((InterruptRequestRegister & InterruptMaskRegisterInverted & InServiceRegisterInverted) != 0) {
            CheckForIrq();
        }
    }

    /// <summary>
    ///     Recomputes the highest-priority in-service IRQ.
    /// </summary>
    private void UpdateActiveIrqLine() {
        if (InServiceRegister == 0) {
            ActiveIrqLine = 8;
            return;
        }

        for (byte bitIndex = 0, bitMask = 1; bitIndex < 8; bitIndex++, bitMask <<= 1) {
            if ((InServiceRegister & bitMask) == 0) {
                continue;
            }

            ActiveIrqLine = bitIndex;
            return;
        }
    }

    /// <summary>
    ///     Validates that an IRQ number is within the supported 0-7 range.
    /// </summary>
    /// <param name="value">IRQ value to validate.</param>
    /// <param name="caller">The name of the calling member populated automatically.</param>
    /// <returns><see langword="true" /> when the value is valid; otherwise <see langword="false" />.</returns>
    private bool ValidateIrq(byte value, [CallerMemberName] string? caller = null) {
        if (value <= 7) {
            return true;
        }

        Logger.Error("PIC: Invalid IRQ {Irq} supplied to {Caller}", value, caller ?? "unknown");
        return false;
    }

    /// <summary>
    ///     Checks for pending, unmasked, non-serviced IRQs and signals activation when found.
    /// </summary>
    public void CheckForIrq() {
        byte pending = (byte)(InterruptRequestRegister & InterruptMaskRegisterInverted & InServiceRegisterInverted);
        if (pending == 0) {
            Deactivate();
            return;
        }

        if (IsSpecialMaskModeEnabled) {
            Activate();
            return;
        }

        byte candidate = GetHighestPriorityIrq(pending);
        if (candidate == 8) {
            Deactivate();
            return;
        }

        if (HasHigherPriority(candidate, ActiveIrqLine)) {
            Activate();
        } else {
            Deactivate();
        }
    }

    /// <summary>
    ///     Latches an IRQ request and, if not masked or already in service, signals activation.
    /// </summary>
    /// <param name="value">IRQ line number.</param>
    public void RaiseIrq(byte value) {
        if (!ValidateIrq(value)) {
            return;
        }

        byte bit = (byte)(1 << value);
        if ((InterruptRequestRegister & bit) != 0) {
            return;
        }

        // Value changed (as it is currently not active).
        InterruptRequestRegister |= bit;
        Logger.Debug("PIC: IRQ {Irq} latched (irr={Irr:X2}, imr={Imr:X2})", value, InterruptRequestRegister,
            InterruptMaskRegister);
        if ((bit & InterruptMaskRegisterInverted & InServiceRegisterInverted) == 0) {
            Logger.Verbose("PIC: IRQ {Irq} masked or already in service (imr={Imr:X2}, isr={Isr:X2})",
                value,
                InterruptMaskRegister,
                InServiceRegister);
            return;
        }

        // Not masked and not in service.
        if (IsSpecialMaskModeEnabled || HasHigherPriority(value, ActiveIrqLine)) {
            Activate();
        } else {
            Logger.Verbose("PIC: IRQ {Irq} queued but lower priority than active IRQ {Active}", value, ActiveIrqLine);
        }
    }

    /// <summary>
    ///     Clears a latched IRQ request and re-evaluates signaling if needed.
    /// </summary>
    /// <param name="value">IRQ line number.</param>
    public void LowerIrq(byte value) {
        if (!ValidateIrq(value)) {
            return;
        }

        byte bit = (byte)(1 << value);
        if ((InterruptRequestRegister & bit) == 0) {
            Logger.Debug("PIC: Attempted to lower IRQ {Irq} that was not latched", value);
            return;
        }

        // Value will change (as it is currently active).
        InterruptRequestRegister &= (byte)~bit;
        Logger.Debug("PIC: IRQ {Irq} cleared (irr={Irr:X2})", value, InterruptRequestRegister);
        CheckForIrq();
        if (Logger.IsEnabled(LogEventLevel.Verbose)) {
            Logger.Verbose("PIC: IRQ {Irq} cleared; pending mask={Pending:X2}", value,
                InterruptRequestRegister & InterruptMaskRegisterInverted & InServiceRegisterInverted);
        }
    }

    /// <summary>
    ///     Transfers a pending IRQ into service without directly invoking the CPU.
    /// </summary>
    /// <param name="value">IRQ line number.</param>
    public void StartIrq(byte value) {
        if (!ValidateIrq(value)) {
            return;
        }

        InterruptRequestRegister &= (byte)~(1 << value);
        if (!IsAutoEndOfInterruptEnabled) {
            ActiveIrqLine = value;
            InServiceRegister |= (byte)(1 << value);
            InServiceRegisterInverted = (byte)~InServiceRegister;
            Logger.Debug("PIC: IRQ {Irq} started (autoEOI={AutoEoi})", value, IsAutoEndOfInterruptEnabled);
        } else if (ShouldRotateOnAutoEoi) {
            SetLowestPriorityIrq(value);
            Logger.Debug("PIC: Auto-EOI rotation applied for IRQ {Irq}", value);
        }
    }

    /// <summary>
    ///     Resets controller registers to their defaults prior to initialization.
    /// </summary>
    public virtual void Initialize() {
        IsAutoEndOfInterruptEnabled = false; // auto_eoi defaults to false
        ShouldRotateOnAutoEoi = false; // rotate_on_auto_eoi cleared on reset
        IsIssrRequested = false; // request_issr cleared during bootstrap
        IsSpecialMaskModeEnabled = false; // special mask mode disabled initially
        IsSingleModeConfigured = false; // single flag false until ICW1 says otherwise
        CurrentIcwIndex = 0u; // icw_index reset awaiting ICW1 sequence
        RemainingInitializationWords = 0u; // icw_words zeroed before ICW1
        InterruptRequestRegister = 0; // irr cleared; no pending IRQs
        InServiceRegister = 0; // isr cleared; nothing in service
        InterruptMaskRegisterInverted = 0; // imrr reset to 0 to mirror imr=0xff
        InServiceRegisterInverted = 0xff; // isrr initialised to 0xff
        InterruptMaskRegister = 0xff; // imr set to mask all IRQs until programmed
        ActiveIrqLine = 8; // active_irq set to 8 sentinel (no IRQ active)
        _lowestPriorityIrq = 7; // default lowest priority points to IRQ7
        Logger.Debug("PIC: Controller state reset to defaults.");
    }
}

/// <summary>
///     Concrete controller that signals the CPU when the primary PIC has pending work.
/// </summary>
/// <param name="logger">Logging facility for diagnostic output.</param>
/// <param name="cpuState">CPU state used to adjust cycle counts when IRQs trigger.</param>
/// <param name="setIrqCheck">Delegate that updates the global IRQ pending flag.</param>
internal sealed class PrimaryPicChannel(ILoggerService logger, PicPitCpuState cpuState, Action<bool> setIrqCheck)
    : Intel8259Channel(logger) {
    /// <inheritdoc />
    protected override void Activate() {
        setIrqCheck(true);
        // Cycles 0: take care of the port I/O adjustments added in the raise_irq base caller.
        cpuState.CyclesLeft += cpuState.Cycles;
        cpuState.Cycles = 0;
        // Maybe when coming from an EOI, give a tiny delay for the CPU to pick it up (see ActivateIrq).
    }

    /// <inheritdoc />
    protected override void Deactivate() {
        setIrqCheck(false);
    }

    /// <inheritdoc />
    public override void Initialize() {
        base.Initialize();
        InterruptVectorBase = 0x08;
    }
}

/// <summary>
///     Concrete controller that relays secondary PIC activity through the cascade line.
/// </summary>
/// <param name="logger">Logging facility for diagnostic output.</param>
/// <param name="cascadeRaise">Delegate invoked when the secondary PIC asserts the cascade line.</param>
/// <param name="cascadeLower">Delegate invoked when the secondary PIC deasserts the cascade line.</param>
internal sealed class SecondaryPicChannel(ILoggerService logger, Action cascadeRaise, Action cascadeLower)
    : Intel8259Channel(logger) {
    /// <inheritdoc />
    protected override void Activate() {
        cascadeRaise();
    }

    /// <inheritdoc />
    protected override void Deactivate() {
        cascadeLower();
    }

    /// <inheritdoc />
    public override void Initialize() {
        base.Initialize();
        InterruptVectorBase = 0x70;
    }
}