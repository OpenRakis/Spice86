namespace Spice86.Core.Emulator.Devices.Video;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Devices.Video.Registers.Enums;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

public class VgaIoPortHandler : DefaultIOPortHandler {
    private readonly AttributeControllerRegisters _attributeRegisters;
    private readonly CrtControllerRegisters _crtRegisters;
    private readonly DacRegisters _dacRegisters;
    private readonly GeneralRegisters _generalRegisters;
    private readonly GraphicsControllerRegisters _graphicsRegisters;
    private readonly ILoggerService _logger;
    private readonly SequencerRegisters _sequencerRegisters;
    private bool _attributeDataMode;

    public VgaIoPortHandler(Machine machine, ILoggerService loggerService, Configuration configuration, IVideoState videoState) :
        base(machine, configuration, loggerService) {
        _logger = loggerService.WithLogLevel(LogEventLevel.Information);

        // Initialize registers.
        _attributeRegisters = videoState.AttributeControllerRegisters;
        _graphicsRegisters = videoState.GraphicsControllerRegisters;
        _sequencerRegisters = videoState.SequencerRegisters;
        _crtRegisters = videoState.CrtControllerRegisters;
        _generalRegisters = videoState.GeneralRegisters;
        _dacRegisters = videoState.DacRegisters;
    }

    private static IEnumerable<int> HandledPorts =>
        new SortedSet<int> {
            Ports.AttributeAddress,
            Ports.AttributeData,
            Ports.CrtControllerAddress,
            Ports.CrtControllerAddressAlt,
            Ports.CrtControllerAddressAltMirror1,
            Ports.CrtControllerAddressAltMirror2,
            Ports.CrtControllerData,
            Ports.CrtControllerDataAlt,
            Ports.CrtControllerDataAltMirror1,
            Ports.CrtControllerDataAltMirror2,
            Ports.DacAddressReadIndex,
            Ports.DacAddressWriteIndex,
            Ports.DacData,
            Ports.DacPelMask,
            Ports.DacStateRead,
            Ports.FeatureControlRead,
            Ports.FeatureControlWrite,
            Ports.FeatureControlWriteAlt,
            Ports.GraphicsControllerAddress,
            Ports.GraphicsControllerData,
            Ports.InputStatus0Read,
            Ports.InputStatus1Read,
            Ports.InputStatus1ReadAlt,
            Ports.MiscOutputRead,
            Ports.MiscOutputWrite,
            Ports.SequencerAddress,
            Ports.SequencerData
        };

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        foreach (int port in HandledPorts) {
            ioPortDispatcher.AddIOPortHandler(port, this);
        }
    }

    public override byte ReadByte(int port) {
        byte value;
        switch (port) {
            case Ports.DacStateRead:
                value = _dacRegisters.State;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read DAC State: {Value:X2}", port, value);
                }
                break;
            case Ports.DacAddressWriteIndex:
                value = _dacRegisters.IndexRegisterWriteMode;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read DAC Write Index: {Value:X2}", port, value);
                }
                break;
            case Ports.DacData:
                value = _dacRegisters.DataRegister;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read DAC Data Register: {Value:X2}", port, value);
                }
                break;
            case Ports.DacPelMask:
                value = _dacRegisters.PixelMask;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read DAC Pel Mask: {Value:X2}", port, value);
                }
                break;
            case Ports.GraphicsControllerAddress:
                value = (byte)_graphicsRegisters.AddressRegister;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read current Graphics register: {Value:X2} {Register}", port, value, _graphicsRegisters.AddressRegister);
                }
                break;
            case Ports.GraphicsControllerData:
                value = _graphicsRegisters.ReadRegister(_graphicsRegisters.AddressRegister);
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read from Graphics register {Register}: {Value:X2} {Explained}", port, _graphicsRegisters.AddressRegister, value, _graphicsRegisters.AddressRegister.Explain(value));
                }
                break;
            case Ports.SequencerAddress:
                value = (byte)_sequencerRegisters.Address;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read current _sequencerRegister: {Value:X2} {Register}", port, value, _sequencerRegisters.Address);
                }
                break;
            case Ports.SequencerData:
                value = _sequencerRegisters.ReadRegister(_sequencerRegisters.Address);
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read from Sequencer register {Register}: {Value:X2} {Explained}", port, _sequencerRegisters.Address, value, _sequencerRegisters.Address.Explain(value));
                }
                break;
            case Ports.AttributeAddress:
                value = (byte)_attributeRegisters.AddressRegister;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read _attributeRegister: {Value:X2} {Register}", port, value, _attributeRegisters.AddressRegister);
                }
                break;
            case Ports.AttributeData:
                value = _attributeRegisters.ReadRegister(_attributeRegisters.AddressRegister);
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read from Attribute register {Register}: {Value:X2}", port, _attributeRegisters.AddressRegister, value);
                }
                break;
            case Ports.CrtControllerAddress or Ports.CrtControllerAddressAlt or Ports.CrtControllerAddressAltMirror1 or Ports.CrtControllerAddressAltMirror2:
                value = (byte)_crtRegisters.AddressRegister;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read CrtRegisterAddress: {Value:X2} {Register}", port, value, _crtRegisters.AddressRegister);
                }
                break;
            case Ports.CrtControllerData or Ports.CrtControllerDataAlt or Ports.CrtControllerDataAltMirror1 or Ports.CrtControllerDataAltMirror2:
                value = _crtRegisters.ReadRegister(_crtRegisters.AddressRegister);
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read from CRT register {Register}: {Value:X2} {Explained}", port, _crtRegisters.AddressRegister, value, _crtRegisters.AddressRegister.Explain(value));
                }
                break;
            case Ports.InputStatus1Read or Ports.InputStatus1ReadAlt:
                _attributeDataMode = false; // Reset the attribute data mode for port 0x03C0 to "Index"
                value = _generalRegisters.InputStatusRegister1.Value;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read byte from port InputStatus1Read: {Value:X2} {Binary}", port, value, Convert.ToString(value, 2).PadLeft(8, '0'));
                }
                break;
            case Ports.InputStatus0Read:
                value = _generalRegisters.InputStatusRegister0.Value;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Read from InputStatus0Read: {Value:X2} {@FullValue}", port, value, value);
                }
                break;
            case Ports.MiscOutputRead:
                value = _generalRegisters.MiscellaneousOutput.Value;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Read MiscOutput: {Value:X2} {@Explained}", port, value, value);
                }
                break;
            default:
                value = base.ReadByte(port);
                break;
        }

        return value;
    }

    public override ushort ReadWord(int port) {
        byte value = ReadByte(port);

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Returning byte {Byte} for ReadWord() on port {Port}", value, port);
        }

        return value;
    }

    public override uint ReadDWord(int port) {
        byte value = ReadByte(port);

        if (_logger.IsEnabled(LogEventLevel.Debug)) {
            _logger.Debug("Returning byte {Byte} for ReadDWord() on port {Port}", value, port);
        }

        return value;
    }

    public override void WriteByte(int port, byte value) {
        switch (port) {
            case Ports.DacAddressReadIndex:
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to DacAddressReadIndex: {Value}", port, value);
                }
                _dacRegisters.IndexRegisterReadMode = value;
                break;

            case Ports.DacAddressWriteIndex:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to DacAddressWriteIndex: {Value}", port, value);
                }
                _dacRegisters.IndexRegisterWriteMode = value;
                break;

            case Ports.DacData:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("[{Port:X4}] Write to DacData: {Value:X2}", port, value);
                }
                _dacRegisters.DataRegister = value;
                break;

            case Ports.DacPelMask:
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to DacPelMask: {Value:X2}", port, value);
                }
                _dacRegisters.PixelMask = value;
                break;

            case Ports.GraphicsControllerAddress:
                _graphicsRegisters.AddressRegister = (GraphicsControllerRegister)value;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to GraphicsControllerAddress: {Value:X2} {Register}", port, value, _graphicsRegisters.AddressRegister);
                }
                break;

            case Ports.GraphicsControllerData:
                if (_graphicsRegisters.AddressRegister is GraphicsControllerRegister.ReadMapSelect or GraphicsControllerRegister.BitMask or GraphicsControllerRegister.GraphicsMode) {
                    if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                        _logger.Verbose("[{Port:X4}] Write to Graphics register {Register}: {Value:X2} {Explained}", port, _graphicsRegisters.AddressRegister, value, _graphicsRegisters.AddressRegister.Explain(value));
                    }
                } else if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to Graphics register {Register}: {Value:X2} {Explained}", port, _graphicsRegisters.AddressRegister, value, _graphicsRegisters.AddressRegister.Explain(value));
                }
                _graphicsRegisters.Write(_graphicsRegisters.AddressRegister, value);
                break;

            case Ports.SequencerAddress:
                _sequencerRegisters.Address = (SequencerRegister)value;
                if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    _logger.Verbose("[{Port:X4}] Write to SequencerAddress: {Value:X2} {Register}", port, value, _sequencerRegisters.Address);
                }
                break;

            case Ports.SequencerData:
                if (_sequencerRegisters.Address == SequencerRegister.PlaneMask) {
                    if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                        _logger.Verbose("[{Port:X4}] Write to Sequencer register {Register}: {Value:X2} {Explained}", port, _sequencerRegisters.Address, value, _sequencerRegisters.Address.Explain(value));
                    }
                } else if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to Sequencer register {Register}: {Value:X2} {Explained}", port, _sequencerRegisters.Address, value, _sequencerRegisters.Address.Explain(value));
                }
                _sequencerRegisters.WriteRegister(_sequencerRegisters.Address, value);
                break;

            case Ports.AttributeAddress:
                if (!_attributeDataMode) {
                    _attributeRegisters.AddressRegister = (AttributeControllerRegister)(value & 0b11111);
                    if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                        _logger.Verbose("[{Port:X4}] Write to AttributeAddress: {Value:X2} {Register}", port, value, _attributeRegisters.AddressRegister);
                    }
                } else {
                    if (_logger.IsEnabled(LogEventLevel.Debug)) {
                        _logger.Debug("[{Port:X4}] Write to Attribute register {Register}: {Value:X2} {Binary}", port, _attributeRegisters.AddressRegister, value, Convert.ToString(value, 2).PadLeft(8, '0'));
                    }
                    _attributeRegisters.WriteRegister(_attributeRegisters.AddressRegister, value);
                }

                _attributeDataMode = !_attributeDataMode;
                break;

            case Ports.AttributeData:
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to Attribute register {Register}: {Value:X2} {Binary}", port, _attributeRegisters.AddressRegister, value, Convert.ToString(value, 2).PadLeft(8, '0'));
                }
                _attributeRegisters.WriteRegister(_attributeRegisters.AddressRegister, value);
                break;

            case Ports.CrtControllerAddress or Ports.CrtControllerAddressAlt or Ports.CrtControllerAddressAltMirror1 or Ports.CrtControllerAddressAltMirror2:
                _crtRegisters.AddressRegister = (CrtControllerRegister)value;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to CrtControllerAddress: {Value:X2} {Register}", port, value, _crtRegisters.AddressRegister);
                }
                break;

            case Ports.CrtControllerData or Ports.CrtControllerDataAlt or Ports.CrtControllerDataAltMirror1 or Ports.CrtControllerDataAltMirror2:
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to CRT register {Register}: {Value:X2} {Explained}", port, _crtRegisters.AddressRegister, value, _crtRegisters.AddressRegister.Explain(value));
                }
                _crtRegisters.WriteRegister(_crtRegisters.AddressRegister, value);

                break;
            case Ports.MiscOutputWrite:
                _generalRegisters.MiscellaneousOutput.Value = value;
                if (_logger.IsEnabled(LogEventLevel.Debug)) {
                    _logger.Debug("[{Port:X4}] Write to MiscOutputWrite: {Value:X2} {@Explained}", port, value, _generalRegisters.MiscellaneousOutput);
                }
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }

    /// <summary>
    ///     Special shortcut for VGA controller to select a register and write a value in a single call.
    /// </summary>
    public override void WriteWord(int port, ushort value) {
        WriteByte(port, (byte)(value & 0xFF));
        WriteByte(port + 1, (byte)(value >> 8));
    }
}