namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Serilog.Events;

using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
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
///     Represents the callback signature invoked when a queued PIC event fires.
/// </summary>
/// <param name="value">Controller-supplied value associated with the event.</param>
public delegate void PicEventHandler(uint value);

/// <summary>
///     Represents the callback signature invoked once per simulated millisecond tick.
/// </summary>
public delegate void TimerTickHandler();

/// <summary>
///     Coordinates the paired PIC channels, handles bus registration, and advances deterministic tick scheduling.
/// </summary>
/// <remarks>
///     Installs port handlers on construction, mirrors interrupt register state, and integrates with the shared CPU
///     scheduler without wall-clock dependencies.
/// </remarks>
public sealed class DualPic : IDisposable {
    /// <summary>
    ///     Identifies which controller to observe or configure.
    /// </summary>
    /// <remarks>
    ///     The primary channel handles IRQ lines 0 through 7, while the secondary cascades through IRQ 2 and serves
    ///     lines 8 through 15.
    /// </remarks>
    public enum PicChannel {
        /// <summary>
        ///     PIC servicing IRQ lines 0 through 7.
        /// </summary>
        Primary,

        /// <summary>
        ///     PIC servicing IRQ lines 8 through 15.
        /// </summary>
        Secondary
    }

    private const ushort PrimaryPicCommandPort = 0x20;
    private const ushort PrimaryPicDataPort = 0x21;
    private const ushort SecondaryPicCommandPort = 0xa0;
    private const ushort SecondaryPicDataPort = 0xa1;

    private const byte SpecificEoiBase = (byte)(Ocw2Flags.EndOfInterrupt | Ocw2Flags.Specific);

    private const int HandlerCount = 4;
    private readonly PicPitCpuState _cpuState;
    private readonly PicEventQueue _eventQueue;
    private readonly IoSystem _ioSystem;
    private readonly ILoggerService _logger;

    private readonly Intel8259Channel _primaryChannel;

    private readonly IoReadHandler[] _readHandlers = new IoReadHandler[HandlerCount];
    private readonly Intel8259Channel _secondaryChannel;
    private readonly IoWriteHandler[] _writeHandlers = new IoWriteHandler[HandlerCount];

    // Thread-safe copy of the fractional tick index used by asynchronous consumers.
    private double _atomicIndex;
    private TickerBlock? _firstTicker;

    /// <summary>
    ///     Initializes controller state, registers I/O handlers, and configures deterministic scheduling.
    /// </summary>
    /// <param name="ioSystem">I/O fabric used to register command and data port handlers.</param>
    /// <param name="cpuState">Shared CPU timing state consumed by both controllers.</param>
    /// <param name="loggerService">Logger used for diagnostic messages.</param>
    public DualPic(IoSystem ioSystem, PicPitCpuState cpuState, ILoggerService loggerService) {
        _ioSystem = ioSystem;
        _cpuState = cpuState;
        _logger = loggerService;

        _primaryChannel = new PrimaryPicChannel(_logger, _cpuState, SetIrqCheck);
        _secondaryChannel = new SecondaryPicChannel(_logger, CascadeRaiseFromSecondary, CascadeLowerFromSecondary);
        _eventQueue = new PicEventQueue(_cpuState, _logger);

        InitializeControllers();
        InitializeQueue();
        InstallHandlers();
    }

    /// <summary>
    ///     Gets a value indicating whether any pending IRQ requires CPU attention.
    /// </summary>
    public bool IrqCheck { get; private set; }

    /// <summary>
    ///     Gets the elapsed tick count maintained by the PIC scheduler.
    /// </summary>
    public uint Ticks { get; private set; }

    /// <summary>
    ///     Releases registered I/O handlers from the shared bus.
    /// </summary>
    public void Dispose() {
        foreach (IoReadHandler handler in _readHandlers) {
            handler.Uninstall();
        }

        foreach (IoWriteHandler handler in _writeHandlers) {
            handler.Uninstall();
        }
    }

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
        _readHandlers[0] = new IoReadHandler(_ioSystem, ReadCommand, _logger);
        _readHandlers[0].Install(PrimaryPicCommandPort);
        _readHandlers[1] = new IoReadHandler(_ioSystem, ReadData, _logger);
        _readHandlers[1].Install(PrimaryPicDataPort);
        _readHandlers[2] = new IoReadHandler(_ioSystem, ReadCommand, _logger);
        _readHandlers[2].Install(SecondaryPicCommandPort);
        _readHandlers[3] = new IoReadHandler(_ioSystem, ReadData, _logger);
        _readHandlers[3].Install(SecondaryPicDataPort);

        _writeHandlers[0] = new IoWriteHandler(_ioSystem, WriteCommand, _logger);
        _writeHandlers[0].Install(PrimaryPicCommandPort);
        _writeHandlers[1] = new IoWriteHandler(_ioSystem, WriteData, _logger);
        _writeHandlers[1].Install(PrimaryPicDataPort);
        _writeHandlers[2] = new IoWriteHandler(_ioSystem, WriteCommand, _logger);
        _writeHandlers[2].Install(SecondaryPicCommandPort);
        _writeHandlers[3] = new IoWriteHandler(_ioSystem, WriteData, _logger);
        _writeHandlers[3].Install(SecondaryPicDataPort);
    }

    private void CascadeRaiseFromSecondary() {
        // The secondary PIC signals the primary over IRQ 2; raising it propagates the cascade line.
        _primaryChannel.RaiseIrq(2);
    }

    private void CascadeLowerFromSecondary() {
        // Lowering the same cascade line releases the request on the primary controller.
        _primaryChannel.LowerIrq(2);
    }

    /// <summary>
    ///     Updates the cached IRQ check flag used by the CPU loop.
    /// </summary>
    /// <param name="value">Value to assign.</param>
    private void SetIrqCheck(bool value) {
        IrqCheck = value;
    }

    /// <summary>
    ///     Resets controller registers and reinitializes the event queue.
    /// </summary>
    public void Reset() {
        _logger.Debug("PIC: Reset invoked; controllers and event queue will be reinitialized");

        InitializeControllers();
        InitializeQueue();
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
            case > 15:
                _logger.Error("PIC: Acknowledge requested for out-of-range IRQ {Irq}", irq);
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
    ///     Computes the next interrupt vector if one is pending, mirroring the legacy API.
    /// </summary>
    /// <returns>The vector number, or <see langword="null" /> if none is pending.</returns>
    public byte? ComputeVectorNumber() {
        RunIrqs();
        UIntPtr? last = _cpuState.LastHardwareInterrupt;
        if (last == null) {
            return null;
        }

        _cpuState.ClearLastHardwareInterrupt();
        return (byte)last.Value;
    }

    /// <summary>
    ///     Resets both PIC channels and applies the default mask/unmask configuration.
    /// </summary>
    private void InitializeControllers() {
        IrqCheck = false;
        Ticks = 0;

        _primaryChannel.Initialize();
        _secondaryChannel.Initialize();

        SetIrqMask(0, false); // Enable system timer IRQ 0.
        SetIrqMask(1, false); // Enable keyboard controller IRQ 1.
        SetIrqMask(2, false); // Route the cascade line so the secondary can signal the primary.
        SetIrqMask(8, false); // Enable the RTC source on the secondary PIC (IRQ 8).

        SetIrqMask(9, false); // AT-era systems expose IRQ 9 in addition to the cascade line.
        DeactivateIrq(9); // Clear any stale latch on IRQ 9 to mirror the bootstrap sequence.
    }

    /// <summary>
    ///     Clears the event queue state and associated bookkeeping.
    /// </summary>
    private void InitializeQueue() {
        _eventQueue.Initialize();
        _firstTicker = null;
        _atomicIndex = 0.0;
    }

    /// <summary>
    ///     Captures a snapshot of controller-facing registers for inspection.
    /// </summary>
    /// <param name="channel">Controller to query.</param>
    /// <returns>Structure describing the register state.</returns>
    public PicChannelSnapshot GetChannelSnapshot(PicChannel channel) {
        Intel8259Channel pic = channel == PicChannel.Primary ? _primaryChannel : _secondaryChannel;
        return new PicChannelSnapshot(pic.InterruptRequestRegister,
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
        Intel8259Channel pic = GetCommandChannelByPort(port);
        return pic.IsIssrRequested ? pic.InServiceRegister : pic.InterruptRequestRegister;
    }

    /// <summary>
    ///     Services reads from a PIC data port, returning the current mask register.
    /// </summary>
    private uint ReadData(ushort port) {
        return GetDataChannelByPort(port).InterruptMaskRegister;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Intel8259Channel GetCommandChannelByPort(ushort port) {
        switch (port) {
            case PrimaryPicCommandPort:
                return _primaryChannel;
            case SecondaryPicCommandPort:
                return _secondaryChannel;
            default:
                _logger.Error("PIC: Command port {Port:X} is not recognized; defaulting to primary controller", port);
                return _primaryChannel;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Intel8259Channel GetDataChannelByPort(ushort port) {
        switch (port) {
            case PrimaryPicDataPort:
                return _primaryChannel;
            case SecondaryPicDataPort:
                return _secondaryChannel;
            default:
                _logger.Error("PIC: Data port {Port:X} is not recognized; defaulting to primary controller", port);
                return _primaryChannel;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Intel8259Channel GetChannelByIrq(uint irq) {
        return irq > 7 ? _secondaryChannel : _primaryChannel;
    }

    /// <summary>
    ///     Handles command port writes, decoding ICW/OCW sequences and maintaining controller state.
    /// </summary>
    private void WriteCommand(ushort port, uint value) {
        byte rawValue = NumericHelpers.CheckCast<byte, uint>(value);
        Intel8259Channel pic = GetCommandChannelByPort(port);

        if (ProcessInitializationCommand(rawValue, pic)) {
            return;
        }

        if (ProcessOperationalControlWord3(port, rawValue, pic)) {
            return;
        }

        ProcessOperationalControlWord2(rawValue, pic);
    }

    private bool ProcessInitializationCommand(byte rawValue, Intel8259Channel pic) {
        var icw1Flags = (Icw1Flags)rawValue;
        if ((icw1Flags & Icw1Flags.Initialization) == 0) {
            return false;
        }

        if ((icw1Flags & Icw1Flags.FourByteInterval) != 0) {
            _logger.Error("PIC ({Channel}): 4-byte interval not handled", GetChannelName(pic));
        }

        if ((icw1Flags & Icw1Flags.LevelTriggered) != 0) {
            _logger.Error("PIC ({Channel}): Level triggered mode not handled", GetChannelName(pic));
        }

        if ((icw1Flags & Icw1Flags.ProcessorModeMask) != 0) {
            _logger.Error("PIC ({Channel}): 8080/8085 mode not handled", GetChannelName(pic));
        }

        pic.SetInterruptMaskRegister(0);
        pic.IsSingleModeConfigured = (icw1Flags & Icw1Flags.SingleMode) != 0;
        pic.CurrentIcwIndex = 1u;
        pic.RemainingInitializationWords = (nuint)(2 + (rawValue & (byte)Icw1Flags.RequireIcw4));
        return true;
    }

    private bool ProcessOperationalControlWord3(ushort port, byte rawValue, Intel8259Channel pic) {
        var ocw3Flags = (Ocw3Flags)rawValue;
        if ((ocw3Flags & Ocw3Flags.CommandSelect) == 0) {
            return false;
        }

        if ((ocw3Flags & Ocw3Flags.Poll) != 0) {
            _logger.Error("PIC ({Channel}): Poll command not handled", GetCommandChannelName(port));
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
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("PIC {Channel} (port {Port:X}): special mask {Mode}",
                GetCommandChannelName(port),
                port,
                pic.IsSpecialMaskModeEnabled ? "ON" : "OFF");
        }

        return true;
    }

    private void ProcessOperationalControlWord2(byte rawValue, Intel8259Channel pic) {
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
                if (pic.ActiveIrqLine == 8) {
                    if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                        _logger.Verbose("PIC {Channel}: Ignored nonspecific EOI because no IRQ is active",
                            GetChannelName(pic));
                    }
                    return;
                }

                clearedIrq = pic.ActiveIrqLine;
            }

            pic.InServiceRegister &= (byte)~(1 << clearedIrq);
            pic.InServiceRegisterInverted = (byte)~pic.InServiceRegister;

            if (shouldRotate) {
                pic.SetLowestPriorityIrq(clearedIrq);
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("PIC {Channel}: Nonspecific EOI rotated lowest priority to IRQ {Irq}",
                        GetChannelName(pic),
                        clearedIrq);
                }
            } else if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                _logger.Verbose("PIC {Channel}: Cleared IRQ {Cleared} via EOI", GetChannelName(pic), clearedIrq);
            }

            pic.CheckAfterEoi();
            return;
        }

        if (isSpecific) {
            if (!shouldRotate) {
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("PIC {Channel}: Specific priority set command without rotate ignored",
                        GetChannelName(pic));
                }
                return;
            }

            pic.SetLowestPriorityIrq(priorityLevel);
            pic.CheckForIrq();
            if (_logger.IsEnabled(LogEventLevel.Debug)) {
                _logger.Debug("PIC {Channel}: Lowest priority explicitly set to IRQ {Irq}",
                    GetChannelName(pic),
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
        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("PIC {Channel}: Rotation command applied; lowest priority is IRQ {Irq}",
                GetChannelName(pic),
                priorityLevel);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetDataChannelName(ushort port) {
        return port == PrimaryPicDataPort ? nameof(PicChannel.Primary) : nameof(PicChannel.Secondary);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetCommandChannelName(ushort port) {
        return port == PrimaryPicCommandPort ? nameof(PicChannel.Primary) : nameof(PicChannel.Secondary);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetChannelName(Intel8259Channel channel) {
        return ReferenceEquals(channel, _primaryChannel) ? nameof(PicChannel.Primary) : nameof(PicChannel.Secondary);
    }

    /// <summary>
    ///     Handles data port writes, updating OCW1 or the remaining ICW fields.
    /// </summary>
    private void WriteData(ushort port, uint value) {
        byte val = NumericHelpers.CheckCast<byte, uint>(value);
        Intel8259Channel pic = GetDataChannelByPort(port);
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
                _logger.Warning("PIC: Unexpected ICW value {Value:X}", val);

                break;
        }
    }

    private void ProcessIcw2Write(ushort port, byte val, Intel8259Channel pic) {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("PIC {Channel}: Base vector {Vector}", GetDataChannelName(port), val);
        }

        pic.InterruptVectorBase = (byte)(val & 0xf8);
        if (pic.CurrentIcwIndex++ >= pic.RemainingInitializationWords) {
            pic.CurrentIcwIndex = 0;
        } else if (pic.IsSingleModeConfigured) {
            pic.CurrentIcwIndex = 3u;
        }
    }

    private void ProcessIcw3Write(ushort port, byte val, Intel8259Channel pic) {
        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("PIC {Channel}: ICW 3 {Vector}", GetDataChannelName(port), val);
        }

        if (pic.CurrentIcwIndex++ >= pic.RemainingInitializationWords) {
            pic.CurrentIcwIndex = 0;
        }
    }

    private void ProcessIcw4Write(ushort port, byte val, Intel8259Channel pic) {
        var icw4Flags = (Icw4Flags)val;
        pic.IsAutoEndOfInterruptEnabled = (icw4Flags & Icw4Flags.AutoEoi) != 0;

        if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            _logger.Verbose("PIC {Channel}: ICW 4 {Value}", GetDataChannelName(port), val);
        }

        if ((icw4Flags & Icw4Flags.Intel8086Mode) == 0) {
            _logger.Error("PIC {Channel}: ICW4 {Value:X}, 8085 mode not handled", GetDataChannelName(port), val);
        }

        if ((icw4Flags & Icw4Flags.SpecialFullyNestedMode) != 0) {
            _logger.Warning("PIC {Channel}: ICW4 {Value:X}, special fully-nested mode not handled",
                GetDataChannelName(port), val);
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
        if (irq > 15) {
            _logger.Error("PIC: Activation requested for out-of-range IRQ {Irq}", irq);
            return;
        }

        byte channelIrq = irq > 7 ? (byte)(irq - 8) : irq;

        int oldCycles = _cpuState.Cycles;
        GetChannelByIrq(irq).RaiseIrq(channelIrq);

        if (oldCycles == _cpuState.Cycles) {
            return;
        }

        // if CPU_Cycles have changed, this means that the interrupt was triggered by an I/O
        // register writing rather than an event.
        // Real hardware executes 0 to ~13 NOPs or comparable instructions
        // before the processor picks up the interrupt. Let's try with 2
        // cycles here.
        // Required by Panic demo (irq0), It came from the desert (MPU401)
        // Does it matter if CPU_CycleLeft becomes negative?
        // It might be an idea to do this always to simulate this
        // So on writing mask and EOI as well. (so inside the activate function)
        //		CPU_CycleLeft += (CPU_Cycles-2);
        _cpuState.CyclesLeft -= 2;
        _cpuState.Cycles = 2;
    }

    /// <summary>
    ///     Deasserts a numbered IRQ.
    /// </summary>
    /// <param name="irq">IRQ number in the range 0–15.</param>
    public void DeactivateIrq(byte irq) {
        if (irq > 15) {
            _logger.Error("PIC: Deactivation requested for out-of-range IRQ {Irq}", irq);
            return;
        }

        byte channelIrq = irq > 7 ? (byte)(irq - 8) : irq;
        GetChannelByIrq(irq).LowerIrq(channelIrq);
    }

    // Select the first pending IRQ on the secondary controller and cascade it through IRQ 2.
    private void SecondaryStartIrq() {
        byte selectedIrq = _secondaryChannel.GetNextPendingIrq();
        if (selectedIrq == 8) {
            _logger.Error("PIC {Channel}: IRQ 2 is active, but IRQ is not active on the secondary controller.",
                nameof(PicChannel.Secondary));

            return;
        }

        _secondaryChannel.StartIrq(selectedIrq);
        _primaryChannel.StartIrq(2);
        _cpuState.CpuHwInterrupt((nuint)(_secondaryChannel.InterruptVectorBase + selectedIrq));
    }

    // Starts servicing the specified primary IRQ and notifies the CPU.
    private void PrimaryStartIrq(byte index) {
        _primaryChannel.StartIrq(index);
        _cpuState.CpuHwInterrupt((nuint)(_primaryChannel.InterruptVectorBase + index));
    }

    /// <summary>
    ///     Resolves pending IRQs when the CPU interrupt flag permits delivery.
    /// </summary>
    /// <remarks>
    ///     Skips dispatch when interrupts are disabled, no IRQs are latched, or the decoder is executing the trap-run
    ///     sentinel used to drain outstanding cycles.
    /// </remarks>
    private void RunIrqs() {
        if (!_cpuState.InterruptFlag) {
            return;
        }

        if (!IrqCheck) {
            return;
        }

        byte nextIrq = _primaryChannel.GetNextPendingIrq();
        switch (nextIrq) {
            case 8:
                IrqCheck = false;
                return;
            case 2:
                SecondaryStartIrq();
                break;
            default:
                PrimaryStartIrq(nextIrq);
                break;
        }

        // Disable check variable.
        IrqCheck = false;
    }

    /// <summary>
    ///     Applies or clears the mask bit for a numbered IRQ.
    /// </summary>
    /// <param name="irq">IRQ number in the range 0–15.</param>
    /// <param name="masked"><see langword="true" /> to suppress the IRQ; otherwise <see langword="false" />.</param>
    public void SetIrqMask(uint irq, bool masked) {
        if (irq > 15) {
            _logger.Error("PIC: Mask update requested for out-of-range IRQ {Irq}", irq);
            return;
        }

        byte channelIrq = irq > 7 ? (byte)(irq - 8) : (byte)irq;
        Intel8259Channel pic = GetChannelByIrq(irq);
        // clear bit
        byte bit = (byte)(1 << channelIrq);
        byte newMask = pic.InterruptMaskRegister;
        newMask &= (byte)~bit;
        if (masked) {
            newMask |= bit;
        }

        byte previousMask = pic.InterruptMaskRegister;
        pic.SetInterruptMaskRegister(newMask);

        if (((previousMask ^ newMask) & bit) != 0) {
            bool isMasked = (newMask & bit) != 0;
            _logger.Debug("PIC: IRQ {Irq} mask changed to {Masked}", irq, isMasked);

            IrqMaskChanged?.Invoke((byte)irq, isMasked);
        }
    }

    /// <summary>
    ///     Queues a PIC event with a fractional tick delay.
    /// </summary>
    /// <param name="handler">Callback to invoke.</param>
    /// <param name="delay">Delay in tick units relative to the current index.</param>
    /// <param name="val">Value forwarded to the callback.</param>
    public void AddEvent(PicEventHandler handler, double delay, uint val = 0) {
        _logger.Verbose("PIC: Scheduling event {Handler} with delay {Delay} and payload {Value}", handler.Method.Name,
            delay, val);
        _eventQueue.AddEvent(handler, delay, val);
    }

    /// <summary>
    ///     Removes queued events matching both handler and value.
    /// </summary>
    /// <param name="handler">Handler to match.</param>
    /// <param name="val">Value to match.</param>
    public void RemoveSpecificEvents(PicEventHandler handler, uint val) {
        _logger.Verbose("PIC: Removing specific events for {Handler} with payload {Value}", handler.Method.Name, val);
        _eventQueue.RemoveSpecificEvents(handler, val);
    }

    /// <summary>
    ///     Removes all queued events matching the provided handler.
    /// </summary>
    /// <param name="handler">Handler to remove.</param>
    public void RemoveEvents(PicEventHandler handler) {
        _logger.Verbose("PIC: Removing all events for {Handler}", handler.Method.Name);
        _eventQueue.RemoveEvents(handler);
    }

    /// <summary>
    ///     Advances queued events, synchronizes the atomic index, and services resulting IRQs.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when the queue processed or retained events; otherwise <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     Invokes <see cref="UpdateAtomicIndex" /> before processing so asynchronous consumers observe the latest
    ///     fractional tick.
    /// </remarks>
    public bool RunQueue() {
        UpdateAtomicIndex();
        bool queueResult = _eventQueue.RunQueue();
        if (queueResult) {
            RunIrqs();
        }

        return queueResult;
    }

    /// <summary>
    ///     Removes a previously registered per-tick handler.
    /// </summary>
    /// <param name="handler">Handler to remove from the list.</param>
    /// <remarks>
    ///     No action is taken if the handler is not present.
    /// </remarks>
    public void RemoveTickHandler(TimerTickHandler handler) {
        TickerBlock? previous = null;
        TickerBlock? current = _firstTicker;

        while (current != null) {
            if (current.Handler == handler) {
                if (previous == null) {
                    _firstTicker = current.Next;
                } else {
                    previous.Next = current.Next;
                }

                return;
            }

            previous = current;
            current = current.Next;
        }
    }

    /// <summary>
    ///     Registers a handler to run once per simulated tick.
    /// </summary>
    /// <param name="handler">Handler to invoke each tick.</param>
    /// <remarks>
    ///     Handlers execute in last-in-first-out order because new registrations are added to the front of the list.
    /// </remarks>
    public void AddTickHandler(TimerTickHandler handler) {
        var newTicker = new TickerBlock {
            Handler = handler,
            Next = _firstTicker
        };
        _firstTicker = newTicker;
    }

    /// <summary>
    ///     Advances the tick counter, executes scheduled events, and invokes tick handlers.
    /// </summary>
    /// <remarks>
    ///     Resets the CPU slice counters to their maximum values before evaluating queued events for the new tick.
    /// </remarks>
    public void AddTick() {
        // Set up new number of cycles for PIC.
        _cpuState.CyclesLeft = _cpuState.CyclesMax;
        _cpuState.Cycles = 0;
        Ticks++;

        // Decrement each scheduled entry by one tick (the queue stores offsets in 1.0 tick units).
        _eventQueue.DecrementIndicesForTick();

        // Call our list of ticker handlers.
        TickerBlock? ticker = _firstTicker;
        while (ticker != null) {
            TickerBlock? nextTicker = ticker.Next;
            ticker.Handler?.Invoke();
            ticker = nextTicker;
        }
    }

    /// <summary>
    ///     Computes the fractional tick index using the current CPU cycle progress.
    /// </summary>
    /// <returns>Tick count with a sub-tick fractional component.</returns>
    /// <remarks>
    ///     Adds the integral tick counter to the fraction returned by <see cref="PicPitCpuState.GetTickIndex" />.
    /// </remarks>
    public double GetFullIndex() {
        return Ticks + _cpuState.GetTickIndex();
    }

    /// <summary>
    ///     Stores a thread-safe copy of the fractional tick index.
    /// </summary>
    /// <remarks>Further calls to <see cref="GetAtomicIndex" /> return the value captured here.</remarks>
    public void UpdateAtomicIndex() {
        _atomicIndex = GetFullIndex();
    }

    /// <summary>
    ///     Gets the last stored thread-safe fractional tick index.
    /// </summary>
    /// <returns>Previously stored fractional tick value.</returns>
    /// <remarks>
    ///     The returned value is only refreshed by <see cref="UpdateAtomicIndex" />.
    /// </remarks>
    public double GetAtomicIndex() {
        return _atomicIndex;
    }

    /// <summary>
    ///     Converts a fractional tick amount into an integral cycle count using the CPU scheduler.
    /// </summary>
    /// <param name="amount">Fraction of the current tick window.</param>
    /// <returns>Integral cycle count.</returns>
    public int MakeCycles(double amount) {
        return _cpuState.MakeCycles(amount);
    }

    [Flags]
    private enum Icw1Flags : byte {
        RequireIcw4 = 0x01,
        SingleMode = 0x02,
        FourByteInterval = 0x04,
        LevelTriggered = 0x08,
        Initialization = 0x10,
        ProcessorModeMask = 0xe0
    }

    [Flags]
    private enum Ocw3Flags : byte {
        ReadIssr = 0x01,
        FunctionSelect = 0x02,
        Poll = 0x04,
        CommandSelect = 0x08,
        SpecialMaskEnable = 0x20,
        SpecialMaskSelect = 0x40
    }

    [Flags]
    private enum Ocw2Flags : byte {
        EndOfInterrupt = 0x20,
        Specific = 0x40,
        Rotate = 0x80
    }

    [Flags]
    private enum Icw4Flags : byte {
        Intel8086Mode = 0x01,
        AutoEoi = 0x02,
        SpecialFullyNestedMode = 0x10
    }

    private sealed class TickerBlock {
        public TimerTickHandler? Handler;
        public TickerBlock? Next;
    }
}
