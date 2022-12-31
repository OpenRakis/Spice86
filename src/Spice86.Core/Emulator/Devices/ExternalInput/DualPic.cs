namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Core.DI;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;

public class DualPic : DefaultIOPortHandler {
    private const int MasterCommand = 0x20;

    private const int MasterData = 0x21;

    private const int SlaveCommand = 0xA0;

    private const int SlaveData = 0xA1;

    private const byte DefaultIcw1 = 0x11;
    // Auto EOI
    private const byte DefaultIcw4 = 0b10;

    private const byte BaseInterruptVectorMaster = 0x08;

    private const byte BaseInterruptVectorSlave = 0x70;

    private readonly Pic _pic1;
    private readonly Pic _pic2;

    public DualPic(Machine machine, Configuration configuration) : base(machine, configuration) {
        _pic1 = new Pic(machine, new ServiceProvider().GetLoggerForContext<Pic>(), true);
        _pic2 = new Pic(machine, new ServiceProvider().GetLoggerForContext<Pic>(), false);
        Initialize();
    }

    public void Initialize() {
        // Send default initialization commands to the pics
        // ICW1
        _pic1.ProcessCommandWrite(DefaultIcw1);
        _pic2.ProcessCommandWrite(DefaultIcw1);
        // ICW2
        _pic1.ProcessDataWrite(BaseInterruptVectorMaster);
        _pic2.ProcessDataWrite(BaseInterruptVectorSlave);
        // ICW3
        _pic1.ProcessDataWrite(0);
        _pic2.ProcessDataWrite(0);
        // ICW4
        _pic1.ProcessDataWrite(DefaultIcw4);
        _pic2.ProcessDataWrite(DefaultIcw4);
    }

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

    public bool HasPendingRequest() {
        return _pic1.HasPendingRequest() || _pic2.HasPendingRequest();
    }

    public byte? ComputeVectorNumber() {
        if (_pic1.HasPendingRequest()) {
            return _pic1.ComputeVectorNumber();
        }

        if (_pic2.HasPendingRequest()) {
            return _pic2.ComputeVectorNumber();
        }

        return null;
    }

    public void AcknwowledgeInterrupt() {
        _pic1.AcknwowledgeInterrupt();
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(MasterCommand, this);
        ioPortDispatcher.AddIOPortHandler(MasterData, this);
        ioPortDispatcher.AddIOPortHandler(SlaveCommand, this);
        ioPortDispatcher.AddIOPortHandler(SlaveData, this);
    }

    public override byte ReadByte(int port) {
        return port switch {
            MasterCommand => _pic1.CommandRead(),
            MasterData => _pic1.DataRead(),
            SlaveCommand => _pic2.CommandRead(),
            SlaveData => _pic2.DataRead(),
            _ => base.ReadByte(port),
        };
    }

    public override ushort ReadWord(int port) {
        if (port == MasterCommand) {
            return (ushort)(ReadByte(MasterCommand) | ReadByte(SlaveCommand) << 8);
        }

        if (port == MasterData) {
            return (ushort)(ReadByte(MasterData) | ReadByte(SlaveData) << 8);
        }

        return base.ReadWord(port);
    }

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