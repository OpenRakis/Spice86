namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Interfaces;

/// <summary>
/// Emulates and manages the two PICs of the IBM PC.
/// </summary>
public class DualPic : DefaultIOPortHandler {
    private const int MasterCommand = 0x20;

    private const int MasterData = 0x21;

    private const int SlaveCommand = 0xA0;

    private const int SlaveData = 0xA1;

    private const byte DefaultIcw1 = 0b10001;
    
    private const byte DefaultIcw4 = 0b0001;

    private const byte BaseInterruptVectorMaster = 0x08;

    private const byte BaseInterruptVectorSlave = 0x70;

    private readonly IHardwareInterruptController _pic1;
    private readonly IHardwareInterruptController _pic2;

    /// <summary>
    /// Initializes a new instance of the <see cref="DualPic"/> class.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="configuration">The emulator configuration.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DualPic(Machine machine, Configuration configuration, ILoggerService loggerService) : base(machine, configuration, loggerService) {
        _pic1 = new Pic(loggerService);
        _pic2 = new Pic(loggerService);
        Initialize();
    }
    
    /// <summary>
    /// Initializes the PICs with default initialization commands.
    /// </summary>
    public void Initialize() {
        // Send default initialization commands to the pics
        // ICW1
        _pic1.ProcessCommandWrite(DefaultIcw1);
        _pic2.ProcessCommandWrite(DefaultIcw1);
        // ICW2
        _pic1.ProcessDataWrite(BaseInterruptVectorMaster);
        _pic2.ProcessDataWrite(BaseInterruptVectorSlave);
        // ICW3
        _pic1.ProcessDataWrite(0b00000100); // slave at irq 2
        _pic2.ProcessDataWrite(0); // slave id 0
        // ICW4
        _pic1.ProcessDataWrite(DefaultIcw4);
        _pic2.ProcessDataWrite(DefaultIcw4);
    }

    /// <summary>
    /// Masks all interrupts globally by setting the interrupt mask bit in the processor's status register. <br/>
    /// This prevents any interrupts from being serviced while the processor is executing critical sections of code
    /// that must not be interrupted.
    /// or events.
    /// </summary>
    public void MaskAllInterrupts() {
        _pic1.ProcessDataWrite(0xFF);
        _pic2.ProcessDataWrite(0xFF);
    }

    /// <summary>
    /// Services an IRQ request
    /// </summary>
    /// <param name="irq">The IRQ Number, which will be internally translated to a vector number</param>
    /// <exception cref="UnrecoverableException">If not defined in the ISA bus IRQ table</exception>
    public void ProcessInterruptRequest(byte irq) {
        if (irq < 8) {
            _pic1.InterruptRequest(irq);
        } else if (irq < 15) {
            _pic2.InterruptRequest((byte)(irq - 8));
        } else {
            throw new UnhandledOperationException(_machine, $"IRQ {irq} not supported at the moment");
        }
    }
    
    /// <summary>
    /// Determines whether this instance has a pending interrupt request.
    /// </summary>
    /// <returns>
    /// <c>true</c> if this instance has a pending interrupt request; otherwise, <c>false</c>.
    /// </returns>
    public bool HasPendingRequest() {
        return _pic1.HasPendingRequest() || _pic2.HasPendingRequest();
    }

    /// <summary>
    /// Computes the interrupt vector number from the first PIC that has a pending request,
    /// or from the second PIC if the first PIC has no pending requests.
    /// If neither PIC has a pending request, returns null.
    /// </summary>
    /// <returns>The interrupt vector number, or null if no pending request.</returns>
    public byte? ComputeVectorNumber() {
        if (_pic1.HasPendingRequest()) {
            return _pic1.ComputeVectorNumber();
        }

        if (_pic2.HasPendingRequest()) {
            return _pic2.ComputeVectorNumber();
        }

        return null;
    }
    
    /// <summary>
    /// Acknowledges the interrupt request from the first PIC. <br/>
    /// This signals that the PIC has processed the interrupt request and is ready to receive new requests.
    /// </summary>
    public void AcknowledgeInterrupt(byte irq) {
        if (irq < 8) {
            _pic1.AcknowledgeInterrupt();
        } else if (irq < 15) {
            _pic2.AcknowledgeInterrupt();
            _pic1.AcknowledgeInterrupt();
        } else {
            throw new UnhandledOperationException(_machine, $"IRQ {irq} not supported at the moment");
        }
    }

    /// <inheritdoc />
    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(MasterCommand, this);
        ioPortDispatcher.AddIOPortHandler(MasterData, this);
        ioPortDispatcher.AddIOPortHandler(SlaveCommand, this);
        ioPortDispatcher.AddIOPortHandler(SlaveData, this);
    }

    /// <inheritdoc />
    public override byte ReadByte(int port) {
        return port switch {
            MasterCommand => _pic1.CommandRead(),
            MasterData => _pic1.DataRead(),
            SlaveCommand => _pic2.CommandRead(),
            SlaveData => _pic2.DataRead(),
            _ => base.ReadByte(port),
        };
    }

    /// <inheritdoc />
    public override ushort ReadWord(int port) {
        if (port == MasterCommand) {
            return (ushort)(ReadByte(MasterCommand) | ReadByte(SlaveCommand) << 8);
        }

        if (port == MasterData) {
            return (ushort)(ReadByte(MasterData) | ReadByte(SlaveData) << 8);
        }

        return base.ReadWord(port);
    }

    /// <inheritdoc />
    public override void WriteByte(int port, byte value) {
        switch (port) {
            case MasterCommand:
                _pic1.ProcessCommandWrite(value);
                break;
            case MasterData:
                _pic1.ProcessDataWrite(value);
                break;
            case SlaveCommand:
                _pic2.ProcessCommandWrite(value);
                break;
            case SlaveData:
                _pic2.ProcessDataWrite(value);
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }

    /// <inheritdoc />
    public override void WriteWord(int port, ushort value) {
        if (port == MasterCommand) {
            WriteByte(MasterCommand, (byte)value);
            WriteByte(SlaveCommand, (byte)(value >> 8));
            return;
        }

        if (port == MasterData) {
            WriteByte(MasterData, (byte)value);
            WriteByte(SlaveData, (byte)(value >> 8));
            return;
        }

        base.WriteWord(port, value);
    }
}