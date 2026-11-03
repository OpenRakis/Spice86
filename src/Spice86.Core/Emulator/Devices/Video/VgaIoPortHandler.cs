// Tandy register dispatch adapted from DOSBox Staging.
// SPDX-FileCopyrightText: 2020-2026 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.Devices.Video;


using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Devices.Video.Registers.Enums;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Platform;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Shared.Interfaces;

/// <summary>
///     I/O port handler for the VGA card.
/// </summary>
public class VgaIoPortHandler : DefaultIOPortHandler {
    private readonly AttributeControllerRegisters _attributeRegisters;
    private readonly CrtControllerRegisters _crtRegisters;
    private readonly DacRegisters _dacRegisters;
    private readonly GeneralRegisters _generalRegisters;
    private readonly GraphicsControllerRegisters _graphicsRegisters;
    private readonly SequencerRegisters _sequencerRegisters;
    private readonly IVideoState _videoState;
    private readonly IEmulatedClock _clock;
    private bool _attributeDataMode;
    private readonly TandyVideoState? _tandy;

    /// <summary>
    ///     Create a new VGA I/O port handler.
    /// </summary>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="ioPortDispatcher">The class that is responsible for dispatching ports reads and writes to classes that respond to them.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="videoState">Represents the state of the video card.</param>
    /// <param name="clock">The emulated clock driving the beam independently of rendering.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    public VgaIoPortHandler(State state, IOPortDispatcher ioPortDispatcher, ILogger loggerService,
        IVideoState videoState, IEmulatedClock clock, bool failOnUnhandledPort)
        : this(state, ioPortDispatcher, loggerService, videoState, clock, failOnUnhandledPort, MachineProfile.For(MachineType.Pc)) {
    }

    /// <summary>Registers only the selected machine's native video ports.</summary>
    public VgaIoPortHandler(State state, IOPortDispatcher ioPortDispatcher, ILogger loggerService,
        IVideoState videoState, IEmulatedClock clock, bool failOnUnhandledPort, MachineProfile profile) : base(state, failOnUnhandledPort, loggerService) {

        // Initialize registers.
        _attributeRegisters = videoState.AttributeControllerRegisters;
        _graphicsRegisters = videoState.GraphicsControllerRegisters;
        _sequencerRegisters = videoState.SequencerRegisters;
        _crtRegisters = videoState.CrtControllerRegisters;
        _generalRegisters = videoState.GeneralRegisters;
        _dacRegisters = videoState.DacRegisters;
        _videoState = videoState;
        _clock = clock;
        _tandy = profile.IsTandy ? (videoState as VideoState)?.Tandy : null;
        _videoState.BeamTiming.Refresh(0);
        InitPortHandlers(ioPortDispatcher);
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
            Ports.SequencerData,
            Ports.CgaModeControl,
            Ports.CgaColorSelect
        };

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        if (_tandy != null) {
            for (int port = 0x3D0; port <= 0x3DC; port++) {
                ioPortDispatcher.AddIOPortHandler(port, this);
            }
            ioPortDispatcher.AddIOPortHandler(0x3DE, this);
            ioPortDispatcher.AddIOPortHandler(0x3DF, this);
            return;
        }
        foreach (int port in HandledPorts) {
            ioPortDispatcher.AddIOPortHandler(port, this);
        }
    }

    /// <inheritdoc />
    public override byte ReadByte(ushort port) {
        if (_tandy != null) {
            if (port == 0x3DA) {
                return (byte)((ReadBeamStatus() & 9) | 4);
            }
            if (port is >= 0x3D0 and <= 0x3D7) {
                if ((port & 1) == 0) {
                    return _tandy.CrtcIndex;
                }
                return _tandy.CrtcIndex < 32 ? _tandy.Crtc[_tandy.CrtcIndex] : (byte)0xFF;
            }
            return 0xFF;
        }
        byte value;
        switch (port) {
            case Ports.DacStateRead:
                value = _dacRegisters.State;
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Read DAC State: {Value:X2}", port, value);
                }
                break;
            case Ports.DacAddressWriteIndex:
                value = _dacRegisters.IndexRegisterWriteMode;
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Read DAC Write Index: {Value:X2}", port, value);
                }
                break;
            case Ports.DacData:
                value = _dacRegisters.DataRegister;
                if (_loggerService.IsEnabled(LogLevel.Trace)) {
                    _loggerService.LogTrace("[{Port:X4}] Read DAC Data Register: {Value:X2}", port, value);
                }
                break;
            case Ports.DacPelMask:
                value = _dacRegisters.PixelMask;
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Read DAC Pel Mask: {Value:X2}", port, value);
                }
                break;
            case Ports.GraphicsControllerAddress:
                value = (byte)_graphicsRegisters.AddressRegister;
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Read current Graphics register: {Value:X2} {Register}", port, value, _graphicsRegisters.AddressRegister);
                }
                break;
            case Ports.GraphicsControllerData:
                value = _graphicsRegisters.ReadRegister(_graphicsRegisters.AddressRegister);
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Read from Graphics register {Register}: {Value:X2} {Explained}", port, _graphicsRegisters.AddressRegister, value, _graphicsRegisters.AddressRegister.Explain(value));
                }
                break;
            case Ports.SequencerAddress:
                value = (byte)_sequencerRegisters.Address;
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Read current _sequencerRegister: {Value:X2} {Register}", port, value, _sequencerRegisters.Address);
                }
                break;
            case Ports.SequencerData:
                value = _sequencerRegisters.ReadRegister(_sequencerRegisters.Address);
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Read from Sequencer register {Register}: {Value:X2} {Explained}", port, _sequencerRegisters.Address, value, _sequencerRegisters.Address.Explain(value));
                }
                break;
            case Ports.AttributeAddress:
                value = (byte)_attributeRegisters.AddressRegister;
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Read _attributeRegister: {Value:X2} {Register}", port, value, _attributeRegisters.AddressRegister);
                }
                break;
            case Ports.AttributeData:
                value = _attributeRegisters.ReadRegister(_attributeRegisters.AddressRegister);
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Read from Attribute register {Register}: {Value:X2}", port, _attributeRegisters.AddressRegister, value);
                }
                break;
            case Ports.CrtControllerAddress or Ports.CrtControllerAddressAlt or Ports.CrtControllerAddressAltMirror1 or Ports.CrtControllerAddressAltMirror2:
                value = (byte)_crtRegisters.AddressRegister;
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Read CrtRegisterAddress: {Value:X2} {Register}", port, value, _crtRegisters.AddressRegister);
                }
                break;
            case Ports.CrtControllerData or Ports.CrtControllerDataAlt or Ports.CrtControllerDataAltMirror1 or Ports.CrtControllerDataAltMirror2:
                value = _crtRegisters.ReadRegister(_crtRegisters.AddressRegister);
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Read from CRT register {Register}: {Value:X2} {Explained}", port, _crtRegisters.AddressRegister, value, _crtRegisters.AddressRegister.Explain(value));
                }
                break;
            case Ports.InputStatus1Read or Ports.InputStatus1ReadAlt:
                _attributeDataMode = false; // Reset the attribute data mode for port 0x03C0 to "Index"
                value = ReadBeamStatus();
                if (_loggerService.IsEnabled(LogLevel.Trace)) {
                    _loggerService.LogTrace("[{Port:X4}] Read byte from port InputStatus1Read: {Value:X2} {Binary}", port, value, Convert.ToString(value, 2).PadLeft(8, '0'));
                }
                break;
            case Ports.InputStatus0Read:
                value = _generalRegisters.InputStatusRegister0.Value;
                if (_loggerService.IsEnabled(LogLevel.Trace)) {
                    _loggerService.LogTrace("[{Port:X4}] Read from InputStatus0Read: {Value:X2} {@FullValue}", port, value, value);
                }
                break;
            case Ports.MiscOutputRead:
                value = _generalRegisters.MiscellaneousOutput.Value;
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Read MiscOutput: {Value:X2} {@Explained}", port, value, value);
                }
                break;
            case Ports.CgaModeControl or Ports.CgaColorSelect:
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Read write-only CGA register", port);
                }
                value = 0xFF;
                break;
            default:
                value = base.ReadByte(port);
                break;
        }

        return value;
    }

    private byte ReadBeamStatus() {
        return _videoState.BeamTiming.ReadStatus(_clock.ElapsedTimeMs);
    }

    /// <inheritdoc />
    public override ushort ReadWord(ushort port) {
        byte value = ReadByte(port);

        if (_loggerService.IsEnabled(LogLevel.Debug)) {
            _loggerService.LogDebug("Returning byte {Byte} for ReadWord() on port {Port}", value, port);
        }

        return value;
    }

    /// <inheritdoc />
    public override uint ReadDWord(ushort port) {
        byte value = ReadByte(port);

        if (_loggerService.IsEnabled(LogLevel.Debug)) {
            _loggerService.LogDebug("Returning byte {Byte} for ReadDWord() on port {Port}", value, port);
        }

        return value;
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        _videoState.IsRenderingDirty = true;
        if (_tandy != null) {
            WriteTandy(port, value);
            return;
        }
        switch (port) {
            case Ports.DacAddressReadIndex:
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Write to DacAddressReadIndex: {Value}", port, value);
                }
                _dacRegisters.IndexRegisterReadMode = value;
                break;

            case Ports.DacAddressWriteIndex:
                if (_loggerService.IsEnabled(LogLevel.Trace)) {
                    _loggerService.LogTrace("[{Port:X4}] Write to DacAddressWriteIndex: {Value}", port, value);
                }
                _dacRegisters.IndexRegisterWriteMode = value;
                break;

            case Ports.DacData:
                if (_loggerService.IsEnabled(LogLevel.Trace)) {
                    _loggerService.LogTrace("[{Port:X4}] Write to DacData: {Value:X2}", port, value);
                }
                _dacRegisters.DataRegister = value;
                if (_dacRegisters.TripletJustCompleted) {
                    _dacRegisters.RebuildAttributeMap(_attributeRegisters);
                }
                break;

            case Ports.DacPelMask:
                if (_loggerService.IsEnabled(LogLevel.Trace)) {
                    _loggerService.LogTrace("[{Port:X4}] Write to DacPelMask: {Value:X2}", port, value);
                }
                _dacRegisters.PixelMask = value;  // setter calls RebuildAllPaletteMap internally
                _dacRegisters.RebuildAttributeMap(_attributeRegisters);
                break;

            case Ports.GraphicsControllerAddress:
                _graphicsRegisters.AddressRegister = (GraphicsControllerRegister)value;
                if (_loggerService.IsEnabled(LogLevel.Trace)) {
                    _loggerService.LogTrace("[{Port:X4}] Write to GraphicsControllerAddress: {Value:X2} {Register}", port, value, _graphicsRegisters.AddressRegister);
                }
                break;

            case Ports.GraphicsControllerData:
                if (_graphicsRegisters.AddressRegister is GraphicsControllerRegister.ReadMapSelect or GraphicsControllerRegister.BitMask or GraphicsControllerRegister.GraphicsMode or GraphicsControllerRegister.EnableSetReset or GraphicsControllerRegister.SetReset) {
                    if (_loggerService.IsEnabled(LogLevel.Trace)) {
                        _loggerService.LogTrace("[{Port:X4}] Write to Graphics register {Register}: {Value:X2} {Explained}", port, _graphicsRegisters.AddressRegister, value, _graphicsRegisters.AddressRegister.Explain(value));
                    }
                } else if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Write to Graphics register {Register}: {Value:X2} {Explained}", port, _graphicsRegisters.AddressRegister, value, _graphicsRegisters.AddressRegister.Explain(value));
                }
                _graphicsRegisters.Write(_graphicsRegisters.AddressRegister, value);
                break;

            case Ports.SequencerAddress:
                _sequencerRegisters.Address = (SequencerRegister)value;
                if (_loggerService.IsEnabled(LogLevel.Trace)) {
                    _loggerService.LogTrace("[{Port:X4}] Write to SequencerAddress: {Value:X2} {Register}", port, value, _sequencerRegisters.Address);
                }
                break;

            case Ports.SequencerData:
                if (_sequencerRegisters.Address == SequencerRegister.PlaneMask) {
                    if (_loggerService.IsEnabled(LogLevel.Trace)) {
                        _loggerService.LogTrace("[{Port:X4}] Write to Sequencer register {Register}: {Value:X2} {Explained}", port, _sequencerRegisters.Address, value, _sequencerRegisters.Address.Explain(value));
                    }
                } else if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Write to Sequencer register {Register}: {Value:X2} {Explained}", port, _sequencerRegisters.Address, value, _sequencerRegisters.Address.Explain(value));
                }
                _sequencerRegisters.WriteRegister(_sequencerRegisters.Address, value);
                if (_sequencerRegisters.Address == SequencerRegister.ClockingMode) {
                    _videoState.BeamTiming.Refresh(_clock.ElapsedTimeMs);
                }
                break;

            case Ports.AttributeAddress:
                if (!_attributeDataMode) {
                    _attributeRegisters.AddressRegister = (AttributeControllerRegister)(value & 0b11111);
                    if (_loggerService.IsEnabled(LogLevel.Trace)) {
                        _loggerService.LogTrace("[{Port:X4}] Write to AttributeAddress: {Value:X2} {Register}", port, value, _attributeRegisters.AddressRegister);
                    }
                } else {
                    if (_loggerService.IsEnabled(LogLevel.Debug)) {
                        _loggerService.LogDebug("[{Port:X4}] Write to Attribute register {Register}: {Value:X2} {Binary}", port, _attributeRegisters.AddressRegister, value, Convert.ToString(value, 2).PadLeft(8, '0'));
                    }
                    _attributeRegisters.WriteRegister(_attributeRegisters.AddressRegister, value);
                    _dacRegisters.RebuildAttributeMap(_attributeRegisters);
                }

                _attributeDataMode = !_attributeDataMode;
                break;

            case Ports.AttributeData:
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Write to Attribute register {Register}: {Value:X2} {Binary}", port, _attributeRegisters.AddressRegister, value, Convert.ToString(value, 2).PadLeft(8, '0'));
                }
                _attributeRegisters.WriteRegister(_attributeRegisters.AddressRegister, value);
                _dacRegisters.RebuildAttributeMap(_attributeRegisters);
                break;

            case Ports.CrtControllerAddress or Ports.CrtControllerAddressAlt or Ports.CrtControllerAddressAltMirror1 or Ports.CrtControllerAddressAltMirror2:
                _crtRegisters.AddressRegister = (CrtControllerRegister)value;
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Write to CrtControllerAddress: {Value:X2} {Register}", port, value, _crtRegisters.AddressRegister);
                }
                break;

            case Ports.CrtControllerData or Ports.CrtControllerDataAlt or Ports.CrtControllerDataAltMirror1 or Ports.CrtControllerDataAltMirror2:
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Write to CRT register {Register}: {Value:X2} {Explained}", port, _crtRegisters.AddressRegister, value, _crtRegisters.AddressRegister.Explain(value));
                }
                if (_crtRegisters.AddressRegister == CrtControllerRegister.StartAddressHigh && value != _crtRegisters.ScreenStartAddressHigh) {
                    _loggerService.LogTrace("Setting Video display to 0x{Value:X4}", (_graphicsRegisters.MiscellaneousGraphicsRegister.BaseAddress + _crtRegisters.ScreenStartAddress) >> 4);
                }
                _crtRegisters.WriteRegister(_crtRegisters.AddressRegister, value);
                _videoState.BeamTiming.Refresh(_clock.ElapsedTimeMs);
                break;
            case Ports.MiscOutputWrite:
                _generalRegisters.MiscellaneousOutput.Value = value;
                _videoState.BeamTiming.Refresh(_clock.ElapsedTimeMs);
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Write to MiscOutputWrite: {Value:X2} {@Explained}", port, value, _generalRegisters.MiscellaneousOutput);
                }
                break;
            case Ports.CgaModeControl:
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Write to CgaModeControl: {Value:X2}", port, value);
                }
                _generalRegisters.CgaModeControl = value;
                UpdateCgaPalette();
                break;
            case Ports.CgaColorSelect:
                if (_loggerService.IsEnabled(LogLevel.Debug)) {
                    _loggerService.LogDebug("[{Port:X4}] Write to CgaColorSelect: {Value:X2}", port, value);
                }
                _generalRegisters.CgaColorSelect = value;
                UpdateCgaPalette();
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }

    private void UpdateCgaPalette() {
        if (_generalRegisters.CgaBitsPerPixel == 0) {
            return;
        }

        byte colorSelect = _generalRegisters.CgaColorSelect;
        byte[] palette = _attributeRegisters.InternalPalette;
        if (_generalRegisters.CgaBitsPerPixel == 1) {
            palette[0] = 0;
            palette[1] = ToVgaAttribute(colorSelect);
        } else {
            palette[0] = ToVgaAttribute(colorSelect);
            int intensity = (colorSelect & 0x10) >> 1;
            bool monochrome = (_generalRegisters.CgaModeControl & 0x04) != 0;
            int paletteSelect = (colorSelect & 0x20) >> 5;
            for (int i = 1; i < 4; i++) {
                int color = i * 2 + paletteSelect;
                if (monochrome) {
                    color = i * 2 + (i & 1);
                }
                palette[i] = ToVgaAttribute((byte)(color | intensity));
            }
        }
        _attributeRegisters.OverscanColor = palette[0];
        _dacRegisters.RebuildAttributeMap(_attributeRegisters);
    }

    private static byte ToVgaAttribute(byte color) {
        return (byte)((color & 0x07) | ((color & 0x08) << 1));
    }

    /// <summary>
    ///     Special shortcut for VGA controller to select a register and write a value in a single call.
    /// </summary>
    public override void WriteWord(ushort port, ushort value) {
        WriteByte(port, (byte)(value & 0xFF));
        WriteByte((ushort)(port + 1), (byte)(value >> 8));
    }

    private void WriteTandy(ushort port, byte value) {
        if (_tandy == null) {
            return;
        }
        if (port is >= 0x3D0 and <= 0x3D7) {
            if ((port & 1) == 0) {
                _tandy.CrtcIndex = value;
            } else {
                _tandy.WriteCrtc(value);
            }
        } else {
            switch (port) {
                case 0x3D8:
                    _tandy.WriteMode(value);
                    break;
                case 0x3D9:
                    _tandy.ColorSelect = value;
                    break;
                case 0x3DA:
                    _tandy.RegisterIndex = value;
                    break;
                case 0x3DB:
                    _tandy.LightPenTriggered = false;
                    break;
                case 0x3DC:
                    if (!_tandy.LightPenTriggered) {
                        _tandy.LightPenTriggered = true;
                        TandyVideoState.ScanoutParameters scanout = _tandy.Scanout;
                        int addressAdd = scanout.Width / 4;
                        if (scanout.IsGraphics) {
                            addressAdd = scanout.Width * scanout.BitsPerPixel / 8;
                        }
                        ushort address = _videoState.BeamTiming.GetLightPenAddress(_clock.ElapsedTimeMs, addressAdd);
                        _tandy.Crtc[0x10] = (byte)(address >> 8);
                        _tandy.Crtc[0x11] = (byte)address;
                    }
                    break;
                case 0x3DE:
                    _tandy.WriteRegister(value);
                    break;
                case 0x3DF:
                    _tandy.WritePageBank(value);
                    break;
            }
        }
        _videoState.BeamTiming.Refresh(_clock.ElapsedTimeMs);
    }
}