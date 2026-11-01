namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Typed wrappers for the VGA I/O ports registered by <see cref="VgaIoPortHandler"/>. Each method is a
/// thin call to <see cref="IOPortDispatcher"/> at the port of <see cref="Ports"/> it wraps.
/// </summary>
public sealed class VgaPorts {
    private readonly IOPortDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="VgaPorts"/> class.
    /// </summary>
    /// <param name="dispatcher">The I/O port dispatcher the wrappers call.</param>
    public VgaPorts(IOPortDispatcher dispatcher) {
        _dispatcher = dispatcher;
    }

    /// <summary>Reads the attribute controller address register (0x3C0).</summary>
    [PortWrapper(Ports.AttributeAddress, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadAttributeAddress() => _dispatcher.ReadByte(Ports.AttributeAddress);

    /// <summary>Writes the attribute controller index or data (0x3C0), alternating with each write.</summary>
    [PortWrapper(Ports.AttributeAddress, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteAttributeAddress(byte value) => _dispatcher.WriteByte(Ports.AttributeAddress, value);

    /// <summary>Reads the attribute controller data register (0x3C1).</summary>
    [PortWrapper(Ports.AttributeData, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadAttributeData() => _dispatcher.ReadByte(Ports.AttributeData);

    /// <summary>Writes the attribute controller data register (0x3C1).</summary>
    [PortWrapper(Ports.AttributeData, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteAttributeData(byte value) => _dispatcher.WriteByte(Ports.AttributeData, value);

    /// <summary>Reads the input status 0 register (0x3C2).</summary>
    [PortWrapper(Ports.InputStatus0Read, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadInputStatus0() => _dispatcher.ReadByte(Ports.InputStatus0Read);

    /// <summary>Writes the miscellaneous output register (0x3C2).</summary>
    [PortWrapper(Ports.MiscOutputWrite, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteMiscOutput(byte value) => _dispatcher.WriteByte(Ports.MiscOutputWrite, value);

    /// <summary>Reads the sequencer index register (0x3C4).</summary>
    [PortWrapper(Ports.SequencerAddress, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadSequencerAddress() => _dispatcher.ReadByte(Ports.SequencerAddress);

    /// <summary>Writes the sequencer index register (0x3C4).</summary>
    [PortWrapper(Ports.SequencerAddress, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSequencerAddress(byte value) => _dispatcher.WriteByte(Ports.SequencerAddress, value);

    /// <summary>Reads the sequencer data register (0x3C5).</summary>
    [PortWrapper(Ports.SequencerData, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadSequencerData() => _dispatcher.ReadByte(Ports.SequencerData);

    /// <summary>Writes the sequencer data register (0x3C5).</summary>
    [PortWrapper(Ports.SequencerData, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteSequencerData(byte value) => _dispatcher.WriteByte(Ports.SequencerData, value);

    /// <summary>Writes the sequencer index and data registers in one word access (0x3C4).</summary>
    [PortWrapper(Ports.SequencerAddress, BitWidth.WORD_16, PortDirection.Out)]
    public void WriteSequencerIndexAndData(ushort value) => _dispatcher.WriteWord(Ports.SequencerAddress, value);

    /// <summary>Reads the DAC pixel mask register (0x3C6).</summary>
    [PortWrapper(Ports.DacPelMask, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadDacPelMask() => _dispatcher.ReadByte(Ports.DacPelMask);

    /// <summary>Writes the DAC pixel mask register (0x3C6).</summary>
    [PortWrapper(Ports.DacPelMask, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteDacPelMask(byte value) => _dispatcher.WriteByte(Ports.DacPelMask, value);

    /// <summary>Reads the DAC state register (0x3C7).</summary>
    [PortWrapper(Ports.DacStateRead, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadDacState() => _dispatcher.ReadByte(Ports.DacStateRead);

    /// <summary>Writes the index of the first DAC entry to be read (0x3C7).</summary>
    [PortWrapper(Ports.DacAddressReadIndex, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteDacReadIndex(byte value) => _dispatcher.WriteByte(Ports.DacAddressReadIndex, value);

    /// <summary>Reads the index of the first DAC entry to be written (0x3C8).</summary>
    [PortWrapper(Ports.DacAddressWriteIndex, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadDacWriteIndex() => _dispatcher.ReadByte(Ports.DacAddressWriteIndex);

    /// <summary>Writes the index of the first DAC entry to be written (0x3C8).</summary>
    [PortWrapper(Ports.DacAddressWriteIndex, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteDacWriteIndex(byte value) => _dispatcher.WriteByte(Ports.DacAddressWriteIndex, value);

    /// <summary>Reads the DAC data register (0x3C9).</summary>
    [PortWrapper(Ports.DacData, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadDacData() => _dispatcher.ReadByte(Ports.DacData);

    /// <summary>Writes the DAC data register (0x3C9).</summary>
    [PortWrapper(Ports.DacData, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteDacData(byte value) => _dispatcher.WriteByte(Ports.DacData, value);

    /// <summary>Reads the feature control register (0x3CA).</summary>
    [PortWrapper(Ports.FeatureControlRead, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadFeatureControl() => _dispatcher.ReadByte(Ports.FeatureControlRead);

    /// <summary>Reads the miscellaneous output register (0x3CC).</summary>
    [PortWrapper(Ports.MiscOutputRead, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadMiscOutput() => _dispatcher.ReadByte(Ports.MiscOutputRead);

    /// <summary>Reads the graphics controller index register (0x3CE).</summary>
    [PortWrapper(Ports.GraphicsControllerAddress, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadGraphicsControllerAddress() => _dispatcher.ReadByte(Ports.GraphicsControllerAddress);

    /// <summary>Writes the graphics controller index register (0x3CE).</summary>
    [PortWrapper(Ports.GraphicsControllerAddress, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteGraphicsControllerAddress(byte value) => _dispatcher.WriteByte(Ports.GraphicsControllerAddress, value);

    /// <summary>Reads the graphics controller data register (0x3CF).</summary>
    [PortWrapper(Ports.GraphicsControllerData, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadGraphicsControllerData() => _dispatcher.ReadByte(Ports.GraphicsControllerData);

    /// <summary>Writes the graphics controller data register (0x3CF).</summary>
    [PortWrapper(Ports.GraphicsControllerData, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteGraphicsControllerData(byte value) => _dispatcher.WriteByte(Ports.GraphicsControllerData, value);

    /// <summary>Writes the graphics controller index and data registers in one word access (0x3CE).</summary>
    [PortWrapper(Ports.GraphicsControllerAddress, BitWidth.WORD_16, PortDirection.Out)]
    public void WriteGraphicsControllerIndexAndData(ushort value) => _dispatcher.WriteWord(Ports.GraphicsControllerAddress, value);

    /// <summary>Reads the CRT controller index register (0x3B4).</summary>
    [PortWrapper(Ports.CrtControllerAddress, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadCrtControllerAddress() => _dispatcher.ReadByte(Ports.CrtControllerAddress);

    /// <summary>Writes the CRT controller index register (0x3B4).</summary>
    [PortWrapper(Ports.CrtControllerAddress, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteCrtControllerAddress(byte value) => _dispatcher.WriteByte(Ports.CrtControllerAddress, value);

    /// <summary>Reads the CRT controller data register (0x3B5).</summary>
    [PortWrapper(Ports.CrtControllerData, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadCrtControllerData() => _dispatcher.ReadByte(Ports.CrtControllerData);

    /// <summary>Writes the CRT controller data register (0x3B5).</summary>
    [PortWrapper(Ports.CrtControllerData, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteCrtControllerData(byte value) => _dispatcher.WriteByte(Ports.CrtControllerData, value);

    /// <summary>Reads the CRT controller index register (0x3D4).</summary>
    [PortWrapper(Ports.CrtControllerAddressAlt, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadCrtControllerAddressAlt() => _dispatcher.ReadByte(Ports.CrtControllerAddressAlt);

    /// <summary>Writes the CRT controller index register (0x3D4).</summary>
    [PortWrapper(Ports.CrtControllerAddressAlt, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteCrtControllerAddressAlt(byte value) => _dispatcher.WriteByte(Ports.CrtControllerAddressAlt, value);

    /// <summary>Reads the CRT controller data register (0x3D5).</summary>
    [PortWrapper(Ports.CrtControllerDataAlt, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadCrtControllerDataAlt() => _dispatcher.ReadByte(Ports.CrtControllerDataAlt);

    /// <summary>Writes the CRT controller data register (0x3D5).</summary>
    [PortWrapper(Ports.CrtControllerDataAlt, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteCrtControllerDataAlt(byte value) => _dispatcher.WriteByte(Ports.CrtControllerDataAlt, value);

    /// <summary>Writes the CRT controller index and data registers in one word access (0x3D4).</summary>
    [PortWrapper(Ports.CrtControllerAddressAlt, BitWidth.WORD_16, PortDirection.Out)]
    public void WriteCrtControllerIndexAndData(ushort value) => _dispatcher.WriteWord(Ports.CrtControllerAddressAlt, value);

    /// <summary>Reads the CRT controller index register mirror (0x3D0).</summary>
    [PortWrapper(Ports.CrtControllerAddressAltMirror1, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadCrtControllerAddressAltMirror1() => _dispatcher.ReadByte(Ports.CrtControllerAddressAltMirror1);

    /// <summary>Writes the CRT controller index register mirror (0x3D0).</summary>
    [PortWrapper(Ports.CrtControllerAddressAltMirror1, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteCrtControllerAddressAltMirror1(byte value) => _dispatcher.WriteByte(Ports.CrtControllerAddressAltMirror1, value);

    /// <summary>Reads the CRT controller data register mirror (0x3D1).</summary>
    [PortWrapper(Ports.CrtControllerDataAltMirror1, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadCrtControllerDataAltMirror1() => _dispatcher.ReadByte(Ports.CrtControllerDataAltMirror1);

    /// <summary>Writes the CRT controller data register mirror (0x3D1).</summary>
    [PortWrapper(Ports.CrtControllerDataAltMirror1, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteCrtControllerDataAltMirror1(byte value) => _dispatcher.WriteByte(Ports.CrtControllerDataAltMirror1, value);

    /// <summary>Reads the CRT controller index register mirror (0x3D2).</summary>
    [PortWrapper(Ports.CrtControllerAddressAltMirror2, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadCrtControllerAddressAltMirror2() => _dispatcher.ReadByte(Ports.CrtControllerAddressAltMirror2);

    /// <summary>Writes the CRT controller index register mirror (0x3D2).</summary>
    [PortWrapper(Ports.CrtControllerAddressAltMirror2, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteCrtControllerAddressAltMirror2(byte value) => _dispatcher.WriteByte(Ports.CrtControllerAddressAltMirror2, value);

    /// <summary>Reads the CRT controller data register mirror (0x3D3).</summary>
    [PortWrapper(Ports.CrtControllerDataAltMirror2, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadCrtControllerDataAltMirror2() => _dispatcher.ReadByte(Ports.CrtControllerDataAltMirror2);

    /// <summary>Writes the CRT controller data register mirror (0x3D3).</summary>
    [PortWrapper(Ports.CrtControllerDataAltMirror2, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteCrtControllerDataAltMirror2(byte value) => _dispatcher.WriteByte(Ports.CrtControllerDataAltMirror2, value);

    /// <summary>Reads the input status 1 register at its monochrome address (0x3BA).</summary>
    [PortWrapper(Ports.InputStatus1Read, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadInputStatus1Mono() => _dispatcher.ReadByte(Ports.InputStatus1Read);

    /// <summary>Reads the input status 1 register (0x3DA).</summary>
    [PortWrapper(Ports.InputStatus1ReadAlt, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadInputStatus1() => _dispatcher.ReadByte(Ports.InputStatus1ReadAlt);

    /// <summary>Reads the CGA mode control register (0x3D8).</summary>
    [PortWrapper(Ports.CgaModeControl, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadCgaModeControl() => _dispatcher.ReadByte(Ports.CgaModeControl);

    /// <summary>Writes the CGA mode control register (0x3D8).</summary>
    [PortWrapper(Ports.CgaModeControl, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteCgaModeControl(byte value) => _dispatcher.WriteByte(Ports.CgaModeControl, value);

    /// <summary>Reads the CGA color select register (0x3D9).</summary>
    [PortWrapper(Ports.CgaColorSelect, BitWidth.BYTE_8, PortDirection.In)]
    public byte ReadCgaColorSelect() => _dispatcher.ReadByte(Ports.CgaColorSelect);

    /// <summary>Writes the CGA color select register (0x3D9).</summary>
    [PortWrapper(Ports.CgaColorSelect, BitWidth.BYTE_8, PortDirection.Out)]
    public void WriteCgaColorSelect(byte value) => _dispatcher.WriteByte(Ports.CgaColorSelect, value);
}
