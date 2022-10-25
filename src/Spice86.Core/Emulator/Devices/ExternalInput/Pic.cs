namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Serilog.Events;

using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Utils;
using Spice86.Shared.Interfaces;

/// <summary>
/// Emulates a PIC8259 Programmable Interrupt Controller.<br/>
/// Some resources:
/// <ul><br/>
/// <li>https://wiki.osdev.org/PIC</li><br/>
/// <li>https://k.lse.epita.fr/internals/8259a_controller.html</li><br/>
/// </ul>
/// </summary>
public class Pic : IHardwareInterruptController {
    private readonly ILoggerService _loggerService;

    private bool _initialized;

    private byte _baseInterruptVector;

    private int _initializationCommandsExpected = 2;

    private int _currentInitializationCommand = 0;

    // 1 indicates the channel is masked (inhibited), 0 indicates the channel is enabled.
    private byte _interruptMaskRegister = 0;
    private byte _interruptRequestRegister = 0;
    private byte _inServiceRegister = 0;
    private byte _currentIrq = 0;
    private byte _lowestPriorityIrq = 7;
    private bool _specialMask = false;
    private bool _polled = false;
    private bool _autoEoi = false;

    private SelectedReadRegister _selectedReadRegister = SelectedReadRegister.InterruptRequestRegister;
    private readonly ReaderWriterLockSlim _readerWriterLock = new();

    /// <summary>
    /// Initializes a new instance of the PIC.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    public Pic(ILoggerService loggerService) {
        _loggerService = loggerService;
    }

    /// <summary>
    /// Services an IRQ request
    /// </summary>
    /// <param name="irq">The IRQ Number, which will be internally translated to a vector number</param>
    public void InterruptRequest(byte irq) {
        if (!_initialized)
            return;

        _readerWriterLock.EnterWriteLock();

        uint bit = 1u << irq;
        if ((_inServiceRegister & bit) == 0) {
            _interruptRequestRegister = (byte)((_interruptRequestRegister | bit) & ~_interruptMaskRegister);
            if (irq != 0)
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("Interrupt requested for irq {Irq} ", irq);
                }
        } else {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("Interrupt request {Irq} already in service", irq);
            }
        }

        _readerWriterLock.ExitWriteLock();
    }

    /// <summary>
    /// Acknowledges an interrupt by clearing the highest priority interrupt in-service bit.
    /// </summary>
    public void AcknowledgeInterrupt() {
        ClearHighestInServiceIrq();
    }

    private void ProcessICW1(byte value) {
        bool icw4Present = (value & 0b1) != 0;
        bool singleController = (value & 0b10) != 0;
        bool levelTriggered = (value & 0b1000) != 0;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose(
                "MASTER PIC COMMAND ICW1 {Value}. {Icw4Present}, {SingleController}, {LevelTriggered}",
                ConvertUtils.ToHex8(value), icw4Present, singleController, levelTriggered);
        }

        _initialized = false;
        _currentInitializationCommand = 1;
        _initializationCommandsExpected = icw4Present ? 4 : 3;
    }

    private void ProcessICW2(byte value) {
        _baseInterruptVector = (byte)(value & 0b11111000);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("MASTER PIC COMMAND ICW2 {Value}. {BaseOffsetInInterruptDescriptorTable}",
                ConvertUtils.ToHex8(value), value);
        }
    }

    private void ProcessICW3(byte value) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("PIC COMMAND ICW3 {Value}", ConvertUtils.ToHex8(value));
        }

        if (_initializationCommandsExpected == 3) {
            _initialized = true;
        }
    }

    private void ProcessICW4(byte value) {
        _autoEoi = (value & 0b10) != 0;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("PIC COMMAND ICW4 {Value}. Auto EOI is  {AutoEoi}", ConvertUtils.ToHex8(value),
                _autoEoi);
        }

        _initialized = true;
    }

    private void ProcessOCW1(byte value) {
        _interruptMaskRegister = value;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("PIC COMMAND OCW1 {Value}. Mask is {Mask}", ConvertUtils.ToHex8(value),
                ConvertUtils.ToBin8(value));
        }
    }

    private void ProcessOCW2(byte value) {
        byte interruptLevel = (byte)(value & 0b111);
        bool sendEndOfInterruptCommand = (value & 0b10_0000) != 0;
        bool sendSpecificCommand = (value & 0b100_0000) != 0;
        bool rotatePriorities = (value & 0b1000_0000) != 0;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose(
                "PIC COMMAND OCW2 {Value}. {InterruptLevel}, {SendEndOfInterruptCommand}, {SendSpecificCommand}, @{RotatePriorities}",
                ConvertUtils.ToHex8(value), interruptLevel, sendEndOfInterruptCommand, sendSpecificCommand,
                rotatePriorities);
        }

        if (sendEndOfInterruptCommand) {
            if (sendSpecificCommand) {
                ClearInServiceRegister(interruptLevel);
            } else {
                ClearHighestInServiceIrq();
            }
        }

        if (rotatePriorities) {
            _lowestPriorityIrq = interruptLevel;
        }
    }

    private int? ComputeHighestPriorityToSearchRequests() {
        if (_specialMask) {
            // In special mask mode, we don't take into account other IRQs that may be in service
            return HighestPriorityIrq;
        }

        int? maxIrqInService = FindHighestIrqInService();
        if (maxIrqInService == HighestPriorityIrq) {
            // Highest IRQ already in service
            return null;
        }

        // No IRQ in service, will need to search for all IRQ up to the highest priority
        if (maxIrqInService == null) {
            return HighestPriorityIrq;
        }

        return maxIrqInService;
    }

    /// <summary>
    /// Determines if the interrupt controller has any pending interrupt requests.
    /// </summary>
    /// <returns>True if there is at least one pending interrupt request, false otherwise.</returns>
    public bool HasPendingRequest() {
        return EnabledInterruptRequests != 0;
    }

    private byte EnabledInterruptRequests => (byte)(_interruptRequestRegister & ~_interruptMaskRegister);

    private bool IsRequestedButNotYetInService(byte irq) {
        int irqMask = GenerateIrqMask(irq);
        if (!IsIrqRequested(irq)) {
            // Request cannot be processed if it doesn't exist
            return false;
        }
        if (IsIrqInService(irq)) {
            // IRQ request cannot be processed since it is already in service
            return false;
        }
        return true;
    }

    /// <inheritdoc />
    public byte? ComputeVectorNumber() {
        if (EnabledInterruptRequests == 0) {
            // No requests
            return null;
        }

        int? maxIrqToSearch = ComputeHighestPriorityToSearchRequests();
        if (maxIrqToSearch == null) {
            return null;
        }

        // search for higher priority Requests
        byte? irq = FindHighestPriorityIrq((int)maxIrqToSearch, IsRequestedButNotYetInService);
        if (irq == null) {
            return null;
        }

        _currentIrq = (byte)irq;
        SetInServiceRegister((byte)irq);
        ClearInterruptRequestRegister((byte)irq);
        return (byte)(_baseInterruptVector + irq);
    }

    private bool IsIrqRequested(int irq) {
        return (EnabledInterruptRequests & GenerateIrqMask(irq)) != 0;
    }

    private bool IsIrqInService(int irq) {
        return (_inServiceRegister & GenerateIrqMask(irq)) != 0;
    }

    private int GenerateIrqMask(int irq) {
        return 1 << irq;
    }

    private byte HighestPriorityIrq => (byte)((_lowestPriorityIrq + 1) % 8);

    private byte? FindHighestPriorityIrq(int stopAt, Func<byte, bool> foundCondition) {
        // Browse the irq space from the highest priority to the lowest.
        byte irq = HighestPriorityIrq;
        do {
            if (foundCondition.Invoke(irq)) {
                return irq;
            }

            irq = (byte)((irq + 1) % 8);
        } while (irq != stopAt);

        return null;
    }

    private bool IsIrqInService(byte irq) {
        return (_inServiceRegister & GenerateIrqMask(irq)) != 0;
    }

    private byte? FindHighestIrqInService() {
        return FindHighestPriorityIrq(HighestPriorityIrq, IsIrqInService);
    }

    private void ClearHighestInServiceIrq() {
        byte? highestIrqInService = FindHighestIrqInService();
        if (highestIrqInService == null) {
            return;
        }

        ClearInServiceRegister((byte)highestIrqInService);
    }

    private void SetInServiceRegister(byte irq) {
        if (!_autoEoi) {
            _inServiceRegister = (byte)(_inServiceRegister | GenerateIrqMask(irq));
        }
    }

    private void ClearInServiceRegister(byte irq) {
        _inServiceRegister = (byte)(_inServiceRegister & ~GenerateIrqMask(irq));
    }

    private void ClearInterruptRequestRegister(byte irq) {
        _interruptRequestRegister = (byte)(_interruptRequestRegister & ~GenerateIrqMask(irq));
    }

    private void ProcessOCW3(byte value) {
        int specialMask = (value & 0b1100000) >> 5;
        int poll = (value & 0b100) >> 2;
        int readOperation = value & 0b11;
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose(
                "PIC COMMAND OCW3 {Value}. {SpecialMask}, {Poll}, @{ReadOperation}",
                ConvertUtils.ToHex8(value), specialMask, poll, readOperation);
        }

        if (poll != 0) {
            _polled = true;
            return;
        }

        if (specialMask == 0b10) {
            _specialMask = false;
        } else if (specialMask == 0b11) {
            _specialMask = true;
        }

        if (readOperation == 0b10) {
            _selectedReadRegister = SelectedReadRegister.InterruptRequestRegister;
        } else if (readOperation == 0b11) {
            _selectedReadRegister = SelectedReadRegister.InServiceRegister;
        }
    }

    /// <summary>
    /// Processes the command byte write operation.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void ProcessCommandWrite(byte value) {
        if ((value & 0b1_0000) != 0) {
            ProcessICW1(value);
        } else {
            if ((value & 0b1_1000) == 0b1000) {
                ProcessOCW3(value);
            } else {
                ProcessOCW2(value);
            }
        }
    }

    /// <summary>
    /// Processes the command byte write operation.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void ProcessDataWrite(byte value) {
        if (!_initialized) {
            // Process initialization commands
            switch (_currentInitializationCommand) {
                case 1:
                    ProcessICW2(value);
                    break;
                case 2:
                    ProcessICW3(value);
                    break;
                case 3:
                    ProcessICW4(value);
                    break;
                default:
                    throw new InvalidOperationException($"Invalid initialization command index {_currentInitializationCommand}, should never happen");
            }

            _currentInitializationCommand++;
        } else {
            ProcessOCW1(value);
        }
    }

    private byte ReadPolledData() {
        ClearHighestInServiceIrq();
        _polled = false;
        return _currentIrq;
    }

    /// <summary>
    /// Reads data from the highest priority ISR and clears the corresponding bit in the In-Service Register.
    /// </summary>
    /// <returns>The ISR value that was read.</returns>
    public byte CommandRead() {
        if (_polled) {
            return ReadPolledData();
        }

        if (_selectedReadRegister == SelectedReadRegister.InServiceRegister) {
            return _inServiceRegister;
        }

        return _interruptRequestRegister;
    }

    /// <summary>
    /// Reads a byte from the command register.
    /// </summary>
    /// <returns>The byte read from the command register.</returns>
    public byte DataRead() {
        if (_polled) {
            return ReadPolledData();
        }

        return _interruptMaskRegister;
    }
}