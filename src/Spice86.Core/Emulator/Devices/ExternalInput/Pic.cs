using Spice86.Logging;
using Spice86.Shared.Interfaces;

namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Serilog;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Utils;

/// <summary>
/// Emulates a PIC8259 Programmable Interrupt Controller.<br/>
/// Some resources:
/// <ul>
/// <li>https://wiki.osdev.org/PIC</li>
/// <li>https://k.lse.epita.fr/internals/8259a_controller.html</li>
/// </ul>
/// </summary>
public class Pic {
    private readonly ILoggerService _loggerService;

    private readonly Machine _machine;

    private bool _initialized;

    private byte _baseInterruptVector;

    private int _initializationCommandsExpected = 2;

    private int _currentInitializationCommand = 0;

    private readonly bool _master;

    // 1 indicates the channel is masked (inhibited), 0 indicates the channel is enabled.
    private byte _interruptMaskRegister = 0;
    private byte _interruptRequestRegister = 0;
    private byte _inServiceRegister = 0;
    private byte _currentIrq = 0;
    private byte _lowestPriorityIrq = 7;
    private bool _specialMask = false;
    private bool _polled = false;
    private bool _interruptOngoing = false;
    private bool _autoEoi = false;
    private SelectedReadRegister _selectedReadRegister = SelectedReadRegister.InterruptRequestRegister;

    public Pic(Machine machine, ILoggerService loggerService, bool master) {
        _loggerService = loggerService;
        _master = master;
        _machine = machine;
    }

    /// <summary>
    /// Services an IRQ request
    /// </summary>
    /// <param name="irq">The IRQ Number, which will be internally translated to a vector number</param>
    /// <exception cref="UnrecoverableException">If not defined in the ISA bus IRQ table</exception>
    public void InterruptRequest(byte irq) {
        SetInterruptRequestRegister(irq);
    }

    public void AcknwowledgeInterrupt() {
        ClearHighestInServiceIrq();
    }

    private void ProcessICW1(byte value) {
        bool icw4Present = (value & 0b1) != 0;
        bool singleController = (value & 0b10) != 0;
        bool levelTriggered = (value & 0b1000) != 0;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information(
                "MASTER PIC COMMAND ICW1 {Value}. {Icw4Present}, {SingleController}, {LevelTriggered}",
                ConvertUtils.ToHex8(value), icw4Present, singleController, levelTriggered);
        }

        _initialized = false;
        _currentInitializationCommand = 1;
        _initializationCommandsExpected = icw4Present ? 4 : 3;
    }

    private void ProcessICW2(byte value) {
        _baseInterruptVector = (byte)(value & 0b11111000);
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("MASTER PIC COMMAND ICW2 {Value}. {BaseOffsetInInterruptDescriptorTable}",
                ConvertUtils.ToHex8(value), value);
        }
    }

    private void ProcessICW3(byte value) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("PIC COMMAND ICW3 {Value}.", ConvertUtils.ToHex8(value));
        }

        if (_initializationCommandsExpected == 3) {
            _initialized = true;
        }
    }

    private void ProcessICW4(byte value) {
        _autoEoi = (value & 0b10) != 0;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("PIC COMMAND ICW4 {Value}. Auto EOI is  {AutoEoi}", ConvertUtils.ToHex8(value),
                _autoEoi);
        }

        _initialized = true;
    }

    private void ProcessOCW1(byte value) {
        _interruptMaskRegister = value;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("PIC COMMAND OCW1 {Value}. Mask is {Mask}", ConvertUtils.ToHex8(value),
                ConvertUtils.ToBin8(value));
        }
    }

    private void ProcessOCW2(byte value) {
        byte interruptLevel = (byte)(value & 0b111);
        bool sendEndOfInterruptCommand = (value & 0b10_0000) != 0;
        bool sendSpecificCommand = (value & 0b100_0000) != 0;
        bool rotatePriorities = (value & 0b1000_0000) != 0;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information(
                "PIC COMMAND OCW2 {Value}. {InterruptLevel}, {SendEndOfInterruptCommand}, {SendSpecificCommand}, @{RotatePriorities}",
                ConvertUtils.ToHex8(value), interruptLevel, sendEndOfInterruptCommand, sendSpecificCommand,
                rotatePriorities);
        }

        if (rotatePriorities) {
            _lowestPriorityIrq = interruptLevel;
        }

        if (sendEndOfInterruptCommand) {
            if (sendSpecificCommand) {
                ClearInServiceRegister(interruptLevel);
            } else {
                ClearHighestInServiceIrq();
            }
        }
    }

    private int? ComputeHighestPriorityToSearchRequests() {
        if (_specialMask) {
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

    public bool HasPendingRequest() {
        return EnabledInterruptRequests != 0;
    }

    private byte EnabledInterruptRequests => (byte)(_interruptRequestRegister & ~_interruptMaskRegister);

    public byte? ComputeVectorNumber() {
        byte enabledInterruptRequests = EnabledInterruptRequests;
        if (enabledInterruptRequests == 0) {
            // No requests
            _interruptOngoing = false;
            return null;
        }

        int? maxIrqToSearch = ComputeHighestPriorityToSearchRequests();
        if (maxIrqToSearch == null) {
            return null;
        }

        // search for higher priority Requests 
        byte? irq = FindIrq((int)maxIrqToSearch, (irq) => {
            int irqMask = GenerateIrqMask(irq);
            bool irqInService = (_inServiceRegister & irqMask) != 0;
            bool irqRequestExists = (enabledInterruptRequests & irqMask) != 0;
            return !(_specialMask && irqInService) && !_interruptOngoing && irqRequestExists;
        });
        if (irq == null) {
            return null;
        }

        // Higher priority request found, servicing it
        if (!_autoEoi) {
            _interruptOngoing = true;
        }

        _currentIrq = (byte)irq;
        ClearInterruptRequestRegister((byte)irq);
        SetInServiceRegister((byte)irq);
        return (byte)(_baseInterruptVector + irq);
    }

    private int GenerateIrqMask(int irq) {
        return 1 << irq;
    }

    private byte HighestPriorityIrq => (byte)((_lowestPriorityIrq + 1) % 8);

    private byte? FindIrq(int stopAt, Func<int, bool> irqFoundCondition) {
        // Browse the irq space from the highest priority to the lowest.
        byte irq = HighestPriorityIrq;
        do {
            if (irqFoundCondition.Invoke(irq)) {
                return irq;
            }

            irq = (byte)((irq + 1) % 8);
        } while (irq != stopAt);

        return null;
    }

    private byte? FindHighestIrqInService() {
        return FindIrq(HighestPriorityIrq, (irq) => {
            return (_inServiceRegister & GenerateIrqMask(irq)) != 0;
        });
    }

    private void ClearHighestInServiceIrq() {
        int? highestIrqInService = FindHighestIrqInService();
        if (highestIrqInService == null) {
            return;
        }

        ClearInServiceRegister((int)highestIrqInService);
    }

    private void SetInServiceRegister(byte irq) {
        if (!_autoEoi) {
            _inServiceRegister = (byte)(_inServiceRegister | GenerateIrqMask(irq));
        }
    }

    private void ClearInServiceRegister(int irq) {
        _inServiceRegister = (byte)(_inServiceRegister & ~GenerateIrqMask(irq));
    }

    private void SetInterruptRequestRegister(byte irq) {
        _interruptRequestRegister = (byte)(_interruptRequestRegister | GenerateIrqMask(irq));
    }

    private void ClearInterruptRequestRegister(byte irq) {
        _interruptRequestRegister = (byte)(_interruptRequestRegister & ~GenerateIrqMask(irq));
    }

    private void ProcessOCW3(byte value) {
        int specialMask = (value & 0b1100000) >> 5;
        int poll = (value & 0b100) >> 2;
        int readOperation = value & 0b11;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information(
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
                    throw new UnhandledOperationException(_machine,
                        $"Invalid initialization command index {_currentInitializationCommand}, should never happen");
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

    public byte CommandRead() {
        if (_polled) {
            return ReadPolledData();
        }

        if (_selectedReadRegister == SelectedReadRegister.InServiceRegister) {
            return _inServiceRegister;
        }

        return _interruptRequestRegister;
    }

    public byte DataRead() {
        if (_polled) {
            return ReadPolledData();
        }

        return _interruptMaskRegister;
    }
}

enum SelectedReadRegister {
    InServiceRegister, InterruptRequestRegister
}