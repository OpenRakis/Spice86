namespace Spice86.Emulator.Devices
{
    using Serilog;

    using Spice86.Emulator.Errors;
    using Spice86.Emulator.IOPorts;
    using Spice86.Emulator.Machine;
    using Spice86.Utils;

    using System.Collections.Generic;

    public class Pic : DefaultIOPortHandler
    {
        private const int MasterPortA = 0x20;

        private const int MasterPortB = 0x21;

        private const int SlavePortA = 0xA0;

        private const int SlavePortB = 0xA1;

        private static readonly ILogger _logger = Log.Logger.ForContext<Pic>();

        private static readonly Dictionary<int, int> _vectorNumberToIrq = new();

        private int _commandsToProcess = 2;

        private int _currentCommand = 0;

        private bool _inintialized = false;

        private int _interruptMask = 0;

        private bool _lastIrqAcknowledged = true;

        static Pic()
        {
            _vectorNumberToIrq.Add(8, 0);
            _vectorNumberToIrq.Add(9, 1);
        }

        public Pic(Machine machine, bool initialized, bool failOnUnhandledPort) : base(machine, failOnUnhandledPort)
        {
            this._inintialized = initialized;
        }

        public virtual void AcknwowledgeInterrupt()
        {
            _lastIrqAcknowledged = true;
        }

        public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher)
        {
            ioPortDispatcher.AddIOPortHandler(MasterPortA, this);
            ioPortDispatcher.AddIOPortHandler(MasterPortB, this);
            ioPortDispatcher.AddIOPortHandler(SlavePortA, this);
            ioPortDispatcher.AddIOPortHandler(SlavePortB, this);
        }

        public virtual bool IrqMasked(int vectorNumber)
        {
            if (_vectorNumberToIrq.TryGetValue(vectorNumber, out var irqNumber) == false)
            {
                return false;
            }
            int maskForVectorNumber = (1 << irqNumber);
            return (maskForVectorNumber & _interruptMask) != 0;
        }

        public virtual bool IsLastIrqAcknowledged()
        {
            return _lastIrqAcknowledged;
        }

        public override void Outb(int port, int value)
        {
            if (port == MasterPortA)
            {
                ProcessPortACommand(value);
                return;
            }
            else if (port == MasterPortB)
            {
                ProcessPortBCommand(value);
                return;
            }
            base.Outb(port, value);
        }

        public virtual void ProcessInterrupt(int vectorNumber)
        {
            if (IrqMasked(vectorNumber))
            {
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
                {
                    _logger.Information("Cannot process interrupt {@ProcessInterrupt}, IRQ is masked.", ConvertUtils.ToHex8(vectorNumber));
                }

                return;
            }

            if (!IsLastIrqAcknowledged())
            {
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
                {
                    _logger.Information("Cannot process interrupt {@ProcessInterrupt}, Last IRQ was not acknowledged.", ConvertUtils.ToHex8(vectorNumber));
                }

                return;
            }

            _lastIrqAcknowledged = false;
            cpu.ExternalInterrupt(vectorNumber);
        }

        private void ProcessICW1(int value)
        {
            bool icw4Present = (value & 0b1) == 1;
            bool singleController = (value & 0b10) == 1;
            bool levelTriggered = (value & 0b1000) == 1;
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
            {
                _logger.Information("MASTER PIC COMMAND ICW1 {@Value}. {@Icw4Present}, {@SingleController}, {@LevelTriggered}",
                    ConvertUtils.ToHex8(value), icw4Present, singleController, levelTriggered);
            }
            _commandsToProcess = icw4Present ? 4 : 3;
        }

        private static void ProcessICW2(int value)
        {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
            {
                _logger.Information("MASTER PIC COMMAND ICW2 {@Value}. {@BaseOffsetInInterruptDescriptorTable}", ConvertUtils.ToHex8(value),
                    value);
            }
        }

        private static void ProcessICW3(int value)
        {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
            {
                _logger.Information("PIC COMMAND ICW3 {@Value}.", ConvertUtils.ToHex8(value));
            }
        }

        private static void ProcessICW4(int value)
        {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
            {
                _logger.Information("PIC COMMAND ICW4 {@Value}.", ConvertUtils.ToHex8(value));
            }
        }

        private void ProcessOCW1(int value)
        {
            _interruptMask = value;
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
            {
                _logger.Information("PIC COMMAND OCW1 {@Value}. Mask is {@Mask}", ConvertUtils.ToHex8(value), ConvertUtils.ToBin8(value));
            }
        }

        private void ProcessOCW2(int value)
        {
            int interruptLevel = value & 0b111;
            bool sendEndOfInterruptCommand = (value & 0b100000) != 0;
            _lastIrqAcknowledged = sendEndOfInterruptCommand;
            bool sendSpecificCommand = (value & 0b1000000) != 0;
            bool rotatePriorities = (value & 0b10000000) != 0;
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information))
            {
                _logger.Information(
                    "PIC COMMAND OCW2 {@Value}. {@InterruptLevel}, {@SendEndOfInterruptCommand}, {@SendSpecificCommand}, @{RotatePriorities}",
                    ConvertUtils.ToHex8(value), interruptLevel, sendEndOfInterruptCommand, sendSpecificCommand, rotatePriorities);
            }
        }

        private void ProcessPortACommand(int value)
        {
            if (!_inintialized)
            {
                // Process initialization commands
                if (_currentCommand == 1)
                {
                    ProcessICW2(value);
                }
                else if (_currentCommand == 2)
                {
                    ProcessICW3(value);
                }
                else if (_currentCommand == 3)
                {
                    ProcessICW4(value);
                }
                else
                {
                    throw new UnhandledOperationException(machine,
                        $"Invalid initialization command index {_currentCommand}, should never happen");
                }
            }
            _currentCommand = (_currentCommand + 1) % _commandsToProcess;
            if (_currentCommand == 0)
            {
                _commandsToProcess = 2;
                _inintialized = true;
            }
            else
            {
                ProcessOCW2(value);
            }
        }

        private void ProcessPortBCommand(int value)
        {
            if (!_inintialized)
            {
                ProcessICW1(value);
                _currentCommand = 1;
            }
            else
            {
                ProcessOCW1(value);
            }
        }
    }
}