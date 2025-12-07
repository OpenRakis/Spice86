namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

// PIC Controllers
// ~~~~~~~~~~~~~~~
// The sources here identify the two Programmable Interrupt Controllers
// (PICs) as primary and secondary: the prior services IRQs 0 to 7
// while the latter services IRQs 8 to 15.
//
// In addition to describing the IRQ range for each PIC, the primary and
// secondary terminology also refers to the fact that the CPU is notified by the
// primary PIC, while the secondary PIC signals the primary via IRQ 2.
//
// It should be noted that some historical documents described the two PICs in a
// "master-slave" relationship, which is misleading given that fact that the
// primary has no control over the secondary.

/// <summary>
///     Coordinates the paired PIC controllers and handles bus registration.
/// </summary>
public sealed partial class DualPic : DefaultIOPortHandler {
    /// <summary>
    ///     Identifies which controller to observe or configure.
    /// </summary>
    /// <remarks>
    ///     The primary controller handles IRQ lines 0 through 7, while the secondary cascades through IRQ 2 and serves
    ///     lines 8 through 15.
    /// </remarks>
    public enum PicController {
        /// <summary>
        ///     PIC servicing IRQ lines 0 through 7.
        /// </summary>
        Primary,

        /// <summary>
        ///     PIC servicing IRQ lines 8 through 15.
        /// </summary>
        Secondary
    }

    private const byte MaxIrq = 15;
    private const byte NoPendingIrq = 8;

    private const ushort PrimaryPicCommandPort = 0x20;
    private const ushort PrimaryPicDataPort = 0x21;
    private const ushort SecondaryPicCommandPort = 0xa0;
    private const ushort SecondaryPicDataPort = 0xa1;

    private const byte SpecificEoiBase = (byte)(Ocw2Flags.EndOfInterrupt | Ocw2Flags.Specific);

    private readonly IOPortDispatcher _ioPortDispatcher;
    private readonly Intel8259Pic _primaryPic;
    private readonly Intel8259Pic _secondaryPic;

    /// <summary>
    ///     Initializes controller state and registers I/O handlers.
    /// </summary>
    /// <param name="ioPortDispatcher">I/O dispatcher used to register port handlers.</param>
    /// <param name="state">CPU state.</param>
    /// <param name="loggerService">Logger used for diagnostic messages.</param>
    /// <param name="failOnUnhandledPort">Whether to throw on unhandled port access.</param>
    public DualPic(IOPortDispatcher ioPortDispatcher, State state, ILoggerService loggerService, bool failOnUnhandledPort)
        : base(state, failOnUnhandledPort, loggerService) {
        _ioPortDispatcher = ioPortDispatcher;
        _primaryPic = new PrimaryPic(loggerService, SetIrqCheck);
        _secondaryPic = new SecondaryPic(loggerService, CascadeRaiseFromSecondary, CascadeLowerFromSecondary);

        InitializeControllers();
        InstallHandlers();
    }

    /// <summary>
    ///     Gets a value indicating whether any pending IRQ requires CPU attention.
    /// </summary>
    public bool IrqCheck { get; private set; }

    /// <summary>
    ///     Raised when the mask state of an IRQ changes.
    /// </summary>
    /// <remarks>
    ///     The first argument contains the IRQ number and the boolean argument is <see langword="true" /> when the IRQ becomes
    ///     masked.
    /// </remarks>
    public event Action<byte, bool>? IrqMaskChanged;

    /// <summary>
    ///     Installs all I/O handlers required to expose the PIC command and data ports.
    /// </summary>
    private void InstallHandlers() {
        _ioPortDispatcher.AddIOPortHandler(PrimaryPicCommandPort, this);
        _ioPortDispatcher.AddIOPortHandler(PrimaryPicDataPort, this);
        _ioPortDispatcher.AddIOPortHandler(SecondaryPicCommandPort, this);
        _ioPortDispatcher.AddIOPortHandler(SecondaryPicDataPort, this);
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        return port switch {
            PrimaryPicCommandPort or SecondaryPicCommandPort => (byte)ReadCommand(port),
            PrimaryPicDataPort or SecondaryPicDataPort => (byte)ReadData(port),
            _ => base.ReadByte(port)
        };
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        switch (port) {
            case PrimaryPicCommandPort:
            case SecondaryPicCommandPort:
                WriteCommand(port, value);
                break;
            case PrimaryPicDataPort:
            case SecondaryPicDataPort:
                WriteData(port, value);
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }

    private void CascadeRaiseFromSecondary() {
        // The secondary PIC signals the primary over IRQ 2; raising it propagates the cascade line.
        _primaryPic.RaiseIrq(2);
    }

    private void CascadeLowerFromSecondary() {
        // Lowering the same cascade line releases the request on the primary controller.
        _primaryPic.LowerIrq(2);
    }

    /// <summary>
    ///     Updates the cached IRQ check flag used by the CPU loop.
    /// </summary>
    /// <param name="value">Value to assign.</param>
    private void SetIrqCheck(bool value) {
        IrqCheck = value;
    }

    /// <summary>
    ///     Resets controller registers.
    /// </summary>
    public void Reset() {
        _loggerService.Debug("PIC: Reset invoked; controllers will be reinitialized");
        InitializeControllers();
    }

    /// <summary>
    ///     Maintains compatibility with legacy callers that asserted an IRQ via <c>ProcessInterruptRequest</c>.
    /// </summary>
    public void ProcessInterruptRequest(byte irq) {
        ActivateIrq(irq);
    }

    /// <summary>
    ///     Maintains compatibility with legacy callers that acknowledged an IRQ via <c>AcknowledgeInterrupt</c>.
    /// </summary>
    /// <param name="irq">IRQ number to acknowledge.</param>
    public void AcknowledgeInterrupt(byte irq) {
        switch (irq) {
            case > MaxIrq:
                _loggerService.Error("PIC: Acknowledge requested for out-of-range IRQ {Irq}", irq);
                return;
            case < 8:
                WriteCommand(PrimaryPicCommandPort, (uint)(SpecificEoiBase | irq));
                break;
            default: {
                byte secondaryIrq = (byte)(irq - 8);
                WriteCommand(SecondaryPicCommandPort, (uint)(SpecificEoiBase | secondaryIrq));
                WriteCommand(PrimaryPicCommandPort, SpecificEoiBase | 2);
                break;
            }
        }
    }

    /// <summary>
    ///     Computes the next interrupt vector if one is pending.
    /// </summary>
    /// <returns>The vector number, or <see langword="null" /> if none is pending.</returns>
    public byte? ComputeVectorNumber() {
        if (!IrqCheck) {
            return null;
        }

        byte nextIrq = _primaryPic.GetNextPendingIrq();
        byte? vectorNumber = null;
        switch (nextIrq) {
            case NoPendingIrq:
                IrqCheck = false;
                return null;
            case 2:
                vectorNumber = SecondaryStartIrq();
                break;
            default:
                vectorNumber = PrimaryStartIrq(nextIrq);
                break;
        }

        // Disable check variable.
        IrqCheck = false;
        return vectorNumber;
    }

    /// <summary>
    ///     Resets both PIC controllers and applies the default mask/unmask configuration.
    /// </summary>
    private void InitializeControllers() {
        IrqCheck = false;

        _primaryPic.Initialize();
        _secondaryPic.Initialize();

        SetIrqMask(0, false); // Enable system timer IRQ 0.
        SetIrqMask(1, false); // Enable keyboard controller IRQ 1.
        SetIrqMask(2, false); // Route the cascade line so the secondary can signal the primary.
        SetIrqMask(8, false); // Enable the RTC source on the secondary PIC (IRQ 8).

        SetIrqMask(9, false); // AT-era systems expose IRQ 9 in addition to the cascade line.
        DeactivateIrq(9); // Clear any stale latch on IRQ 9 to mirror the bootstrap sequence.
    }
    
    /// <summary>
    ///     Captures a snapshot of controller-facing registers for inspection.
    /// </summary>
    /// <param name="controller">Controller to query.</param>
    /// <returns>Structure describing the register state.</returns>
    public PicSnapshot GetPicSnapshot(PicController controller) {
        Intel8259Pic pic = controller == PicController.Primary ? _primaryPic : _secondaryPic;
        return new PicSnapshot(pic.InterruptRequestRegister,
            pic.InterruptMaskRegister,
            pic.InterruptMaskRegisterInverted,
            pic.InServiceRegister,
            pic.InServiceRegisterInverted,
            pic.ActiveIrqLine,
            pic.IsSpecialMaskModeEnabled,
            pic.IsAutoEndOfInterruptEnabled,
            pic.ShouldRotateOnAutoEoi,
            pic.IsSingleModeConfigured,
            pic.InterruptVectorBase);
    }

    /// <summary>
    ///     Services reads from a PIC command port, returning either the IRR or ISR depending on OCW3.
    /// </summary>
    private uint ReadCommand(ushort port) {
        Intel8259Pic pic = GetCommandPicByPort(port);
        return pic.IsIssrRequested ? pic.InServiceRegister : pic.InterruptRequestRegister;
    }

    /// <summary>
    ///     Services reads from a PIC data port, returning the current mask register.
    /// </summary>
    private uint ReadData(ushort port) {
        return GetDataPicByPort(port).InterruptMaskRegister;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Intel8259Pic GetCommandPicByPort(ushort port) {
        switch (port) {
            case PrimaryPicCommandPort:
                return _primaryPic;
            case SecondaryPicCommandPort:
                return _secondaryPic;
            default:
                _loggerService.Error("PIC: Command port {Port:X} is not recognized; defaulting to primary controller", port);
                return _primaryPic;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Intel8259Pic GetDataPicByPort(ushort port) {
        switch (port) {
            case PrimaryPicDataPort:
                return _primaryPic;
            case SecondaryPicDataPort:
                return _secondaryPic;
            default:
                _loggerService.Error("PIC: Data port {Port:X} is not recognized; defaulting to primary controller", port);
                return _primaryPic;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Intel8259Pic GetPicByIrq(uint irq) {
        return irq > 7 ? _secondaryPic : _primaryPic;
    }

    /// <summary>
    ///     Handles command port writes, decoding ICW/OCW sequences and maintaining controller state.
    /// </summary>
    private void WriteCommand(ushort port, uint value) {
        byte rawValue = (byte)value;
        Intel8259Pic pic = GetCommandPicByPort(port);

        if (ProcessInitializationCommand(rawValue, pic)) {
            return;
        }

        if (ProcessOperationalControlWord3(port, rawValue, pic)) {
            return;
        }

        ProcessOperationalControlWord2(rawValue, pic);
    }

    private bool ProcessInitializationCommand(byte rawValue, Intel8259Pic pic) {
        var icw1Flags = (Icw1Flags)rawValue;
        if ((icw1Flags & Icw1Flags.Initialization) == 0) {
            return false;
        }

        if ((icw1Flags & Icw1Flags.FourByteInterval) != 0) {
            _loggerService.Error("PIC ({Controller}): 4-byte interval not handled", GetPicName(pic));
        }

        if ((icw1Flags & Icw1Flags.LevelTriggered) != 0) {
            _loggerService.Error("PIC ({Controller}): Level triggered mode not handled", GetPicName(pic));
        }

        if ((icw1Flags & Icw1Flags.ProcessorModeMask) != 0) {
            _loggerService.Error("PIC ({Controller}): 8080/8085 mode not handled", GetPicName(pic));
        }

        pic.SetInterruptMaskRegister(0);
        pic.IsSingleModeConfigured = (icw1Flags & Icw1Flags.SingleMode) != 0;
        pic.CurrentIcwIndex = 1u;
        pic.RemainingInitializationWords = (nuint)(2 + (rawValue & (byte)Icw1Flags.RequireIcw4));
        return true;
    }

    private bool ProcessOperationalControlWord3(ushort port, byte rawValue, Intel8259Pic pic) {
        var ocw3Flags = (Ocw3Flags)rawValue;
        if ((ocw3Flags & Ocw3Flags.CommandSelect) == 0) {
            return false;
        }

        if ((ocw3Flags & Ocw3Flags.Poll) != 0) {
            _loggerService.Error("PIC ({Controller}): Poll command not handled", GetCommandPicName(port));
        }

        if ((ocw3Flags & Ocw3Flags.FunctionSelect) != 0) {
            pic.IsIssrRequested = (ocw3Flags & Ocw3Flags.ReadIssr) != 0;
        }

        if ((ocw3Flags & Ocw3Flags.SpecialMaskSelect) == 0) {
            return true;
        }

        pic.IsSpecialMaskModeEnabled = (ocw3Flags & Ocw3Flags.SpecialMaskEnable) != 0;
        // Check if there are IRQs ready to run, as the priority system has possibly been changed.
        pic.CheckForIrq();
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("PIC {Controller} (port {Port:X}): special mask {Mode}",
                GetCommandPicName(port),
                port,
                pic.IsSpecialMaskModeEnabled ? "ON" : "OFF");
        }

        return true;
    }

    private void ProcessOperationalControlWord2(byte rawValue, Intel8259Pic pic) {
        var ocw2Flags = (Ocw2Flags)rawValue;
        byte priorityLevel = (byte)(rawValue & 0x07);
        bool isEoi = (ocw2Flags & Ocw2Flags.EndOfInterrupt) != 0;
        bool isSpecific = (ocw2Flags & Ocw2Flags.Specific) != 0;
        bool shouldRotate = (ocw2Flags & Ocw2Flags.Rotate) != 0;

        if (isEoi) {
            byte clearedIrq;
            if (isSpecific) {
                clearedIrq = priorityLevel;
            } else {
                if (pic.ActiveIrqLine == NoPendingIrq) {
                    if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                        _loggerService.Verbose("PIC {Controller}: Ignored nonspecific EOI because no IRQ is active",
                            GetPicName(pic));
                    }
                    return;
                }

                clearedIrq = pic.ActiveIrqLine;
            }

            pic.InServiceRegister &= (byte)~(1 << clearedIrq);
            pic.InServiceRegisterInverted = (byte)~pic.InServiceRegister;

            if (shouldRotate) {
                pic.SetLowestPriorityIrq(clearedIrq);
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("PIC {Controller}: Nonspecific EOI rotated lowest priority to IRQ {Irq}",
                        GetPicName(pic),
                        clearedIrq);
                }
            } else if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("PIC {Controller}: Cleared IRQ {Cleared} via EOI", GetPicName(pic), clearedIrq);
            }

            pic.CheckAfterEoi();
            return;
        }

        if (isSpecific) {
            if (!shouldRotate) {
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("PIC {Controller}: Specific priority set command without rotate ignored",
                        GetPicName(pic));
                }
                return;
            }

            pic.SetLowestPriorityIrq(priorityLevel);
            pic.CheckForIrq();
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("PIC {Controller}: Lowest priority explicitly set to IRQ {Irq}",
                    GetPicName(pic),
                    priorityLevel);
            }

            return;
        }

        pic.ShouldRotateOnAutoEoi = shouldRotate;

        if (!shouldRotate) {
            return;
        }

        pic.SetLowestPriorityIrq(priorityLevel);
        pic.CheckForIrq();
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("PIC {Controller}: Rotation command applied; lowest priority is IRQ {Irq}",
                GetPicName(pic),
                priorityLevel);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetDataPicName(ushort port) {
        return port == PrimaryPicDataPort ? nameof(PicController.Primary) : nameof(PicController.Secondary);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetCommandPicName(ushort port) {
        return port == PrimaryPicCommandPort ? nameof(PicController.Primary) : nameof(PicController.Secondary);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetPicName(Intel8259Pic pic) {
        return ReferenceEquals(pic, _primaryPic) ? nameof(PicController.Primary) : nameof(PicController.Secondary);
    }

    /// <summary>
    ///     Handles data port writes, updating OCW1 or the remaining ICW fields.
    /// </summary>
    private void WriteData(ushort port, uint value) {
        byte val = (byte)value;
        Intel8259Pic pic = GetDataPicByPort(port);
        switch (pic.CurrentIcwIndex) {
            case 0:
                pic.SetInterruptMaskRegister(val);
                break;

            case 1:
                ProcessIcw2Write(port, val, pic);
                break;

            case 2:
                ProcessIcw3Write(port, val, pic);
                break;

            case 3:
                ProcessIcw4Write(port, val, pic);
                break;

            default:
                _loggerService.Warning("PIC: Unexpected ICW value {Value:X}", val);

                break;
        }
    }

    private void ProcessIcw2Write(ushort port, byte val, Intel8259Pic pic) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("PIC {Controller}: Base vector {Vector}", GetDataPicName(port), val);
        }

        pic.InterruptVectorBase = (byte)(val & 0xf8);
        if (pic.CurrentIcwIndex++ >= pic.RemainingInitializationWords) {
            pic.CurrentIcwIndex = 0;
        } else if (pic.IsSingleModeConfigured) {
            pic.CurrentIcwIndex = 3u;
        }
    }

    private void ProcessIcw3Write(ushort port, byte val, Intel8259Pic pic) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("PIC {Controller}: ICW 3 {Vector}", GetDataPicName(port), val);
        }

        if (pic.CurrentIcwIndex++ >= pic.RemainingInitializationWords) {
            pic.CurrentIcwIndex = 0;
        }
    }

    private void ProcessIcw4Write(ushort port, byte val, Intel8259Pic pic) {
        var icw4Flags = (Icw4Flags)val;
        pic.IsAutoEndOfInterruptEnabled = (icw4Flags & Icw4Flags.AutoEoi) != 0;

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("PIC {Controller}: ICW 4 {Value}", GetDataPicName(port), val);
        }

        if ((icw4Flags & Icw4Flags.Intel8086Mode) == 0) {
            _loggerService.Error("PIC {Controller}: ICW4 {Value:X}, 8085 mode not handled", GetDataPicName(port), val);
        }

        if ((icw4Flags & Icw4Flags.SpecialFullyNestedMode) != 0) {
            _loggerService.Warning("PIC {Controller}: ICW4 {Value:X}, special fully-nested mode not handled",
                GetDataPicName(port), val);
        }

        if (pic.CurrentIcwIndex++ >= pic.RemainingInitializationWords) {
            pic.CurrentIcwIndex = 0;
        }
    }

    /// <summary>
    ///     Asserts a numbered IRQ and updates controller state accordingly.
    /// </summary>
    /// <param name="irq">IRQ number in the range 0–15.</param>
    public void ActivateIrq(byte irq) {
        if (irq > MaxIrq) {
            _loggerService.Error("PIC: Activation requested for out-of-range IRQ {Irq}", irq);
            return;
        }

        byte controllerIrq = GetEffectiveControllerIrq(irq);
        GetPicByIrq(irq).RaiseIrq(controllerIrq);
    }

    /// <summary>
    ///     Deasserts a numbered IRQ.
    /// </summary>
    /// <param name="irq">IRQ number in the range 0–15.</param>
    public void DeactivateIrq(byte irq) {
        if (irq > MaxIrq) {
            _loggerService.Error("PIC: Deactivation requested for out-of-range IRQ {Irq}", irq);
            return;
        }

        byte controllerIrq = GetEffectiveControllerIrq(irq);
        GetPicByIrq(irq).LowerIrq(controllerIrq);
    }

    /// <summary>
    ///     Maps a global IRQ number to the controller-relative line serviced by the PIC.
    /// </summary>
    /// <param name="irq">IRQ number in the range 0–15.</param>
    /// <returns>The IRQ index relative to the owning controller.</returns>
    private static byte GetEffectiveControllerIrq(byte irq) {
        return irq > 7 ? (byte)(irq - 8) : irq;
    }

    // Select the first pending IRQ on the secondary controller and cascade it through IRQ 2.
    private byte? SecondaryStartIrq() {
        byte selectedIrq = _secondaryPic.GetNextPendingIrq();
        if (selectedIrq == NoPendingIrq) {
            _loggerService.Error("PIC {Controller}: IRQ 2 is active, but IRQ is not active on the secondary controller.",
                nameof(PicController.Secondary));

            return null;
        }

        _secondaryPic.StartIrq(selectedIrq);
        _primaryPic.StartIrq(2);
        return (byte)(_secondaryPic.InterruptVectorBase + selectedIrq);
    }

    // Starts servicing the specified primary IRQ and notifies the CPU.
    private byte PrimaryStartIrq(byte index) {
        _primaryPic.StartIrq(index);
        return (byte)(_primaryPic.InterruptVectorBase + index);
    }

    /// <summary>
    ///     Applies or clears the mask bit for a numbered IRQ.
    /// </summary>
    /// <param name="irq">IRQ number in the range 0–15.</param>
    /// <param name="masked"><see langword="true" /> to suppress the IRQ; otherwise <see langword="false" />.</param>
    public void SetIrqMask(uint irq, bool masked) {
        if (irq > MaxIrq) {
            _loggerService.Error("PIC: Mask update requested for out-of-range IRQ {Irq}", irq);
            return;
        }

        byte controllerIrq = GetEffectiveControllerIrq((byte)irq);
        Intel8259Pic pic = GetPicByIrq(irq);
        if (pic.SetIrqMask(controllerIrq, masked) is not { } newMask) {
            return;
        }

        _loggerService.Debug("PIC: IRQ {Irq} mask changed to {Masked}", irq, newMask);
        IrqMaskChanged?.Invoke((byte)irq, newMask);
    }
    
    /// <summary>
    ///     Indicates whether the specified IRQ is currently masked on its controller.
    /// </summary>
    /// <param name="irq">IRQ number in the range 0–15.</param>
    /// <returns><see langword="true" /> if the IRQ is masked; otherwise <see langword="false" />.</returns>
    public bool IsInterruptMasked(byte irq) {
        byte effectiveControllerIrq = GetEffectiveControllerIrq(irq);
        Intel8259Pic pic = GetPicByIrq(irq);
        return (pic.InterruptMaskRegister & (1 << effectiveControllerIrq)) != 0;
    }
}
