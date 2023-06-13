namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Data;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Utils;

internal class VgaFunctions {
    private readonly IIOPortHandler _ioPortDispatcher;
    private readonly Memory _memory;

    public VgaFunctions(Memory memory, IIOPortHandler ioPortDispatcher) {
        _memory = memory;
        _ioPortDispatcher = ioPortDispatcher;
        Setup();
    }

    public void SetCursorPosition(ushort address) {
        VgaPort port = GetCrtControllerAddress();
        address /= 2; // Assume we're in text mode.
        WriteToCrtController(port, 0x0E, (byte)(address >> 8));
        WriteToCrtController(port, 0x0F, (byte)address);
    }

    public void planar4_plane(int plane) {
        if (plane < 0) {
            // Return to default mode (read plane0, write all planes)
            WriteToSequencer(0x02, 0x0F);
            WriteToGraphicsController(0x04, 0);
        } else {
            WriteToSequencer(0x02, (byte)(1 << plane));
            WriteToGraphicsController(0x04, (byte)plane);
        }
    }

    public int GetLineLength(VgaMode vgaMode) {
        byte value = ReadCrtController(GetCrtControllerAddress(), 0x13);
        return value * 8 / GetVramRatio(vgaMode);
    }

    private int GetVramRatio(VgaMode vgaMode) {
        return vgaMode.MemoryModel switch {
            MemoryModel.Text => 2,
            MemoryModel.Cga => 4 / vgaMode.BitsPerPixel,
            MemoryModel.Planar => 4,
            _ => 1
        };
    }

    private byte ReadCrtController(VgaPort port, byte index) {
        WriteByteToIoPort(index, port);
        return ReadByteFromIoPort(port + 1);
    }

    public void SetPalette(byte paletteId) {
        for (byte i = 1; i < 4; i++) {
            WriteMaskedToAttributeController(i, 0x01, (byte)(paletteId & 0x01));
        }
    }

    public void SetBorderColor(byte color) {
        byte value = (byte)(color & 0x0F);
        if ((value & 0x08) != 0) {
            value += 0x08;
        }
        WriteToAttributeController(0x00, value);

        byte i;
        for (i = 1; i < 4; i++) {
            WriteMaskedToAttributeController(i, 0x10, (byte)(color & 0x10));
        }
    }

    public void SetCursorShape(ushort cursorType) {
        VgaPort port = GetCrtControllerAddress();
        WriteToCrtController(port, 0x0a, (byte)(cursorType >> 8));
        WriteToCrtController(port, 0x0b, (byte)cursorType);
    }

    private void Setup() {
        // switch to color mode and enable CPU access 480 lines
        WriteToMiscellaneousOutput(0xC3);
        // more than 64k 3C4/04
        WriteToSequencer(0x04, 0x02);
    }

    public VgaPort GetCrtControllerAddress() {
        return (ReadMiscellaneousOutput() & 1) != 0
            ? VgaPort.CrtControllerAddressAlt
            : VgaPort.CrtControllerAddress;
    }

    public void LoadFont(byte[] fontBytes, ushort count, ushort start, byte destinationFlags, byte fontSize) {
        GetFontAccess();
        ushort blockAddress = (ushort)(((destinationFlags & 0x03) << 14) + ((destinationFlags & 0x04) << 11));
        ushort destination = (ushort)(blockAddress + start * 32);
        for (ushort i = 0; i < count; i++) {
            uint address = MemoryUtils.ToPhysicalAddress(VgaConstants.GraphicsSegment, (ushort)(destination + i * 32));
            var value = new Span<byte>(fontBytes, i * fontSize, fontSize);
            _memory.LoadData(address, value.ToArray(), fontSize);
        }
        ReleaseFontAccess();
    }

    private void ReleaseFontAccess() {
        WriteToSequencer(0x00, 0x01);
        WriteToSequencer(0x02, 0x03);
        WriteToSequencer(0x04, 0x03);
        WriteToSequencer(0x00, 0x03);
        byte value = (byte)((ReadMiscellaneousOutput() & 0x01) != 0 ? 0x0e : 0x0a);
        WriteToGraphicsController(0x06, value);
        WriteToGraphicsController(0x04, 0x00);
        WriteToGraphicsController(0x05, 0x10);
    }

    private byte ReadMiscellaneousOutput() {
        return ReadByteFromIoPort(VgaPort.MiscOutputRead);
    }

    private void GetFontAccess() {
        WriteToSequencer(0x00, 0x01);
        WriteToSequencer(0x02, 0x04);
        WriteToSequencer(0x04, 0x07);
        WriteToSequencer(0x00, 0x03);
        WriteToGraphicsController(0x04, 0x02);
        WriteToGraphicsController(0x05, 0x00);
        WriteToGraphicsController(0x06, 0x04);
    }

    private void WriteToMiscellaneousOutput(byte value) {
        WriteByteToIoPort(value, VgaPort.MiscOutputWrite);
    }

    private void WriteToCrtController(VgaPort port, byte index, byte value) {
        WriteWordToIoPort((ushort)(value << 8 | index), port);
    }

    private void WriteToGraphicsController(byte index, byte value) {
        WriteWordToIoPort((ushort)(value << 8 | index), VgaPort.GraphicsControllerAddress);
    }

    private void WriteToSequencer(byte index, byte value) {
        WriteWordToIoPort((ushort)(value << 8 | index), VgaPort.SequencerAddress);
    }

    private void WriteWordToIoPort(ushort value, VgaPort port) {
        _ioPortDispatcher.WriteWord((int)port, value);
    }

    private void WriteToAttributeController(byte index, byte value) {
        ReadByteFromIoPort(VgaPort.InputStatus1ReadAlt);
        byte orig = ReadByteFromIoPort(VgaPort.AttributeAddress);
        WriteByteToIoPort(index, VgaPort.AttributeAddress);
        WriteByteToIoPort(value, VgaPort.AttributeAddress);
        WriteByteToIoPort(orig, VgaPort.AttributeAddress);
    }

    public void PerformGrayScaleSumming(byte start, int count) {
        SetAttributeControllerIndex(0x00);
        for (byte i = start; i < start + count; i++) {
            byte[] rgb = ReadFromDac(i, 1);
            // intensity = ( 0.3 * Red ) + ( 0.59 * Green ) + ( 0.11 * Blue )
            ushort intensity = (ushort)(77 * rgb[0] + 151 * rgb[1] + 28 * rgb[2] + 0x80 >> 8);
            if (intensity > 0x3F) {
                intensity = 0x3F;
            }
            rgb[0] = rgb[1] = rgb[2] = (byte)intensity;

            WriteToDac(rgb, i, 1);
        }
        SetAttributeControllerIndex(0x20);
    }

    public byte[] ReadFromDac(byte start, int count) {
        byte[] result = new byte[3 * count];
        WriteByteToIoPort(start, VgaPort.DacAddressReadIndex);
        for (int i = 0; i < result.Length; i++) {
            result[i] = ReadByteFromIoPort(VgaPort.DacData);
        }
        return result;
    }

    private void SetAttributeControllerIndex(byte value) {
        ReadByteFromIoPort(VgaPort.InputStatus1ReadAlt);
        WriteByteToIoPort(value, VgaPort.AttributeAddress);
    }

    private byte ReadByteFromIoPort(VgaPort port) {
        return _ioPortDispatcher.ReadByte((int)port);
    }

    public void WriteToDac(IReadOnlyList<byte> palette, byte startIndex, int count) {
        WriteByteToIoPort(startIndex, VgaPort.DacAddressWriteIndex);
        int i = 0;
        while (count > 0) {
            WriteByteToIoPort(palette[i++], VgaPort.DacData);
            WriteByteToIoPort(palette[i++], VgaPort.DacData);
            WriteByteToIoPort(palette[i++], VgaPort.DacData);
            count--;
        }
    }

    private void WriteByteToIoPort(byte value, VgaPort port) {
        _ioPortDispatcher.WriteByte((int)port, value);
    }

    public void WriteToPixelMask(byte value) {
        WriteByteToIoPort(value, VgaPort.DacPelMask);
    }

    public void ReadVideoDacState(out byte pMode, out byte curPage) {
        pMode = (byte)(ReadAttributeController(0x10) >> 7);
        curPage = (byte)(ReadAttributeController(0x14) & 0x0F);
        if ((pMode & 0x01) == 0) {
            curPage >>= 2;
        }
    }

    public byte ReadPixelMask() {
        return ReadByteFromIoPort(VgaPort.DacPelMask);
    }

    public void ReadFromDac(ushort segment, ushort offset, byte start, ushort count) {
        WriteByteToIoPort(start, VgaPort.DacAddressReadIndex);
        while (count > 0) {
            _memory.UInt8[segment, offset++] = ReadByteFromIoPort(VgaPort.DacData);
            _memory.UInt8[segment, offset++] = ReadByteFromIoPort(VgaPort.DacData);
            _memory.UInt8[segment, offset++] = ReadByteFromIoPort(VgaPort.DacData);
            count--;
        }
    }

    public void SelectVideoDacColorPage(byte flag, byte data) {
        if ((flag & 0x01) == 0) {
            // select paging mode
            WriteMaskedToAttributeController(0x10, 0x80, (byte)(data << 7));
            return;
        }
        // select page
        byte val = ReadAttributeController(0x10);
        if ((val & 0x80) == 0) {
            data <<= 2;
        }
        data &= 0x0f;
        WriteToAttributeController(0x14, data);
    }

    public void WriteToDac(ushort segment, ushort offset, byte startIndex, ushort count) {
        byte[] rgb = _memory.GetData(MemoryUtils.ToPhysicalAddress(segment, offset), (uint)(3 * count));
        WriteToDac(rgb, startIndex, count);
    }

    public void GetAllPaletteRegisters(ushort segment, ushort offset) {
        for (byte i = 0; i < 0x10; i++) {
            _memory.UInt8[segment, offset++] = ReadAttributeController(i);
        }
        _memory.UInt8[segment, offset] = ReadAttributeController(0x11);
    }

    public byte GetOverscanBorderColor() {
        return ReadAttributeController(0x11);
    }

    public byte ReadAttributeController(byte index) {
        ReadByteFromIoPort(VgaPort.InputStatus1ReadAlt);
        byte original = ReadByteFromIoPort(VgaPort.AttributeAddress);
        WriteByteToIoPort(index, VgaPort.AttributeAddress);
        byte value = ReadByteFromIoPort(VgaPort.AttributeData);
        ReadByteFromIoPort(VgaPort.InputStatus1ReadAlt);
        WriteByteToIoPort(original, VgaPort.AttributeAddress);
        return value;
    }

    public void ToggleIntensity(byte flag) {
        WriteMaskedToAttributeController(0x10, 0x08, (byte)((flag & 0x01) << 3));
    }

    private void WriteMaskedToAttributeController(byte index, byte offBits, byte onBits) {
        ReadByteFromIoPort(VgaPort.InputStatus1ReadAlt);
        byte original = ReadByteFromIoPort(VgaPort.AttributeAddress);
        WriteByteToIoPort(index, VgaPort.AttributeAddress);
        byte value = ReadByteFromIoPort(VgaPort.AttributeData);
        WriteByteToIoPort((byte)(value & ~offBits | onBits), VgaPort.AttributeAddress);
        WriteByteToIoPort(original, VgaPort.AttributeAddress);
    }

    public void SetAllPaletteRegisters(ushort segment, ushort offset) {
        for (byte i = 0; i < 0x10; i++) {
            WriteToAttributeController(i, _memory.UInt8[segment, offset++]);
        }
        WriteToAttributeController(0x11, _memory.UInt8[segment, offset]);
    }

    public void SetOverscanBorderColor(byte color) {
        WriteToAttributeController(0x11, color);
    }

    public void SetEgaPaletteRegister(byte register, byte value) {
        if (register > 0x14) {
            return;
        }
        WriteToAttributeController(register, value);
    }

    public void SetTextBlockSpecifier(byte spec) {
        WriteToSequencer(0x03, spec);
    }

    public void SetScanLines(byte lines) {
        WriteMaskedToCrtController(GetCrtControllerAddress(), 0x09, 0x1F, lines - 1);
    }

    private void WriteMaskedToCrtController(VgaPort port, byte index, int offBits, int onBits) {
        WriteByteToIoPort(index, port);
        byte value = ReadByteFromIoPort(port + 1);
        WriteByteToIoPort((byte)(value & ~offBits | onBits), port + 1);
    }

    public ushort get_vde() {
        VgaPort port = GetCrtControllerAddress();
        ushort vde = ReadCrtController(port, 0x12);
        byte ovl = ReadCrtController(port, 0x07);
        vde = (ushort)(vde + ((ovl & 0x02) << 7) + ((ovl & 0x40) << 3) + 1);
        return vde;
    }

    public void SetDisplayStart(VgaMode vgaMode, int value) {
        VgaPort port = GetCrtControllerAddress();
        value = value * GetVramRatio(vgaMode) / 4;
        WriteToCrtController(port, 0x0C, (byte)(value >> 8));
        WriteToCrtController(port, 0x0D, (byte)value);
    }

    public int GetDisplaystart(VgaMode vgaMode) {
        VgaPort port = GetCrtControllerAddress();
        int address = ReadCrtController(port, 0x0C) << 8 | ReadCrtController(port, 0x0D);
        return address * 4 / GetVramRatio(vgaMode);
    }

    public void SetMode(VideoMode videoMode, ModeFlags flags) {
        VgaMode vgaMode = videoMode.VgaMode;

        // if palette loading (bit 3 of modeset ctl = 0)
        if (!flags.HasFlag(ModeFlags.NoPalette)) {
            // Set the PEL mask
            WriteToPixelMask(videoMode.PixelMask);

            // From which palette
            byte[] palette = videoMode.Palette;
            int paletteEntryCount = videoMode.Palette.Length / 3;

            // Always 256*3 values
            WriteToDac(palette, 0, paletteEntryCount);
            int remainingEntryCount = 256 - paletteEntryCount;
            byte[] empty = new byte[remainingEntryCount * 3];
            WriteToDac(empty, (byte)paletteEntryCount, remainingEntryCount);

            if (flags.HasFlag(ModeFlags.GraySum)) {
                PerformGrayScaleSumming(0x00, 0x100);
            }
        }

        // Set Attribute Ctl
        for (byte i = 0; i < videoMode.AttributeControllerRegisterValues.Length; i++) {
            WriteToAttributeController(i, videoMode.AttributeControllerRegisterValues[i]);
        }

        // Set Sequencer Ctl
        for (byte i = 0; i < videoMode.SequencerRegisterValues.Length; i++) {
            WriteToSequencer(i, videoMode.SequencerRegisterValues[i]);
        }

        // Set Grafx Ctl
        for (byte i = 0; i < videoMode.GraphicsControllerRegisterValues.Length; i++) {
            WriteToGraphicsController(i, videoMode.GraphicsControllerRegisterValues[i]);
        }

        // Set CRTC address VGA or MDA
        byte miscellaneousRegisterValue = videoMode.MiscellaneousRegisterValue;
        VgaPort crtControllerPort = (miscellaneousRegisterValue & 1) == 0
            ? VgaPort.CrtControllerAddress
            : VgaPort.CrtControllerAddressAlt;

        // Disable CRTC write protection
        WriteToCrtController(crtControllerPort, 0x11, 0x00);
        // Set CRTC regs
        for (byte i = 0; i <= 0x18; i++) {
            WriteToCrtController(crtControllerPort, i, videoMode.CrtControllerRegisterValues[i]);
        }

        // Set the misc register
        WriteToMiscellaneousOutput(miscellaneousRegisterValue);

        // Enable video
        SetAttributeControllerIndex(0x20);

        // Clear screen
        if (!flags.HasFlag(ModeFlags.NoClearMem)) {
            ClearScreen(vgaMode);
        }

        // Write the fonts in memory
        if (vgaMode.MemoryModel == MemoryModel.Text) {
            switch (vgaMode.CharacterHeight) {
                case 14:
                    LoadFont(Fonts.VgaFont14, 0x100, 0, 0, 14);
                    break;
                case 16:
                    LoadFont(Fonts.VgaFont16, 0x100, 0, 0, 16);
                    break;
                default:
                    LoadFont(Fonts.VgaFont8, 0x100, 0, 0, 8);
                    break;
            }
        }
    }

    private void ClearScreen(VgaMode vgaMode) {
        switch (vgaMode.MemoryModel) {
            case MemoryModel.Text:
                MemSet16(vgaMode.StartSegment, 0, 0x0720, 32 * 1024);
                break;
            case MemoryModel.Cga:
                MemSet16(vgaMode.StartSegment, 0, 0x0000, 32 * 1024);
                break;
            case MemoryModel.Planar:
            case MemoryModel.Packed:
            default:
                MemSet16(vgaMode.StartSegment, 0, 0x0000, 64 * 1024);
                break;
        }
    }

    public void MemSet16(ushort segment, ushort offset, ushort value, int amount) {
        amount /= 2;
        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
        for (int i = 0; i < amount; i += 2) {
            _memory.SetUint16((uint)(address + i), value);
        }
    }

    public void EnableVideoAddressing(byte disable) {
        byte value = (byte)((disable & 1) != 0 ? 0x00 : 0x02);
        WriteMaskedToMiscellaneousRegister(0x02, value);
    }

    private void WriteMaskedToMiscellaneousRegister(byte offBits, byte onBits) {
        WriteToMiscellaneousOutput((byte)(ReadMiscellaneousOutput() & ~offBits | onBits));
    }
}