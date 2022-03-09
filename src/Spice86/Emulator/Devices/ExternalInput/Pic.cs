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
    private const int InitializeICW1 = 0x10;
    private const int InitializeICW4 = 0x11;
    private const int MasterPortA = 0x20;
    private const int MasterPortB = 0x21;
    private const int SlavePortA = 0xA0;
    private const int SlavePortB = 0xA1;
    private static readonly ILogger _logger = Program.Logger.ForContext<Pic>();
    private static readonly Dictionary<int, int> _vectorNumberToIrq = new();
    private Command currentCommand1;
    private Command currentCommand2;
    private uint inServiceRegister1;
    private uint inServiceRegister2;
    private uint maskRegister;
    private uint requestRegister;
    private State state1;
    private State state2;

    static Pic() {
        // timer
        _vectorNumberToIrq.Add(8, 0);
        // keyboard
        _vectorNumberToIrq.Add(9, 1);
    }

    public Pic(Machine machine, Configuration configuration) : base(machine, configuration) {
    }

    private enum Command {
        None = 0,
        ReadISR = 0x0B,
        ReadIRR = 0x0A,
        Initialize = 0x10,
        InitializeICW4 = 0x11,
        EndOfInterrupt = 0x20
    }

    private enum State {
        Ready,
        Initialization_NeedVector,
        Initialization_NeedInt,
        Initialization_Need1
    }

    /// <summary>
    /// Gets the base interrupt vector for IRQ 0-7.
    /// </summary>
    public int BaseInterruptVector1 { get; private set; } = 0x08;

    /// <summary>
    /// Gets the base interrupt vector for IRQ 8-15.
    /// </summary>
    public int BaseInterruptVector2 { get; private set; } = 0x70;

    public bool IsLastIrqAcknowledged { get; private set; } = true;

    public int AcknwowledgeInterruptRequest() {
        IsLastIrqAcknowledged = true;
            for (int i = 0; i <= 7; i++) {
                uint bit = 1u << i;
                if ((this.requestRegister & bit) == bit && ((~this.maskRegister) & bit) == bit) {
                    this.requestRegister &= ~bit;
                    this.inServiceRegister1 |= bit;
                    return this.BaseInterruptVector1 + i;
                }
            }

            for (int i = 8; i <= 15; i++) {
                uint bit = 1u << i;
                if ((this.requestRegister & bit) == bit && ((~this.maskRegister) & bit) == bit) {
                    this.requestRegister &= ~bit;
                    this.inServiceRegister2 |= 1u << (i - 8);
                    return this.BaseInterruptVector2 + i;
                }
            }

            return -1;
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(MasterPortA, this);
        ioPortDispatcher.AddIOPortHandler(MasterPortB, this);
        ioPortDispatcher.AddIOPortHandler(SlavePortA, this);
        ioPortDispatcher.AddIOPortHandler(SlavePortB, this);
    }

    public bool IrqMasked(int vectorNumber) {
        if (_vectorNumberToIrq.TryGetValue(vectorNumber, out var irqNumber) == false) {
            return false;
        }
        int maskForVectorNumber = (1 << irqNumber);
        return (maskForVectorNumber & maskRegister) != 0;
    }

    public void RaiseHardwareInterrupt(int irq) {
        if (this.state1 != State.Ready && this.state2 != State.Ready)
            return;

        // Only allow the request if not already being serviced.
        if (irq < 8) {
            uint bit = 1u << irq;
            if ((this.inServiceRegister1 & bit) == 0)
                this.requestRegister |= bit;
        } else {
            uint bit = 1u << (irq - 8);
            if ((this.inServiceRegister2 & bit) == 0)
                this.requestRegister |= bit;
        }
    }

    public void ProcessInterrupt(byte vector) {
        if (IrqMasked(vector)) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("Cannot process interrupt {@ProcessInterrupt}, IRQ is masked.", ConvertUtils.ToHex8(vector));
            }
            return;
        }

        if (!IsLastIrqAcknowledged) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("Cannot process interrupt {@ProcessInterrupt}, Last IRQ was not acknowledged.", ConvertUtils.ToHex8(vector));
            }

            return;
        }

        if (this.state1 != State.Ready && this.state2 != State.Ready)
            return;

        // Only allow the request if not already being serviced.
        if (vector < 8) {
            uint bit = 1u << vector;
            if ((this.inServiceRegister1 & bit) == 0)
                this.requestRegister |= bit;
        } else {
            uint bit = 1u << (vector - 8);
            if ((this.inServiceRegister2 & bit) == 0)
                this.requestRegister |= bit;
        }

        IsLastIrqAcknowledged = false;
        _cpu.ExternalInterrupt(vector);
        IsLastIrqAcknowledged = true;
    }

    public override ushort ReadWord(int port) {
        return this.ReadByte(port);
    }

    public override byte ReadByte(int port) {
            switch (port) {
                case MasterPortA:
                    switch (this.currentCommand1) {
                        case Command.ReadISR:
                            return (byte)this.inServiceRegister1;

                        case Command.ReadIRR:
                            return (byte)this.requestRegister;
                    }
                    break;

                case MasterPortB:
                    return (byte)this.maskRegister;

                case SlavePortA:
                    switch (currentCommand2) {
                        case Command.ReadISR:
                            return (byte)this.inServiceRegister2;

                        case Command.ReadIRR:
                            return (byte)(this.requestRegister >> 8);
                    }
                    break;

                case SlavePortB:
                    return (byte)(this.maskRegister >> 8);
            }

            return 0;
    }

    public override void WriteByte(int port, byte value) {
            uint registerValue = this.maskRegister;

            switch (port) {
                case MasterPortA:
                    if (value == (int)Command.EndOfInterrupt) {
                        this.EndCurrentInterrupt1();
                    } else if (value == (int)Command.ReadIRR || value == (int)Command.ReadISR) {
                        this.currentCommand1 = (Command)value;
                    } else if ((value & 0x10) != 0) // ICW1
                      {
                        if (value == InitializeICW1 || value == InitializeICW4) {
                            this.requestRegister = 0;
                            this.inServiceRegister1 = 0;
                            this.inServiceRegister2 = 0;
                            this.maskRegister = 0;
                            this.state1 = State.Initialization_NeedVector;
                        } else {
                            throw new NotImplementedException();
                        }
                    } else if ((value & 0x18) == 0) // OCW2
                      {
                        if ((value & 0xE0) == 0x60) // Specific EOI
                            this.inServiceRegister1 &= ~(1u << (value & 0x07));
                        else
                            throw new NotImplementedException();
                    } else {
                        throw new NotImplementedException();
                    }
                    break;

                case MasterPortB:
                    switch (this.state1) {
                        case State.Initialization_NeedVector:
                            this.BaseInterruptVector1 = value;
                            this.state1 = State.Initialization_NeedInt;
                            break;

                        case State.Initialization_NeedInt:
                            this.state1 = State.Initialization_Need1;
                            break;

                        case State.Initialization_Need1:
                            if (value != 1) {
                                throw new UnhandledOperationException(_machine, $"Invalid initialization command index {value}, should never happen");
                            }
                            this.state1 = State.Ready;
                            break;

                        case State.Ready:
                            registerValue &= 0xFF00;
                            registerValue |= value;
                            this.maskRegister = registerValue;
                            break;
                    }
                    break;

                case SlavePortA:
                    this.currentCommand2 = (Command)value;
                    switch (this.currentCommand2) {
                        case Command.Initialize:
                        case Command.InitializeICW4:
                            this.state2 = State.Initialization_NeedVector;
                            break;

                        case Command.EndOfInterrupt:
                            this.EndCurrentInterrupt2();
                            break;
                    }
                    break;

                case SlavePortB:
                    switch (this.state2) {
                        case State.Initialization_NeedVector:
                            this.BaseInterruptVector2 = value;
                            this.state2 = State.Initialization_NeedInt;
                            break;

                        case State.Initialization_NeedInt:
                            this.state2 = State.Initialization_Need1;
                            break;

                        case State.Initialization_Need1:
                            if (value != 1) {
                                throw new UnhandledOperationException(_machine, $"Invalid initialization command index {value}, should never happen");
                            }
                            this.state2 = State.Ready;
                            break;

                        case State.Ready:
                            registerValue &= 0x00FF;
                            registerValue |= (uint)value << 8;
                            this.maskRegister = registerValue;
                            break;
                    }
                    break;
            }
    }

    public override void WriteWord(int port, ushort value) {
        if (port == 0x20) {
            this.WriteByte(MasterPortA, (byte)value);
            this.WriteByte(MasterPortB, (byte)(value >> 8));
        } else if (port == 0xA0) {
            this.WriteByte(SlavePortA, (byte)value);
            this.WriteByte(SlavePortB, (byte)(value >> 8));
        }
    }

    /// <summary>
    /// Ends the highest-priority in-service interrupt on controller 1.
    /// </summary>
    private void EndCurrentInterrupt1() {
        this.inServiceRegister1 = Intrinsics.ResetLowestSetBit(this.inServiceRegister1);
    }

    /// <summary>
    /// Ends the highest-priority in-service interrupt on controller 2.
    /// </summary>
    private void EndCurrentInterrupt2() {
        this.inServiceRegister2 = Intrinsics.ResetLowestSetBit(this.inServiceRegister2);
    }
}