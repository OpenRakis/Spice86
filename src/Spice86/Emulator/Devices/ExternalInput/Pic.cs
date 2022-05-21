namespace Spice86.Emulator.Devices.ExternalInput;

using Serilog;

using Spice86.Emulator.Errors;
using Spice86.Emulator.IOPorts;
using Spice86.Emulator.VM;
using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Emulates a PIC8259 Programmable Interrupt Controller.<br/>
/// Some resources:
/// <ul>
/// <li>https://wiki.osdev.org/PIC</li>
/// <li>https://k.lse.epita.fr/internals/8259a_controller.html</li>
/// </ul>
/// </summary>
public class Pic : DefaultIOPortHandler {
    private static readonly ILogger _logger = Program.Logger.ForContext<Pic>();

    private const int MasterPortA = 0x20;

    private const int MasterPortB = 0x21;

    private const int SlavePortA = 0xA0;

    private const int SlavePortB = 0xA1;

    private static readonly Dictionary<int, int> _irqToVectorNumber = new();

    private int _commandsToProcess = 2;

    private int _currentCommand = 0;

    private bool _initialized = false;

    private byte _interruptMask = 0;

    static Pic() {
        // timer
        _irqToVectorNumber.Add(0, 8);
        // keyboard
        _irqToVectorNumber.Add(1, 9);
    }

    public Pic(Machine machine, bool initialized, Configuration configuration) : base(machine, configuration) {
        _initialized = initialized;
    }

    public void RegisterVectorNumberToIrq(byte vectorNumber, int irq) => _irqToVectorNumber.Add(irq, vectorNumber);

    public void ProcessInterruptRequest(int irq)
    {
        IsLastIrqAcknowledged = false;
        if(_irqToVectorNumber.TryGetValue(irq, out var vectorNumber))
        {
            ProcessInterruptVector((byte)vectorNumber);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(irq));
        }
        AcknwowledgeInterrupt();
    }

    public void AcknwowledgeInterrupt() {
        IsLastIrqAcknowledged = true;
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(MasterPortA, this);
        ioPortDispatcher.AddIOPortHandler(MasterPortB, this);
        ioPortDispatcher.AddIOPortHandler(SlavePortA, this);
        ioPortDispatcher.AddIOPortHandler(SlavePortB, this);
    }

    public bool IrqMasked(int vectorNumber) {
        if (_irqToVectorNumber.TryGetValue(vectorNumber, out var irqNumber) == false) {
            return false;
        }
        int maskForVectorNumber = (1 << irqNumber);
        return (maskForVectorNumber & _interruptMask) != 0;
    }

    public bool IsLastIrqAcknowledged { get; private set; } = true;

    public override byte ReadByte(int port) {
        return _interruptMask;
    }

    public override void WriteByte(int port, byte value) {
        if (port == MasterPortA) {
            ProcessPortACommand(value);
            return;
        } else if (port == MasterPortB) {
            ProcessPortBCommand(value);
            return;
        }
        base.WriteByte(port, value);
    }

    public void ProcessInterruptVector(byte vectorNumber) {
        if (IrqMasked(vectorNumber)) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("Cannot process interrupt {@ProcessInterrupt}, IRQ is masked.", ConvertUtils.ToHex8(vectorNumber));
            }
            return;
        }

        if (!IsLastIrqAcknowledged) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("Cannot process interrupt {@ProcessInterrupt}, Last IRQ was not acknowledged.", ConvertUtils.ToHex8(vectorNumber));
            }

            return;
        }

        IsLastIrqAcknowledged = false;
        _cpu.ExternalInterrupt(vectorNumber);
    }

    private static void ProcessICW2(byte value) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("MASTER PIC COMMAND ICW2 {@Value}. {@BaseOffsetInInterruptDescriptorTable}", ConvertUtils.ToHex8(value),
                value);
        }
    }

    private static void ProcessICW3(byte value) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PIC COMMAND ICW3 {@Value}.", ConvertUtils.ToHex8(value));
        }
    }

    private static void ProcessICW4(byte value) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PIC COMMAND ICW4 {@Value}.", ConvertUtils.ToHex8(value));
        }
    }

    private void ProcessICW1(byte value) {
        bool icw4Present = (value & 0b1) == 1;
        bool singleController = (value & 0b10) == 1;
        bool levelTriggered = (value & 0b1000) == 1;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("MASTER PIC COMMAND ICW1 {@Value}. {@Icw4Present}, {@SingleController}, {@LevelTriggered}",
                ConvertUtils.ToHex8(value), icw4Present, singleController, levelTriggered);
        }
        _commandsToProcess = icw4Present ? 4 : 3;
    }

    private void ProcessOCW1(byte value) {
        _interruptMask = value;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PIC COMMAND OCW1 {@Value}. Mask is {@Mask}", ConvertUtils.ToHex8(value), ConvertUtils.ToBin8(value));
        }
    }

    private void ProcessOCW2(byte value) {
        int interruptLevel = value & 0b111;
        bool sendEndOfInterruptCommand = (value & 0b100000) != 0;
        IsLastIrqAcknowledged = sendEndOfInterruptCommand;
        bool sendSpecificCommand = (value & 0b1000000) != 0;
        bool rotatePriorities = (value & 0b10000000) != 0;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information(
                "PIC COMMAND OCW2 {@Value}. {@InterruptLevel}, {@SendEndOfInterruptCommand}, {@SendSpecificCommand}, @{RotatePriorities}",
                ConvertUtils.ToHex8(value), interruptLevel, sendEndOfInterruptCommand, sendSpecificCommand, rotatePriorities);
        }
    }

    private void ProcessPortACommand(byte value) {
        if (!_initialized) {
            // Process initialization commands
            switch (_currentCommand) {
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
                    throw new UnhandledOperationException(_machine, $"Invalid initialization command index {_currentCommand}, should never happen");
            }
            _currentCommand = (_currentCommand + 1) % _commandsToProcess;
            if (_currentCommand == 0) {
                _commandsToProcess = 2;
                _initialized = true;
            }
        } else {
            ProcessOCW2(value);
        }
    }

    private void ProcessPortBCommand(byte value) {
        if (!_initialized) {
            ProcessICW1(value);
            _currentCommand = 1;
        } else {
            ProcessOCW1(value);
        }
    }
}