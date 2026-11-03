// SPDX-FileCopyrightText: 2020-2026 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

public partial class VgaFunctionality {
    /// <summary>Programs the native processor/display page register and its BIOS shadow.</summary>
    public void SetTandyPageBank(byte value) {
        if (_tandy == null) {
            return;
        }
        _memory.UInt8[0x40, 0x8A] = value;
        _ioPortDispatcher.WriteByte(0x3DF, value);
    }

    private void SetTandyMode(int modeId, ModeFlags flags) {
        TandyVideoMode? mode = TandyVideoMode.Find(modeId);
        if (mode == null || _tandy == null) {
            return;
        }
        WriteToCrtController(VgaPort.CrtControllerAddressAlt, 0, (byte)mode.HorizontalTotal);
        WriteToCrtController(VgaPort.CrtControllerAddressAlt, 1, (byte)mode.HorizontalDisplay);
        WriteToCrtController(VgaPort.CrtControllerAddressAlt, 2, (byte)(mode.HorizontalDisplay + 1));
        WriteToCrtController(VgaPort.CrtControllerAddressAlt, 3, 10);
        WriteToCrtController(VgaPort.CrtControllerAddressAlt, 4, (byte)mode.VerticalTotal);
        WriteToCrtController(VgaPort.CrtControllerAddressAlt, 5, 6);
        WriteToCrtController(VgaPort.CrtControllerAddressAlt, 6, (byte)mode.VerticalDisplay);
        WriteToCrtController(VgaPort.CrtControllerAddressAlt, 7,
            (byte)(mode.VerticalDisplay + (mode.VerticalTotal - mode.VerticalDisplay) / 2 - 1));
        WriteToCrtController(VgaPort.CrtControllerAddressAlt, 9, (byte)(mode.Scanlines - 1));
        WriteTandyRegister(1, 15);
        WriteTandyRegister(2, 0);
        WriteTandyRegister(3, mode.ExtendedControl);
        for (byte i = 0; i < 16; i++) {
            WriteTandyRegister((byte)(0x10 + i), i);
        }
        WriteTandyRegister(5, 0);
        byte pageBank = mode.Id >= 9 ? (byte)0xF6 : (byte)0x3F;
        _ioPortDispatcher.WriteByte(0x3DF, pageBank);
        _memory.UInt8[0x40, 0x8A] = pageBank;
        byte colorSelect = mode.Id is 6 or 10 ? (byte)0x3F : (byte)0x30;
        _ioPortDispatcher.WriteByte(0x3D8, mode.ModeControl);
        _ioPortDispatcher.WriteByte(0x3D9, colorSelect);
        _biosDataArea.VideoMode = (byte)mode.Id;
        _biosDataArea.VideoMsr = mode.ModeControl;
        _biosDataArea.VideoPal = colorSelect;
        _biosDataArea.ScreenColumns = (ushort)mode.Columns;
        _biosDataArea.ScreenRows = 24;
        _biosDataArea.CharacterHeight = 8;
        _biosDataArea.VideoPageSize = (ushort)mode.PageLength;
        _biosDataArea.CrtControllerBaseAddress = 0x3D4;
        _biosDataArea.VideoPageStart = 0;
        _biosDataArea.CurrentVideoPage = 0;
        for (int page = 0; page < 8; page++) {
            _biosDataArea.CursorPosition[page] = 0;
        }
        if (mode.IsText) {
            SetCursorShape(0x0607);
        }
        SetActivePage(0);
        if (!flags.HasFlag(ModeFlags.NoClearMem)) {
            ClearScreen(mode.Mode);
        }
        _interruptVectorTable[0x44] = _vgaRom.VgaFont8Address;
        _interruptVectorTable[0x1F] = _vgaRom.VgaFont8Address2;
        _videoState.IsRenderingDirty = true;
        VideoModeChanged?.Invoke(this, new VideoModeChangedEventArgs(mode.Mode));
    }

    private void WriteTandyRegister(byte index, byte value) {
        _ioPortDispatcher.WriteByte(0x3DA, index);
        _ioPortDispatcher.WriteByte(0x3DE, value);
    }

    private void SetTandyBackground(byte color) {
        byte select = (byte)((_biosDataArea.VideoPal & 0xE0) | (color & 0x1F));
        _biosDataArea.VideoPal = select;
        int mode = _biosDataArea.VideoMode;
        if (mode == 10) {
            _ioPortDispatcher.WriteByte(0x3D9, 0);
            return;
        }
        if (mode != 6) {
            WriteTandyRegister(2, color);
        }
        if (mode is 8 or 9) {
            WriteTandyRegister(0x10, color);
        }
        _ioPortDispatcher.WriteByte(0x3D9, select);
    }

    private void SetTandyPalette(byte index, byte value) {
        if (_tandy == null) {
            return;
        }
        if (_tandy.BitsPerPixel == 1 && _tandy.IsGraphics) {
            if (index >= 16) {
                return;
            }
            // Preserve the upstream BIOS's doubled 10h offset in this branch.
            index = index == 1 ? (byte)0x1F : (byte)(index | 0x10);
        } else if (_tandy.BitsPerPixel == 2 && _tandy.IsGraphics && _biosDataArea.VideoMode != 10) {
            if (index > 3) {
                return;
            }
            if (index != 0) {
                index = (byte)(index * 2 + 8 + ((_biosDataArea.VideoPal >> 5) & 1));
            }
        }
        WriteTandyRegister((byte)(index + 0x10), value);
        _ioPortDispatcher.WriteByte(0x3DA, 0);
    }

    private (ushort Offset, int Shift, int Mask) TandyPixelLocation(int x, int y) {
        int mode = _biosDataArea.VideoMode;
        int width = _biosDataArea.ScreenColumns * 8;
        if (mode == 10) {
            int offset = (y >> 2) * 160 + ((x >> 2) & ~1) + 0x2000 * (y & 3);
            return ((ushort)offset, 7 - (x & 7), 3);
        }
        int bits = GetCurrentMode().BitsPerPixel;
        int lineMask = mode >= 9 ? 3 : 1;
        int lineShift = mode >= 9 ? 2 : 1;
        int pixelsPerByte = 8 / bits;
        int address = (y >> lineShift) * (width * bits / 8) + x / pixelsPerByte + 0x2000 * (y & lineMask);
        return ((ushort)address, (pixelsPerByte - 1 - x % pixelsPerByte) * bits, (1 << bits) - 1);
    }

    private byte ReadTandyPixel(int x, int y) {
        if (_biosDataArea.VideoMode <= 3) {
            return 0;
        }
        if (_biosDataArea.VideoMode == 10) {
            // Upstream GetPixel retains the CGA layout even in the native high resolution mode.
            ushort cgaOffset = (ushort)((y >> 1) * 80 + (x >> 2) + 0x2000 * (y & 1));
            int cgaData = _memory.UInt8[0xB800, cgaOffset];
            return (byte)((cgaData >> ((3 - (x & 3)) * 2)) & 3);
        }
        (ushort offset, int shift, int mask) = TandyPixelLocation(x, y);
        int data = _memory.UInt8[0xB800, offset];
        return (byte)((data >> shift) & mask);
    }

    private void WriteTandyPixel(int x, int y, byte color, bool xor) {
        if (_biosDataArea.VideoMode <= 3) {
            return;
        }
        (ushort offset, int shift, int mask) = TandyPixelLocation(x, y);
        if (_biosDataArea.VideoMode == 10) {
            WriteTandyPixelByte(offset, shift, 1, color, xor);
            WriteTandyPixelByte((ushort)(offset + 1), shift, 1, (byte)(color >> 1), xor);
            return;
        }
        WriteTandyPixelByte(offset, shift, mask, color, xor);
    }

    private void WriteTandyPixelByte(ushort offset, int shift, int mask, byte color, bool xor) {
        byte previous = _memory.UInt8[0xB800, offset];
        int value = (color & mask) << shift;
        int result = xor ? previous ^ value : (previous & ~(mask << shift)) | value;
        _memory.UInt8[0xB800, offset] = (byte)result;
    }

    private void HandleTandyGraphicsOperation(GraphicsOperation operation) {
        if (operation.MemoryAction is MemoryAction.ReadByte or MemoryAction.WriteByte) {
            for (int pixel = 0; pixel < 8; pixel++) {
                if (operation.MemoryAction == MemoryAction.ReadByte) {
                    operation.Pixels[pixel] = ReadTandyPixel(operation.X + pixel, operation.Y);
                } else {
                    WriteTandyPixel(operation.X + pixel, operation.Y, operation.Pixels[pixel], false);
                }
            }
            return;
        }
        int bits = operation.VgaMode.BitsPerPixel;
        int banks = bits == 4 ? _biosDataArea.ScreenColumns / 10 : 2;
        int bytesPerCell = bits == 1 ? 1 : bits;
        int stride = _biosDataArea.ScreenColumns * bytesPerCell;
        int rowCount = operation.Height / banks;
        int start = 0;
        int end = rowCount;
        int step = 1;
        if (operation.MemoryAction == MemoryAction.MemMove && operation.Lines < operation.Y) {
            start = rowCount - 1;
            end = -1;
            step = -1;
        }
        int fill = operation.Pixels[0] & 3;
        fill |= fill << 2;
        fill |= fill << 4;
        if (bits == 4) {
            fill = (operation.Pixels[0] & 15) * 0x11;
        }
        int columnOffset = operation.X / 8 * bytesPerCell;
        int length = operation.Width / 8 * bytesPerCell;
        for (int row = start; row != end; row += step) {
            for (int bank = 0; bank < banks; bank++) {
                int destination = _biosDataArea.VideoPageStart + (operation.Y / banks + row) * stride + columnOffset + bank * 0x2000;
                int source = _biosDataArea.VideoPageStart + (operation.Lines / banks + row) * stride + columnOffset + bank * 0x2000;
                for (int x = 0; x < length; x++) {
                    byte value = (byte)fill;
                    if (operation.MemoryAction == MemoryAction.MemMove) {
                        value = _memory.UInt8[0xB800, (ushort)(source + x)];
                    }
                    _memory.UInt8[0xB800, (ushort)(destination + x)] = value;
                }
            }
        }
    }
}