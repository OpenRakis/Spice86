namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Data;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <inheritdoc cref="IVgaFunctionality" />
public class VgaFunctionality : IVgaFunctionality {
    private const byte DefaultAttribute = 0x07;
    private readonly BiosDataArea _biosDataArea;
    private readonly IIOPortHandler _ioPortDispatcher;
    private readonly IIndexable _memory;
    private readonly VgaRom _vgaRom;
    private readonly InterruptVectorTable _interruptVectorTable;

    /// <summary>
    /// Creates a new instance of the <see cref="VgaFunctionality"/> class.
    /// </summary>
    /// <param name="memory">The emulator memory.</param>
    /// <param name="interruptVectorTable">The class that wraps reads and writes to the interrupt vector table.</param>
    /// <param name="ioPortDispatcher">The IOPortDispatcher, used to read from or write to VGA ports.</param>
    /// <param name="biosDataArea">The global BIOS variables.</param>
    /// <param name="vgaRom">The VGA ROM, so we can access the IBM fonts.</param>
    /// <param name="bootUpInTextMode">Whether we begin with mode 0x03.</param>
    public VgaFunctionality(IIndexable memory, InterruptVectorTable interruptVectorTable, IIOPortHandler ioPortDispatcher, BiosDataArea biosDataArea, VgaRom vgaRom, bool bootUpInTextMode) {
        _memory = memory;
        _ioPortDispatcher = ioPortDispatcher;
        _biosDataArea = biosDataArea;
        _vgaRom = vgaRom;
        _interruptVectorTable = interruptVectorTable;
        if(bootUpInTextMode) {
            VgaSetMode(0x03, ModeFlags.Legacy);
        }
    }

    /// <inheritdoc />
    public void WriteString(string text) {
        CursorPosition cursorPosition = GetCursorPosition(_biosDataArea.CurrentVideoPage);
        foreach (char character in text) {
            CharacterPlusAttribute characterPlusAttribute = new(character, 0x07, true);
            cursorPosition = WriteTeletype(cursorPosition, characterPlusAttribute);
        }

        SetCursorPosition(cursorPosition);
    }

    /// <inheritdoc />
    public void WriteString(ushort segment, ushort offset, ushort length, bool includeAttributes, byte attribute, CursorPosition cursorPosition, bool updateCursorPosition) {
        while (length-- > 0) {
            char character = (char)_memory.UInt8[segment, offset++];
            if (includeAttributes) {
                attribute = _memory.UInt8[segment, offset++];
            }
            CharacterPlusAttribute characterPlusAttribute = new(character, attribute, true);
            cursorPosition = WriteTeletype(cursorPosition, characterPlusAttribute);
        }

        if (updateCursorPosition) {
            SetCursorPosition(cursorPosition);
        }
    }

    /// <inheritdoc />
    public void SetCursorPosition(CursorPosition cursorPosition) {
        if (cursorPosition.Page > 7) {
            // Should not happen...
            return;
        }

        if (cursorPosition.Page == _biosDataArea.CurrentVideoPage) {
            // Update cursor in hardware
            SetCursorPosition(TextAddress(cursorPosition));
        }

        // Update BIOS cursor pos
        int position = cursorPosition.Y << 8 | cursorPosition.X;
        _biosDataArea.CursorPosition[cursorPosition.Page] = (ushort)position;
    }

    /// <inheritdoc />
    public void WriteTextInTeletypeMode(CharacterPlusAttribute characterPlusAttribute) {
        CursorPosition cursorPosition = GetCursorPosition(_biosDataArea.CurrentVideoPage);
        cursorPosition = WriteTeletype(cursorPosition, characterPlusAttribute);
        SetCursorPosition(cursorPosition);
    }

    /// <inheritdoc />
    public void SetPalette(byte paletteId) {
        for (byte i = 1; i < 4; i++) {
            WriteMaskedToAttributeController(i, 0x01, (byte)(paletteId & 0x01));
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void SetCursorShape(ushort cursorType) {
        _biosDataArea.CursorType = cursorType;
        ushort cursorShape = GetCursorShape();
        VgaPort port = GetCrtControllerPort();
        WriteToCrtController(port, 0x0A, (byte)(cursorShape >> 8));
        WriteToCrtController(port, 0x0B, (byte)cursorShape);
    }

    /// <inheritdoc />
    public void WriteCharacterAtCursor(CharacterPlusAttribute character, byte page, int count = 1) {
        CursorPosition cursorPosition = GetCursorPosition(page);
        while (count-- > 0) {
            WriteCharacter(cursorPosition, character);
        }
    }

    /// <inheritdoc />
    public CharacterPlusAttribute ReadChar(CursorPosition cp) {
        VgaMode vgaMode = GetCurrentMode();

        if (vgaMode.MemoryModel != MemoryModel.Text) {
            return ReadGraphicsCharacter(vgaMode, cp);
        }

        ushort offset = TextAddress(cp);
        ushort value = _memory.UInt16[vgaMode.StartSegment, offset];
        return new CharacterPlusAttribute((char)value, (byte)(value >> 8), false);
    }

    /// <inheritdoc />
    public void VerifyScroll(int direction, byte upperLeftX, byte upperLeftY, byte lowerRightX, byte lowerRightY, int lines, byte attribute) {
        // Verify parameters
        ushort numberOfRows = (ushort)(_biosDataArea.ScreenRows + 1);
        if (lowerRightY >= numberOfRows) {
            lowerRightY = (byte)(numberOfRows - 1);
        }
        ushort numberOfColumns = _biosDataArea.ScreenColumns;
        if (lowerRightX >= numberOfColumns) {
            lowerRightX = (byte)(numberOfColumns - 1);
        }
        int width = lowerRightX - upperLeftX + 1;
        int height = lowerRightY - upperLeftY + 1;
        if (width <= 0 || height <= 0) {
            return;
        }

        if (lines >= height) {
            lines = 0;
        }
        lines *= direction;

        // Scroll (or clear) window
        CursorPosition cursorPosition = new(upperLeftX, upperLeftY, _biosDataArea.CurrentVideoPage);
        Area area = new(width, height);
        CharacterPlusAttribute attr = new(' ', attribute, true);
        Scroll(cursorPosition, area, lines, attr);
    }

    /// <inheritdoc />
    public int SetActivePage(byte page) {
        if (page > 7) {
            return 0;
        }
        // Calculate memory address of start of page
        CursorPosition cursorPosition = new(0, 0, page);
        int address = TextAddress(cursorPosition);
        VgaMode vgaMode = GetCurrentMode();
        SetDisplayStart(vgaMode, address);

        // And change the BIOS page
        _biosDataArea.VideoPageStart = (ushort)address;
        _biosDataArea.CurrentVideoPage = page;

        // Display the cursor, now the page is active
        SetCursorPosition(GetCursorPosition(page));

        return address;
    }

    /// <inheritdoc />
    public byte ReadPixel(ushort x, ushort y) {
        VgaMode vgaMode = GetCurrentMode();

        GraphicsOperation operation = CreateGraphicsOperation(vgaMode);
        operation.X = AlignDown(x, 8);
        operation.Y = y;
        operation.MemoryAction = MemoryAction.ReadByte;
        HandleGraphicsOperation(operation);

        return operation.Pixels[x & 0x07];
    }

    /// <inheritdoc />
    public void WritePixel(byte color, ushort x, ushort y) {
        VgaMode vgaMode = GetCurrentMode();

        GraphicsOperation operation = CreateGraphicsOperation(vgaMode);
        operation.X = AlignDown(x, 8);
        operation.Y = y;
        operation.MemoryAction = MemoryAction.ReadByte;
        HandleGraphicsOperation(operation);

        bool useXor = (color & 0x80) != 0 && vgaMode.BitsPerPixel < 8;
        if (useXor) {
            operation.Pixels[x & 0x07] ^= (byte)(color & 0x7f);
        } else {
            operation.Pixels[x & 0x07] = color;
        }
        operation.MemoryAction = MemoryAction.WriteByte;
        HandleGraphicsOperation(operation);
    }

    /// <inheritdoc />
    public void VgaSetMode(int modeId, ModeFlags flags) {
        VideoMode videoMode = GetVideoMode(modeId);
        VgaMode vgaMode = videoMode.VgaMode;

        // if palette loading (bit 3 of modeSet ctl = 0)
        if (!flags.HasFlag(ModeFlags.NoPalette)) {
            LoadPalette(videoMode, flags);
        }

        // Fill the registers with the values belonging to this mode.
        FillRegisters(videoMode);

        // Enable video
        SetAttributeControllerIndex(0x20);

        // Clear screen
        if (!flags.HasFlag(ModeFlags.NoClearMem)) {
            ClearScreen(vgaMode);
        }

        // Write the fonts in memory
        if (vgaMode.MemoryModel == MemoryModel.Text) {
            WriteFonts(vgaMode);
        }

        // Set the BIOS mem
        ushort characterHeight = SetBiosDataArea(modeId, flags, vgaMode);

        // Set the 0x1F and 0x43 interrupt vectors to the font addresses.
        SetInterruptVectorAddress(0x1F, _vgaRom.VgaFont8Address2.Segment, _vgaRom.VgaFont8Address2.Offset);

        SegmentedAddress address;
        switch (characterHeight) {
            case 8:
                address = _vgaRom.VgaFont8Address;
                break;
            case 14:
                address = _vgaRom.VgaFont14Address;
                break;
            case 16:
                address = _vgaRom.VgaFont16Address;
                break;
            default:
                return;
        }
        SetInterruptVectorAddress(0x43, address.Segment, address.Offset);

        VideoModeChanged?.Invoke(this, new VideoModeChangedEventArgs(vgaMode));
    }

    /// <inheritdoc />
    public SegmentedAddress GetFontAddress(byte fontNumber) {
        SegmentedAddress address = fontNumber switch {
            0x00 => GetInterruptVectorAddress(0x1F),
            0x01 => GetInterruptVectorAddress(0x43),
            0x02 => _vgaRom.VgaFont14Address,
            0x03 => _vgaRom.VgaFont8Address,
            0x04 => _vgaRom.VgaFont8Address2,
            0x05 => _vgaRom.VgaFont14Address,
            0x06 => _vgaRom.VgaFont16Address,
            0x07 => _vgaRom.VgaFont16Address,
            _ => throw new NotSupportedException($"{fontNumber} is not a valid font number")
        };
        return address;
    }

    /// <inheritdoc />
    public void CursorEmulation(bool enabled) {
        _biosDataArea.VideoCtl = (byte)(_biosDataArea.VideoCtl & ~0x01 | (enabled ? 1 : 0));
    }

    /// <inheritdoc />
    public void SummingToGrayScales(bool enabled) {
        _biosDataArea.ModesetCtl = (byte)(_biosDataArea.ModesetCtl & ~0x02 | (enabled ? 2 : 0));
    }

    /// <inheritdoc />
    public void DefaultPaletteLoading(bool enabled) {
        _biosDataArea.VideoCtl = (byte)(_biosDataArea.VideoCtl & ~0x08 | (enabled ? 8 : 0));
    }

    /// <inheritdoc />
    public void SelectScanLines(int lines) {
        byte modeSetCtl = _biosDataArea.ModesetCtl;
        byte featureSwitches = _biosDataArea.VideoFeatureSwitches;
        switch (lines) {
            case 200:
                modeSetCtl = (byte)(modeSetCtl & ~0x10 | 0x80);
                featureSwitches = (byte)(featureSwitches & ~0x0F | 0x08);
                break;
            case 350:
                modeSetCtl = (byte)(modeSetCtl & ~0x90);
                featureSwitches = (byte)(featureSwitches & ~0x0f | 0x09);
                break;
            case 400:
                modeSetCtl = (byte)(modeSetCtl & ~0x80 | 0x10);
                featureSwitches = (byte)(featureSwitches & ~0x0f | 0x09);
                break;
            default:
                throw new NotSupportedException($"{lines}  is not a valid scan line amount");
        }
        _biosDataArea.ModesetCtl = modeSetCtl;
        _biosDataArea.VideoFeatureSwitches = featureSwitches;
    }

    /// <inheritdoc />
    public byte GetFeatureSwitches() {
        return (byte)(_biosDataArea.VideoFeatureSwitches & 0x0F);
    }

    /// <inheritdoc />
    public bool GetColorMode() {
        return (VgaPort)_biosDataArea.CrtControllerBaseAddress == VgaPort.CrtControllerAddress;
    }

    /// <inheritdoc />
    public void LoadGraphicsRom8X16Font(byte rowSpecifier, byte userSpecifiedRows) {
        SegmentedAddress address = _vgaRom.VgaFont16Address;
        LoadGraphicsFont(address.Segment, address.Offset, 16, rowSpecifier, userSpecifiedRows);
    }

    /// <inheritdoc />
    public void LoadRom8X8Font(byte rowSpecifier, byte userSpecifiedRows) {
        SegmentedAddress address = _vgaRom.VgaFont8Address;
        LoadGraphicsFont(address.Segment, address.Offset, 8, rowSpecifier, userSpecifiedRows);
    }

    /// <inheritdoc />
    public void LoadRom8X14Font(byte rowSpecifier, byte userSpecifiedRows) {
        SegmentedAddress address = _vgaRom.VgaFont14Address;
        LoadGraphicsFont(address.Segment, address.Offset, 14, rowSpecifier, userSpecifiedRows);
    }

    /// <inheritdoc />
    public void LoadUserGraphicsCharacters(ushort segment, ushort offset, byte height, byte rowSpecifier, byte userSpecifiedRows) {
        LoadGraphicsFont(segment, offset, height, rowSpecifier, userSpecifiedRows);
    }

    /// <inheritdoc />
    public void LoadUserCharacters8X8(ushort segment, ushort offset) {
        SetInterruptVectorAddress(0x1F, segment, offset);
    }

    /// <inheritdoc />
    public void LoadUserFont(ushort segment, ushort offset, ushort length, ushort start, byte fontBlock, byte height) {
        byte[] bytes = _memory.GetData(MemoryUtils.ToPhysicalAddress(segment, offset), length);
        LoadFont(bytes, length, start, fontBlock, height);
    }

    /// <inheritdoc />
    public void SetScanLines(byte lines) {
        WriteMaskedToCrtController(GetCrtControllerPort(), 0x09, 0x1F, lines - 1);
        _biosDataArea.CharacterHeight = lines;
        ushort vde = GetVde();
        byte rows = (byte)(vde / lines);
        _biosDataArea.ScreenRows = (byte)(rows - 1);
        ushort columns = _biosDataArea.ScreenColumns;
        _biosDataArea.VideoPageSize = (ushort)CalculatePageSize(MemoryModel.Text, columns, rows);
        if (lines == 8) {
            SetCursorShape(0x0607);
        } else {
            SetCursorShape((ushort)(lines - 3 << 8 | lines - 2));
        }
    }

    /// <inheritdoc />
    public void LoadFont(byte[] fontBytes, ushort length, ushort start, byte fontBlock, byte height) {
        GetFontAccess();
        ushort blockAddress = (ushort)(((fontBlock & 0x03) << 14) + ((fontBlock & 0x04) << 11));
        ushort destination = (ushort)(blockAddress + start * 32);
        for (ushort i = 0; i < length; i++) {
            uint address = MemoryUtils.ToPhysicalAddress(VgaConstants.GraphicsSegment, (ushort)(destination + i * 32));
            var value = new Span<byte>(fontBytes, i * height, height);
            _memory.LoadData(address, value.ToArray(), height);
        }
        ReleaseFontAccess();
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public VgaMode GetCurrentMode() {
        int id = _biosDataArea.VideoMode;
        return RegisterValueSet.VgaModes[id].VgaMode;
    }

    /// <inheritdoc />
    public CursorPosition GetCursorPosition(byte page) {
        if (page > 7) {
            return new CursorPosition(0, 0, 0);
        }
        ushort xy = _biosDataArea.CursorPosition[page];
        return new CursorPosition(xy & 0xFF, xy >> 8, page);
    }

    /// <inheritdoc />
    public byte[] ReadFromDac(byte startIndex, int count) {
        byte[] result = new byte[3 * count];
        WriteByteToIoPort(startIndex, VgaPort.DacAddressReadIndex);
        for (int i = 0; i < result.Length; i++) {
            result[i] = ReadByteFromIoPort(VgaPort.DacData);
        }
        return result;
    }

    /// <inheritdoc />
    public void WriteToDac(byte index, byte red, byte green, byte blue) {
        WriteByteToIoPort(index, VgaPort.DacAddressWriteIndex);
        WriteByteToIoPort(red, VgaPort.DacData);
        WriteByteToIoPort(green, VgaPort.DacData);
        WriteByteToIoPort(blue, VgaPort.DacData);
    }

    /// <inheritdoc />
    public void WriteToDac(ushort segment, ushort offset, byte startIndex, ushort count) {
        byte[] rgb = _memory.GetData(MemoryUtils.ToPhysicalAddress(segment, offset), (uint)(3 * count));
        WriteToDac(rgb, startIndex, count);
    }

    /// <inheritdoc />
    public void WriteToPixelMask(byte value) {
        WriteByteToIoPort(value, VgaPort.DacPelMask);
    }

    /// <inheritdoc />
    public ushort ReadColorPageState() {
        byte pMode = (byte)(ReadAttributeController(0x10) >> 7);
        byte curPage = (byte)(ReadAttributeController(0x14) & 0x0F);
        if ((pMode & 0x01) == 0) {
            curPage >>= 2;
        }
        return (ushort)(curPage << 8 | pMode);
    }

    /// <inheritdoc />
    public byte ReadPixelMask() {
        return ReadByteFromIoPort(VgaPort.DacPelMask);
    }

    /// <inheritdoc />
    public void ReadFromDac(ushort segment, ushort offset, byte startIndex, ushort count) {
        WriteByteToIoPort(startIndex, VgaPort.DacAddressReadIndex);
        while (count > 0) {
            _memory.UInt8[segment, offset++] = ReadByteFromIoPort(VgaPort.DacData);
            _memory.UInt8[segment, offset++] = ReadByteFromIoPort(VgaPort.DacData);
            _memory.UInt8[segment, offset++] = ReadByteFromIoPort(VgaPort.DacData);
            count--;
        }
    }

    /// <inheritdoc />
    public void SetP5P4Select(bool enabled) {
        WriteMaskedToAttributeController(0x10, 0x80, (byte)(enabled ? 0x80 : 0x00));
    }

    /// <inheritdoc />
    public void SetColorSelectRegister(byte value) {
        // select page
        byte val = ReadAttributeController(0x10);
        if ((val & 0x80) == 0) {
            value <<= 2;
        }
        value &= 0x0f;
        WriteToAttributeController(0x14, value);
    }

    /// <inheritdoc />
    public void GetAllPaletteRegisters(ushort segment, ushort offset) {
        for (byte i = 0; i < 0x10; i++) {
            _memory.UInt8[segment, offset++] = ReadAttributeController(i);
        }
        _memory.UInt8[segment, offset] = ReadAttributeController(0x11);
    }

    /// <inheritdoc />
    public byte[] GetAllPaletteRegisters() {
        byte[] result = new byte[16];
        for (byte i = 0; i < 16; i++) {
            result[i] = ReadAttributeController(i);
        }
        result[0]  = ReadAttributeController(0x11);

        return result;
    }

    /// <inheritdoc />
    public byte GetOverscanBorderColor() {
        return ReadAttributeController(0x11);
    }

    /// <inheritdoc />
    public void ToggleIntensity(bool enabled) {
        WriteMaskedToAttributeController(0x10, 0x08, (byte)(enabled ? 1 << 3 : 0));
    }

    /// <inheritdoc />
    public byte ReadPaletteRegister(byte index) {
        return (byte)(index < 16 ? ReadAttributeController(index) : 0);
    }

    /// <inheritdoc />
    public void SetAllPaletteRegisters(ushort segment, ushort offset) {
        for (byte i = 0; i < 0x10; i++) {
            WriteToAttributeController(i, _memory.UInt8[segment, offset++]);
        }
        WriteToAttributeController(0x11, _memory.UInt8[segment, offset]);
    }

    /// <inheritdoc />
    public void SetAllPaletteRegisters(byte[] values) {
        if (values.Length != 16) {
            throw new ArgumentOutOfRangeException(nameof(values), "values must have 16 elements");
        }
        for (byte i = 0; i < 16; i++) {
            WriteToAttributeController(i, values[i]);
        }
        WriteToAttributeController(17, values[0]);
    }

    /// <inheritdoc />
    public void SetOverscanBorderColor(byte color) {
        WriteToAttributeController(0x11, color);
    }

    /// <inheritdoc />
    public void SetEgaPaletteRegister(byte register, byte value) {
        if (register > 0x14) {
            return;
        }
        WriteToAttributeController(register, value);
    }

    /// <inheritdoc />
    public void SetFontBlockSpecifier(byte fontBlock) {
        WriteToSequencer(0x03, fontBlock);
    }

    /// <inheritdoc />
    public void EnableVideoAddressing(bool state) {
        byte value = (byte)(state ? 0x02 : 0x00);
        WriteMaskedToMiscellaneousRegister(0x02, value);
    }

    /// <inheritdoc />
    public CursorPosition WriteTeletype(CursorPosition cursorPosition, CharacterPlusAttribute characterPlusAttribute) {
        switch (characterPlusAttribute.Character) {
            case (char)7:
                //FIXME should beep
                break;
            case (char)8:
                if (cursorPosition.X > 0) {
                    cursorPosition.X--;
                }
                break;
            case '\r':
                cursorPosition.X = 0;
                break;
            case '\n':
                cursorPosition.Y++;
                break;
            default:
                cursorPosition = WriteCharacter(cursorPosition, characterPlusAttribute);
                break;
        }

        // Do we need to scroll ?
        ushort numberOfRows = _biosDataArea.ScreenRows;
        if (cursorPosition.Y > numberOfRows) {
            cursorPosition.Y--;

            CursorPosition position = new(0, 0, cursorPosition.Page);
            Area area = new(_biosDataArea.ScreenColumns, numberOfRows + 1);
            Scroll(position, area, 1, new CharacterPlusAttribute(' ', 0, false));
        }
        return cursorPosition;
    }

    /// <inheritdoc />
    public void MoveChars(CursorPosition dest, Area area, int lines) {
        VgaMode vgaMode = GetCurrentMode();

        if (vgaMode.MemoryModel != MemoryModel.Text) {
            GraphicalMoveChars(vgaMode, dest, area, lines);
            return;
        }

        int stride = _biosDataArea.ScreenColumns * 2;
        ushort destinationAddress = TextAddress(dest), sourceAddress = (ushort)(destinationAddress + lines * stride);
        MemMoveStride(vgaMode.StartSegment, destinationAddress, sourceAddress, area.Width * 2, stride, (ushort)area.Height);
    }

    /// <inheritdoc />
    public void ClearCharacters(CursorPosition startPosition, Area area, CharacterPlusAttribute characterPlusAttribute) {
        VgaMode vgaMode = GetCurrentMode();

        if (vgaMode.MemoryModel != MemoryModel.Text) {
            GraphicalClearCharacters(vgaMode, startPosition, area, characterPlusAttribute);
            return;
        }

        ushort value = (ushort)((characterPlusAttribute.UseAttribute ? characterPlusAttribute.Attribute : DefaultAttribute) << 8 | characterPlusAttribute.Character);
        ushort stride = (ushort)(_biosDataArea.ScreenColumns * 2);
        ushort offset = TextAddress(startPosition);
        int amount = area.Width * 2;
        for (int lines = area.Height; lines > 0; lines--, offset += stride) {
            MemSet16(vgaMode.StartSegment, offset, value, amount);
        }
    }

    /// <inheritdoc />
    public void LoadGraphicsFont(ushort segment, ushort offset, byte height, byte rowSpecifier, byte userSpecifiedRows) {
        SetInterruptVectorAddress(0x43, segment, offset);
        byte rows = rowSpecifier switch {
            0 => userSpecifiedRows,
            1 => 14,
            3 => 43,
            _ => 25
        };
        _biosDataArea.ScreenRows = (byte)(rows - 1);
        _biosDataArea.CharacterHeight = height;
    }

    /// <inheritdoc />
    public event EventHandler<VideoModeChangedEventArgs>? VideoModeChanged;

    private ushort SetBiosDataArea(int modeId, ModeFlags flags, VgaMode vgaMode) {
        ushort width = vgaMode.Width;
        ushort height = vgaMode.Height;
        MemoryModel memoryModel = vgaMode.MemoryModel;
        ushort characterHeight = vgaMode.CharacterHeight;
        _biosDataArea.VideoMode = modeId < 0x100 ? (byte)modeId : (byte)0xff;

        if (memoryModel == MemoryModel.Text) {
            _biosDataArea.ScreenColumns = (byte)width;
            _biosDataArea.ScreenRows = (byte)(height - 1);
            _biosDataArea.CursorType = 0x0607;
        } else {
            _biosDataArea.ScreenColumns = (byte)(width / vgaMode.CharacterWidth);
            _biosDataArea.ScreenRows = (byte)(height / vgaMode.CharacterHeight - 1);
            _biosDataArea.CursorType = 0x0000;
        }
        _biosDataArea.VideoPageSize = (ushort)CalculatePageSize(memoryModel, width, height);
        _biosDataArea.CrtControllerBaseAddress = (ushort)GetCrtControllerPort();
        _biosDataArea.CharacterHeight = characterHeight;
        _biosDataArea.VideoCtl = (byte)(0x60 | (flags.HasFlag(ModeFlags.NoClearMem) ? 0x80 : 0x00));
        _biosDataArea.VideoFeatureSwitches = 0xF9;
        _biosDataArea.ModesetCtl &= 0x7F;
        for (int i = 0; i < 8; i++) {
            _biosDataArea.CursorPosition[i] = 0x0000;
        }
        _biosDataArea.VideoPageStart = 0x0000;
        _biosDataArea.CurrentVideoPage = 0x00;
        return characterHeight;
    }

    private ushort TextAddress(CursorPosition cursorPosition) {
        int stride = _biosDataArea.ScreenColumns * 2;
        int pageOffset = _biosDataArea.VideoPageSize * cursorPosition.Page;
        return (ushort)(pageOffset + cursorPosition.Y * stride + cursorPosition.X * 2);
    }

    private void SetCursorPosition(ushort address) {
        VgaPort port = GetCrtControllerPort();
        address /= 2; // Assume we're in text mode.
        WriteToCrtController(port, 0x0E, (byte)(address >> 8));
        WriteToCrtController(port, 0x0F, (byte)address);
    }

    private VgaPort GetCrtControllerPort() {
        return (ReadMiscellaneousOutput() & 1) != 0
            ? VgaPort.CrtControllerAddressAlt
            : VgaPort.CrtControllerAddress;
    }

    private byte ReadMiscellaneousOutput() {
        return ReadByteFromIoPort(VgaPort.MiscOutputRead);
    }

    private byte ReadByteFromIoPort(VgaPort port) {
        return _ioPortDispatcher.ReadByte((int)port);
    }

    private void Planar4Plane(int plane) {
        if (plane < 0) {
            // Return to default mode (read plane0, write all planes)
            WriteToSequencer(0x02, 0x0F);
            WriteToGraphicsController(0x04, 0);
        } else {
            WriteToSequencer(0x02, (byte)(1 << plane));
            WriteToGraphicsController(0x04, (byte)plane);
        }
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

    private CharacterPlusAttribute ReadGraphicsCharacter(VgaMode vgaMode, CursorPosition cursorPosition) {
        int characterHeight = _biosDataArea.CharacterHeight;
        if (cursorPosition.X >= _biosDataArea.ScreenColumns || characterHeight > 16) {
            return new CharacterPlusAttribute((char)0, 0, false);
        }

        // Read cell from screen
        GraphicsOperation operation = CreateGraphicsOperation(vgaMode);
        operation.MemoryAction = MemoryAction.ReadByte;
        operation.X = (ushort)(cursorPosition.X * 8);
        operation.Y = (ushort)(cursorPosition.Y * characterHeight);

        byte foregroundAttribute = 0x00;
        const byte backgroundAttribute = 0x00;
        byte[] lines = new byte[characterHeight];

        for (byte i = 0; i < characterHeight; i++, operation.Y++) {
            byte line = 0;
            HandleGraphicsOperation(operation);
            for (byte j = 0; j < 8; j++) {
                if (operation.Pixels[j] == backgroundAttribute) {
                    continue;
                }
                line |= (byte)(0x80 >> j);
                foregroundAttribute = operation.Pixels[j];
            }
            lines[i] = line;
        }

        // Determine font
        for (char character = (char)0; character < 256; character++) {
            SegmentedAddress font = GetFontAddress(character);
            if (MemCmp(lines, font.Segment, font.Offset, characterHeight) == 0) {
                return new CharacterPlusAttribute(character, foregroundAttribute, false);
            }
        }

        return new CharacterPlusAttribute((char)0, 0, false);
    }

    private SegmentedAddress GetInterruptVectorAddress(byte vector) {
        return new SegmentedAddress(_interruptVectorTable[vector]);
    }

    private int MemCmp(ReadOnlySpan<byte> bytes, ushort segment, ushort offset, int length) {
        int i = 0;
        while (length-- > 0 && i < bytes.Length) {
            int difference = bytes[i] - _memory.UInt8[segment, offset];
            if (difference != 0) {
                return difference < 0 ? -1 : 1;
            }
            i++;
            offset++;
        }
        return 0;
    }

    private ushort GetCursorShape() {
        ushort cursorType = _biosDataArea.CursorType;
        bool emulateCursor = (_biosDataArea.VideoCtl & 1) == 0;
        if (!emulateCursor) {
            return cursorType;
        }
        byte start = (byte)(cursorType >> 8 & 0x3f);
        byte end = (byte)(cursorType & 0x1f);
        ushort characterHeight = _biosDataArea.CharacterHeight;
        if (characterHeight <= 8 || end >= 8 || start >= 0x20) {
            return cursorType;
        }
        if (end != start + 1) {
            start = (byte)((start + 1) * characterHeight / 8 - 1);
        } else {
            start = (byte)((end + 1) * characterHeight / 8 - 2);
        }
        end = (byte)((end + 1) * characterHeight / 8 - 1);
        return (ushort)(start << 8 | end);
    }

    private static ushort AlignDown(ushort value, int alignment) {
        int mask = alignment - 1;
        return (ushort)(value & ~mask);
    }

    private static int CalculatePageSize(MemoryModel memoryModel, int width, int height) {
        return memoryModel switch {
            MemoryModel.Text => Align(width * height * 2, 2 * 1024),
            MemoryModel.Cga => 16 * 1024,
            _ => Align(width * height / 8, 8 * 1024)
        };
    }

    private static int Align(int alignment, int value) {
        int mask = alignment - 1;
        return value + mask & ~mask;
    }

    private static VideoMode GetVideoMode(int modeId) {
        if (RegisterValueSet.VgaModes.TryGetValue(modeId, out VideoMode mode)) {
            return mode;
        }

        throw new ArgumentOutOfRangeException(nameof(modeId), modeId, "Unknown mode");
    }

    private void Scroll(CursorPosition position, Area area, int lines, CharacterPlusAttribute characterPlusAttribute) {
        switch (lines) {
            case 0:
                // Clear window
                ClearCharacters(position, area, characterPlusAttribute);
                break;
            case > 0:
                // Scroll the window up (eg, from page down key)
                area.Height -= lines;
                MoveChars(position, area, lines);

                position.Y += area.Height;
                area.Height = lines;
                ClearCharacters(position, area, characterPlusAttribute);
                break;
            default:
                // Scroll the window down (eg, from page up key)
                position.Y -= lines;
                area.Height += lines;
                MoveChars(position, area, lines);

                position.Y += lines;
                area.Height = -lines;
                ClearCharacters(position, area, characterPlusAttribute);
                break;
        }
    }

    private void GraphicalMoveChars(VgaMode vgaMode, CursorPosition destination, Area area, int lines) {
        GraphicsOperation operation = CreateGraphicsOperation(vgaMode);
        operation.X = area.Width * 8;
        operation.Width = destination.X * 8;
        int characterHeight = _biosDataArea.CharacterHeight;
        operation.Y = area.Height * characterHeight;
        operation.Height = destination.Y * characterHeight;
        operation.Lines = operation.Y + lines * characterHeight;
        operation.MemoryAction = MemoryAction.MemMove;
        HandleGraphicsOperation(operation);
    }

    private void SetInterruptVectorAddress(byte vector, ushort segment, ushort offset) {
        _interruptVectorTable[vector] = new(segment, offset);
    }

    private void GraphicalClearCharacters(VgaMode vgaMode, CursorPosition startPosition, Area area, CharacterPlusAttribute ca) {
        GraphicsOperation operation = CreateGraphicsOperation(vgaMode);
        operation.X = startPosition.X * 8;
        operation.Width = area.Width * 8;
        int characterHeight = _biosDataArea.CharacterHeight;
        operation.Y = startPosition.Y * characterHeight;
        operation.Height = area.Height * characterHeight;
        operation.Pixels[0] = ca.Attribute;
        operation.MemoryAction = MemoryAction.MemSet;
        HandleGraphicsOperation(operation);
    }

    private void HandleGraphicsOperation(GraphicsOperation operation) {
        switch (operation.VgaMode.MemoryModel) {
            case MemoryModel.Planar:
                HandlePlanarGraphicsOperation(operation);
                break;
            case MemoryModel.Cga:
                HandleCgaGraphicsOperation(operation);
                break;
            case MemoryModel.Packed:
                HandlePackedGraphicsOperation(operation);
                break;
            case MemoryModel.Text:
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), $"Unsupported memory model {operation.VgaMode.MemoryModel}");
        }
    }

    private void HandleCgaGraphicsOperation(GraphicsOperation operation) {
        int bitsPerPixel = operation.VgaMode.BitsPerPixel;
        ushort offset = (ushort)(operation.Y / 2 * operation.LineLength + operation.X / 8 * bitsPerPixel);
        switch (operation.MemoryAction) {
            default:
            case MemoryAction.ReadByte:
                ReadByteOperationCga(operation, offset, bitsPerPixel);
                break;
            case MemoryAction.WriteByte:
                WriteByteOperationCga(operation, offset, bitsPerPixel);
                break;
            case MemoryAction.MemSet:
                MemSetOperationCga(operation, offset, bitsPerPixel);
                break;
            case MemoryAction.MemMove:
                MemMoveOperationCga(operation, offset, bitsPerPixel);
                break;
        }
    }

    private void MemMoveOperationCga(GraphicsOperation operation, ushort offset, int bitsPerPixel) {
        ushort source = (ushort)(operation.Lines / 2 * operation.LineLength + operation.X / 8 * bitsPerPixel);
        MemMoveStride(VgaConstants.ColorTextSegment, offset, source, operation.Width / 8 * bitsPerPixel, operation.LineLength, (ushort)(operation.Height / 2));
        MemMoveStride(VgaConstants.ColorTextSegment, (ushort)(offset + 0x2000), (ushort)(source + 0x2000), operation.Width / 8 * bitsPerPixel, operation.LineLength, (ushort)(operation.Height / 2));
    }

    private void MemSetOperationCga(GraphicsOperation operation, ushort offset, int bitsPerPixel) {
        byte data = operation.Pixels[0];
        if (bitsPerPixel == 1) {
            data = (byte)(data & 1 | (data & 1) << 1);
        }
        data &= 3;
        data |= (byte)(data << 2 | data << 4 | data << 6);
        MemSetStride(VgaConstants.ColorTextSegment, offset, data, operation.Width / 8 * bitsPerPixel, operation.LineLength, (ushort)(operation.Height / 2));
        MemSetStride(VgaConstants.ColorTextSegment, (ushort)(offset + 0x2000), data, operation.Width / 8 * bitsPerPixel, operation.LineLength, (ushort)(operation.Height / 2));
    }

    private void WriteByteOperationCga(GraphicsOperation operation, ushort offset, int bitsPerPixel) {
        if ((operation.Y & 1) != 0) {
            offset += 0x2000;
        }
        if (bitsPerPixel == 1) {
            byte uint8 = 0;
            for (int pixel = 0; pixel < 8; pixel++) {
                uint8 |= (byte)((operation.Pixels[pixel] & 1) << 7 - pixel);
            }
            _memory.UInt8[VgaConstants.ColorTextSegment, offset] = uint8;
        } else {
            ushort pixels = 0;
            for (int pixel = 0; pixel < 8; pixel++) {
                pixels |= (ushort)((operation.Pixels[pixel] & 3) << (7 - pixel) * 2);
            }
            pixels = (ushort)(pixels << 8 | pixels >> 8);
            // if (_logger.IsEnabled(LogEventLevel.Verbose)) {
            //     _logger.Verbose("Writing {Value:X2} to offset {Offset:X4}", pixels, offset);
            // }
            _memory.UInt16[VgaConstants.ColorTextSegment, offset] = pixels;
        }
    }

    private void ReadByteOperationCga(GraphicsOperation operation, ushort offset, int bitsPerPixel) {
        if ((operation.Y & 1) != 0) {
            offset += 0x2000;
        }
        if (bitsPerPixel == 1) {
            byte uint8 = _memory.UInt8[VgaConstants.ColorTextSegment, offset];
            int pixel;
            for (pixel = 0; pixel < 8; pixel++) {
                operation.Pixels[pixel] = (byte)(uint8 >> 7 - pixel & 1);
            }
        } else {
            ushort uint16 = _memory.UInt16[VgaConstants.ColorTextSegment, offset];
            uint16 = (ushort)(uint16 << 8 | uint16 >> 8);
            int pixel;
            for (pixel = 0; pixel < 8; pixel++) {
                operation.Pixels[pixel] = (byte)(uint16 >> (7 - pixel) * 2 & 3);
            }
        }
    }

    private void HandlePlanarGraphicsOperation(GraphicsOperation operation) {
        ushort offset = (ushort)(operation.Y * operation.LineLength + operation.X / 8);
        switch (operation.MemoryAction) {
            default:
            case MemoryAction.ReadByte:
                ReadByteOperationPlanar(operation, offset);
                break;
            case MemoryAction.WriteByte:
                WriteByteOperationPlanar(operation, offset);
                break;
            case MemoryAction.MemSet:
                MemSetOperationPlanar(operation, offset);
                break;
            case MemoryAction.MemMove:
                MemMoveOperationPlanar(operation, offset);
                break;
        }
        Planar4Plane(-1);
    }

    private void MemMoveOperationPlanar(GraphicsOperation operation, ushort offset) {
        int plane;
        ushort source = (ushort)(operation.Lines * operation.LineLength + operation.X / 8);
        for (plane = 0; plane < 4; plane++) {
            Planar4Plane(plane);
            MemMoveStride(VgaConstants.GraphicsSegment, offset, source, operation.Width / 8, operation.LineLength, operation.Height);
        }
    }

    private void MemSetOperationPlanar(GraphicsOperation operation, ushort offset) {
        int plane;
        for (plane = 0; plane < 4; plane++) {
            Planar4Plane(plane);
            byte data = (byte)((operation.Pixels[0] & 1 << plane) != 0 ? 0xFF : 0x00);
            MemSetStride(VgaConstants.GraphicsSegment, offset, data, operation.Width / 8, operation.LineLength, operation.Height);
        }
    }

    private void WriteByteOperationPlanar(GraphicsOperation operation, ushort offset) {
        for (int plane = 0; plane < 4; plane++) {
            Planar4Plane(plane);
            byte data = 0;
            for (int pixel = 0; pixel < 8; pixel++) {
                data |= (byte)((operation.Pixels[pixel] >> plane & 1) << 7 - pixel);
            }
            _memory.UInt8[VgaConstants.GraphicsSegment, offset] = data;
        }
    }

    private void ReadByteOperationPlanar(GraphicsOperation operation, ushort offset) {
        operation.Pixels = new byte[8];
        for (int plane = 0; plane < 4; plane++) {
            Planar4Plane(plane);
            byte data = _memory.UInt8[VgaConstants.GraphicsSegment, offset];
            int pixel;
            for (pixel = 0; pixel < 8; pixel++) {
                operation.Pixels[pixel] |= (byte)((data >> 7 - pixel & 1) << plane);
            }
        }
    }

    private void MemMoveStride(ushort segment, ushort destination, ushort source, int length, int stride, int lines) {
        if (source < destination) {
            destination += (ushort)(stride * (lines - 1));
            source += (ushort)(stride * (lines - 1));
            stride = -stride;
        }
        for (; lines > 0; lines--, destination += (ushort)stride, source += (ushort)stride) {
            uint sourceAddress = MemoryUtils.ToPhysicalAddress(segment, source);
            uint destinationAddress = MemoryUtils.ToPhysicalAddress(segment, destination);
            _memory.MemCopy(sourceAddress, destinationAddress, (uint)length);
        }
    }

    private void MemSetStride(ushort segment, ushort destination, byte value, int length, int stride, int lines) {
        for (; lines > 0; lines--, destination += (ushort)stride) {
            _memory.Memset8(MemoryUtils.ToPhysicalAddress(segment, destination), value, (uint)length);
        }
    }

    private CursorPosition WriteCharacter(CursorPosition cursorPosition, CharacterPlusAttribute characterPlusAttribute) {
        VgaMode vgaMode = GetCurrentMode();

        if (vgaMode.MemoryModel != MemoryModel.Text) {
            WriteCharacterGraphics(cursorPosition, characterPlusAttribute, vgaMode);
        } else {
            ushort offset = TextAddress(cursorPosition);
            int attribute = characterPlusAttribute.UseAttribute ? characterPlusAttribute.Attribute : DefaultAttribute;
            _memory.UInt16[vgaMode.StartSegment, offset] = (ushort)(attribute << 8 | characterPlusAttribute.Character);
        }
        cursorPosition.X++;
        // Wrap at end of line.
        if (cursorPosition.X == _biosDataArea.ScreenColumns) {
            cursorPosition.X = 0;
            cursorPosition.Y++;
        }
        return cursorPosition;
    }

    private void WriteCharacterGraphics(CursorPosition cursorPosition, CharacterPlusAttribute characterPlusAttribute, VgaMode vgaMode) {
        if (cursorPosition.X >= _biosDataArea.ScreenColumns) {
            return;
        }
        GraphicsOperation operation = CreateGraphicsOperation(vgaMode);
        operation.X = (ushort)(cursorPosition.X * 8);
        int characterHeight = _biosDataArea.CharacterHeight;
        operation.Y = (ushort)(cursorPosition.Y * characterHeight);
        byte foregroundAttribute = characterPlusAttribute.Attribute;
        bool useXor = false;
        if ((foregroundAttribute & 0x80) != 0 && vgaMode.BitsPerPixel < 8) {
            useXor = true;
            foregroundAttribute &= 0x7f;
        }
        SegmentedAddress font = GetFontAddress(characterPlusAttribute.Character);
        for (int i = 0; i < characterHeight; i++, operation.Y++) {
            byte fontLine = _memory.UInt8[font.Segment, (ushort)(font.Offset + i)];
            if (useXor) {
                operation.MemoryAction = MemoryAction.ReadByte;
                HandleGraphicsOperation(operation);
                for (int j = 0; j < 8; j++) {
                    operation.Pixels[j] ^= (byte)((fontLine & 0x80 >> j) != 0 ? foregroundAttribute : 0x00);
                }
            } else {
                for (int j = 0; j < 8; j++) {
                    operation.Pixels[j] = (byte)((fontLine & 0x80 >> j) != 0 ? foregroundAttribute : 0x00);
                }
            }
            operation.MemoryAction = MemoryAction.WriteByte;
            HandleGraphicsOperation(operation);
        }
    }

    private GraphicsOperation CreateGraphicsOperation(VgaMode vgaMode) {
        VgaPort port = GetCrtControllerPort();
        byte value = ReadCrtController(port, 0x13);
        int vramRatio = GetVramRatio(vgaMode);
        int lineLength = value * 8 / vramRatio;
        int address = ReadCrtController(port, 0x0C) << 8 | ReadCrtController(port, 0x0D);
        int displayStart = address * 4 / vramRatio;

        return new GraphicsOperation {
            Pixels = new byte[8],
            VgaMode = vgaMode,
            LineLength = lineLength,
            DisplayStart = displayStart,
            Width = 0,
            Height = 0,
            X = 0,
            Y = 0,
            MemoryAction = MemoryAction.ReadByte
        };
    }

    private SegmentedAddress GetFontAddress(char character) {
        int characterHeight = _biosDataArea.CharacterHeight;
        SegmentedAddress address;
        if (characterHeight == 8 && character >= 128) {
            address = GetInterruptVectorAddress(0x1F);
            character = (char)(character - 128);
        } else {
            address = GetInterruptVectorAddress(0x43);
        }
        address += (ushort)(character * characterHeight);
        return address;
    }

    private void HandlePackedGraphicsOperation(GraphicsOperation operation) {
        ushort destination = (ushort)(operation.Y * operation.LineLength + operation.X);
        switch (operation.MemoryAction) {
            default:
            case MemoryAction.ReadByte:
                operation.Pixels = _memory.GetData(MemoryUtils.ToPhysicalAddress(VgaConstants.GraphicsSegment, destination), 8);
                break;
            case MemoryAction.WriteByte:
                _memory.LoadData(MemoryUtils.ToPhysicalAddress(VgaConstants.GraphicsSegment, destination), operation.Pixels, 8);
                break;
            case MemoryAction.MemSet:
                MemSetStride(VgaConstants.GraphicsSegment, destination, operation.Pixels[0], operation.Width, operation.LineLength, operation.Height);
                break;
            case MemoryAction.MemMove:
                ushort source = (ushort)(operation.Lines * operation.LineLength + operation.X);
                MemMoveStride(VgaConstants.GraphicsSegment, destination, source, operation.Width, operation.LineLength, operation.Height);
                break;
        }
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

    private void SetAttributeControllerIndex(byte value) {
        ReadByteFromIoPort(VgaPort.InputStatus1ReadAlt);
        WriteByteToIoPort(value, VgaPort.AttributeAddress);
    }

    private void WriteToDac(ReadOnlySpan<byte> palette, byte startIndex, int count) {
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

    private byte ReadAttributeController(byte index) {
        ReadByteFromIoPort(VgaPort.InputStatus1ReadAlt);
        byte original = ReadByteFromIoPort(VgaPort.AttributeAddress);
        WriteByteToIoPort(index, VgaPort.AttributeAddress);
        byte value = ReadByteFromIoPort(VgaPort.AttributeData);
        ReadByteFromIoPort(VgaPort.InputStatus1ReadAlt);
        WriteByteToIoPort(original, VgaPort.AttributeAddress);
        return value;
    }

    private void WriteMaskedToAttributeController(byte index, byte offBits, byte onBits) {
        ReadByteFromIoPort(VgaPort.InputStatus1ReadAlt);
        byte original = ReadByteFromIoPort(VgaPort.AttributeAddress);
        WriteByteToIoPort(index, VgaPort.AttributeAddress);
        byte value = ReadByteFromIoPort(VgaPort.AttributeData);
        WriteByteToIoPort((byte)(value & ~offBits | onBits), VgaPort.AttributeAddress);
        WriteByteToIoPort(original, VgaPort.AttributeAddress);
    }

    private void WriteMaskedToCrtController(VgaPort port, byte index, int offBits, int onBits) {
        WriteByteToIoPort(index, port);
        byte value = ReadByteFromIoPort(port + 1);
        WriteByteToIoPort((byte)(value & ~offBits | onBits), port + 1);
    }

    private ushort GetVde() {
        VgaPort port = GetCrtControllerPort();
        ushort vde = ReadCrtController(port, 0x12);
        byte ovl = ReadCrtController(port, 0x07);
        vde = (ushort)(vde + ((ovl & 0x02) << 7) + ((ovl & 0x40) << 3) + 1);
        return vde;
    }

    private void SetDisplayStart(VgaMode vgaMode, int value) {
        VgaPort port = GetCrtControllerPort();
        value = value * GetVramRatio(vgaMode) / 4;
        WriteToCrtController(port, 0x0C, (byte)(value >> 8));
        WriteToCrtController(port, 0x0D, (byte)value);
    }

    private void WriteFonts(VgaMode vgaMode) {
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

    private void FillRegisters(VideoMode videoMode) {
        SetAttributeControllerRegisters(videoMode.AttributeControllerRegisterValues);
        SetSequencerRegisters(videoMode.SequencerRegisterValues);
        SetGraphicsControllerRegisters(videoMode.GraphicsControllerRegisterValues);

        // Set CRTC address VGA or MDA
        byte miscellaneousRegisterValue = videoMode.MiscellaneousRegisterValue;
        VgaPort crtControllerPort = (miscellaneousRegisterValue & 1) == 0
            ? VgaPort.CrtControllerAddress
            : VgaPort.CrtControllerAddressAlt;

        // Disable CRTC write protection
        WriteToCrtController(crtControllerPort, 0x11, 0x00);
        SetCrtControllerRegisters(crtControllerPort, videoMode.CrtControllerRegisterValues);

        // Set the misc register
        WriteToMiscellaneousOutput(miscellaneousRegisterValue);
    }

    private void SetCrtControllerRegisters(VgaPort crtControllerPort, ReadOnlySpan<byte> videoModeCrtControllerRegisterValues) {
        for (byte i = 0; i <= 0x18; i++) {
            WriteToCrtController(crtControllerPort, i, videoModeCrtControllerRegisterValues[i]);
        }
    }

    private void SetGraphicsControllerRegisters(ReadOnlySpan<byte> videoModeGraphicsControllerRegisterValues) {
        for (byte i = 0; i < videoModeGraphicsControllerRegisterValues.Length; i++) {
            WriteToGraphicsController(i, videoModeGraphicsControllerRegisterValues[i]);
        }
    }

    private void SetSequencerRegisters(ReadOnlySpan<byte> videoModeSequencerRegisterValues) {
        for (byte i = 0; i < videoModeSequencerRegisterValues.Length; i++) {
            WriteToSequencer(i, videoModeSequencerRegisterValues[i]);
        }
    }

    private void SetAttributeControllerRegisters(ReadOnlySpan<byte> videoModeAttributeControllerRegisterValues) {
        for (byte i = 0; i < videoModeAttributeControllerRegisterValues.Length; i++) {
            WriteToAttributeController(i, videoModeAttributeControllerRegisterValues[i]);
        }
    }

    private void LoadPalette(VideoMode videoMode, ModeFlags flags) {
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

    private void ClearScreen(VgaMode vgaMode) {
        switch (vgaMode.MemoryModel) {
            case MemoryModel.Text:
                // Write white (0x07) spaces (0x20) to memory. 
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

    private void MemSet16(ushort segment, ushort offset, ushort value, int amount) {
        amount /= 2;
        uint address = MemoryUtils.ToPhysicalAddress(segment, offset);
        _memory.Memset16(address, value, (uint)amount);
        for (int i = 0; i < amount; i += 2) {
            _memory.UInt16[(uint)(address + i)] = value;
        }
    }

    private void WriteMaskedToMiscellaneousRegister(byte offBits, byte onBits) {
        WriteToMiscellaneousOutput((byte)(ReadMiscellaneousOutput() & ~offBits | onBits));
    }
}