namespace Spice86.Core.Emulator.Devices.Video;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.Video.Registers.CrtController;
using Spice86.Shared.Interfaces;

using System.Text;

/// <inheritdoc />
public class Renderer : IVgaRenderer {
    private readonly IVideoMemory _memory;
    private readonly IVideoState _state;

    private static bool _rendering;

    /// <summary>
    ///   Create a new VGA renderer.
    /// </summary>
    public Renderer(IVideoState state, IVideoMemory memory, ILoggerService loggerService) {
        _state = state;
        _memory = memory;
        loggerService.WithLogLevel(LogEventLevel.Verbose);
    }

    /// <inheritdoc />
    public int Width => (_state.CrtControllerRegisters.HorizontalDisplayEnd + 1) * _state.SequencerRegisters.ClockingModeRegister.DotsPerClock
        / (_state.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode ? 2 : 1);

    /// <inheritdoc />
    public int Height => (_state.CrtControllerRegisters.VerticalDisplayEndValue + 1)
        / (_state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode ? _state.CrtControllerRegisters.CharacterCellHeightRegister.CharacterCellHeight + 1 : 1);

    /// <inheritdoc />
    public void Render(Span<uint> frameBuffer) {
        if (_rendering) {
            return;
        }
        if (Width * Height > frameBuffer.Length) {
            // Resolution change hasn't caught up yet. Skip a frame.
            return;
        }
        _rendering = true;

        int characterWidth = _state.SequencerRegisters.ClockingModeRegister.DotsPerClock;
        int horizontalDisplayEnd = _state.CrtControllerRegisters.HorizontalDisplayEnd + 1;
        int horizontalBlankStart = _state.CrtControllerRegisters.HorizontalBlankingStart;
        int horizontalBlankEnd = _state.CrtControllerRegisters.HorizontalBlankingEndValue;
        int totalWidth = _state.CrtControllerRegisters.HorizontalTotal + 4;
        int verticalDisplayEnd = _state.CrtControllerRegisters.VerticalDisplayEndValue;
        int verticalBlankStart = _state.CrtControllerRegisters.VerticalBlankingStartValue;
        int verticalSyncStart = _state.CrtControllerRegisters.VerticalSyncStartValue;
        int verticalSyncEnd = _state.CrtControllerRegisters.VerticalSyncEndRegister.VerticalSyncEnd;
        int verticalBlankEnd = _state.CrtControllerRegisters.VerticalBlankingEnd - 1;
        int totalHeight = _state.CrtControllerRegisters.VerticalTotalValue + 2;

        // Skew controls the delay of enabling the display at the start of the line.
        int skew = _state.CrtControllerRegisters.HorizontalBlankingEndRegister.DisplayEnableSkew;

        _state.GeneralRegisters.InputStatusRegister1.VerticalRetrace = false;
        _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = true;
        int startAddress = _state.CrtControllerRegisters.ScreenStartAddress;
        int destinationAddress = 0;

        int characterClockMask = _state.CrtControllerRegisters.UnderlineRowScanlineRegister.CountByFour
            ? 3
            : _state.CrtControllerRegisters.CrtModeControlRegister.CountByTwo
                ? 1
                : 0;
        int characterHeight = _state.CrtControllerRegisters.CharacterCellHeightRegister.CharacterCellHeight + 1;

        bool horizontalBlanking = false;
        bool verticalBlanking = false;
        bool overscan = false;
        int memoryAddress = startAddress;
        int previousRowMemoryAddress = memoryAddress;

        bool chain4 = _state.SequencerRegisters.MemoryModeRegister.Chain4Mode;

        MemoryWidth memoryWidthMode;
        if (_state.CrtControllerRegisters.UnderlineRowScanlineRegister.DoubleWordMode) {
            memoryWidthMode = MemoryWidth.DoubleWord;
        } else if (_state.CrtControllerRegisters.CrtModeControlRegister.ByteWordMode == ByteWordMode.Byte) {
            memoryWidthMode = MemoryWidth.Byte;
        } else if (_state.CrtControllerRegisters.CrtModeControlRegister.AddressWrap) {
            memoryWidthMode = MemoryWidth.Word15;
        } else {
            memoryWidthMode = MemoryWidth.Word13;
        }

        // This seems to work, but it shouldn't :) It multiplies the offset by 2 when increasing the memory address per row.
        // According to the documentation this should depend on the byte/word mode, but I probably implemented something else
        // wrong that compensates for it. I'll leave it like this for now.
        int offsetShift = 1;

        int rowWidth = horizontalDisplayEnd * characterWidth;

        const bool debug = false;

        /////////////////////////
        // Start Vertical loop //
        /////////////////////////
        for (uint rowCounter = 0; rowCounter < totalHeight; rowCounter++) {
            ///////////////////////////
            // Start Horizontal loop //
            ///////////////////////////
            StringBuilder sb;
            if (debug) sb = new StringBuilder($"{memoryAddress:X6}|");
            for (int characterCounter = 0; characterCounter < totalWidth; characterCounter++) {
                if (characterCounter == skew) {
                    overscan = false;
                }
                if (characterCounter == horizontalDisplayEnd + skew) {
                    overscan = true;
                }
                if (characterCounter == horizontalBlankStart) {
                    horizontalBlanking = true;
                }
                if (horizontalBlanking && horizontalBlankEnd == (characterCounter & 0b111111)) {
                    horizontalBlanking = false;
                }
                _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = horizontalBlanking || verticalBlanking;
                if (rowCounter < verticalDisplayEnd && characterCounter < horizontalDisplayEnd && (rowCounter & _state.CrtControllerRegisters.CharacterCellHeightRegister.CharacterCellHeight) == 0) {
                    // Convert logical address to physical address.
                    ushort physicalAddress = memoryWidthMode switch {
                        MemoryWidth.Byte => (ushort)memoryAddress,
                        MemoryWidth.Word13 => (ushort)(memoryAddress << 1 | memoryAddress >> 13 & 1),
                        MemoryWidth.Word15 => (ushort)(memoryAddress << 1 | memoryAddress >> 15 & 1),
                        MemoryWidth.DoubleWord => (ushort)(memoryAddress << 2 | memoryAddress >> 14 & 3),
                        _ => throw new InvalidOperationException($"Unknown memory width mode: {memoryWidthMode}")
                    };
                    if (!_state.CrtControllerRegisters.CrtModeControlRegister.CompatibilityModeSupport) {
                        // Use the row scan counter rather than the memory address counter for bit 13.
                        physicalAddress = (ushort)(physicalAddress & ~0x8000 | ((rowCounter & 1) != 0 ? 0x8000 : 0));
                    }
                    if (!_state.CrtControllerRegisters.CrtModeControlRegister.SelectRowScanCounter) {
                        // Use the row scan counter rather than the memory address counter for bit 14.
                        physicalAddress = (ushort)(physicalAddress & ~0x10000 | ((rowCounter & 2) != 0 ? 0x10000 : 0));
                    }
                    if (destinationAddress >= frameBuffer.Length) {
                        break;
                    }

                    // Read 4 bytes from the 4 planes.
                    byte d0 = _memory.Planes[0, physicalAddress];
                    byte d1 = _memory.Planes[1, physicalAddress];
                    byte d2 = _memory.Planes[2, physicalAddress];
                    byte d3 = _memory.Planes[3, physicalAddress];

                    if (_state.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode) {
                        if (debug) {
                            sb.Append(d0 == 0 ? '·' : '█');
                            sb.Append(d1 == 0 ? '·' : '█');
                            sb.Append(d2 == 0 ? '·' : '█');
                            sb.Append(d3 == 0 ? '·' : '█');
                        }
                        // Create 4 pixels from them.
                        frameBuffer[destinationAddress++] = GetDacPaletteColor(d0);
                        frameBuffer[destinationAddress++] = GetDacPaletteColor(d1);
                        frameBuffer[destinationAddress++] = GetDacPaletteColor(d2);
                        frameBuffer[destinationAddress++] = GetDacPaletteColor(d3);
                    } else if (_state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode) {
                        // Loop through those bytes and create an index from the 4 planes for each bit.
                        // outputting 8 pixels.
                        for (int i = 7; i >= 0; i--) {
                            int index = (d3 >> i & 1) << 3 | (d2 >> i & 1) << 2 | (d1 >> i & 1) << 1 | d0 >> i & 1;
                            frameBuffer[destinationAddress++] = GetDacPaletteColor(index);
                        }
                    } else {
                        // Text mode
                        byte character = _memory.Planes[0, physicalAddress];
                        byte attribute = _memory.Planes[1, physicalAddress];
                        int fontAddress = 32 * character; // No idea why this seems to work for all character heights. It shouldn't.
                        int xPosition = characterCounter * characterWidth;
                        uint backGroundColor = GetDacPaletteColor(attribute >> 4 & 0b1111);
                        uint foreGroundColor = GetDacPaletteColor(attribute & 0b1111);
                        for (int y = 0; y < characterHeight; y++) {
                            int yPosition = (int)((rowCounter + y) * rowWidth);
                            byte fontByte = _memory.Planes[2, fontAddress + y];
                            for (int x = 0; x < characterWidth; x++) {
                                uint pixel = (fontByte & 0x80 >> x) != 0 ? foreGroundColor : backGroundColor;
                                destinationAddress = yPosition + xPosition + x;
                                frameBuffer[destinationAddress] = pixel;
                            }
                        }
                    }
                    memoryAddress += (characterCounter & characterClockMask) == 0 ? 1 : 0;
                }
            } // End of X loop
            if ((rowCounter & _state.CrtControllerRegisters.CharacterCellHeightRegister.CharacterCellHeight) == 0) {
                if (debug) {
                    sb.Append($"| {rowCounter}");
                    Console.WriteLine(sb.ToString());
                }
                memoryAddress = previousRowMemoryAddress + (_state.CrtControllerRegisters.Offset << offsetShift);
                previousRowMemoryAddress = memoryAddress;
            }
            if (rowCounter == _state.CrtControllerRegisters.LineCompareValue) {
                previousRowMemoryAddress = memoryAddress = 0;
            }
            if (rowCounter == verticalDisplayEnd) {
                overscan = true;
            }
            if (rowCounter == verticalBlankStart) {
                verticalBlanking = true;
            }
            if (rowCounter == verticalSyncStart) {
                _state.GeneralRegisters.InputStatusRegister1.VerticalRetrace = true;
            }
            if (verticalSyncEnd == (rowCounter & 0b1111)) {
                _state.GeneralRegisters.InputStatusRegister1.VerticalRetrace = false;
            }
            if (verticalBlankEnd == (rowCounter & 0b11111111)) {
                verticalBlanking = false;
            }
        } // End of Y loop

        _rendering = false;
    }

    private uint GetDacPaletteColor(int index) {
        switch (_state.AttributeControllerRegisters.AttributeControllerModeRegister.PixelWidth8) {
            case true:
                return _state.DacRegisters.ArgbPalette[index];
            default: {
                int fromPaletteRam6Bits = _state.AttributeControllerRegisters.InternalPalette[index];
                int bits0To3 = fromPaletteRam6Bits & 0b00001111;
                int bits4And5 = _state.AttributeControllerRegisters.AttributeControllerModeRegister.VideoOutput45Select
                    ? _state.AttributeControllerRegisters.ColorSelectRegister.Bits45 << 4
                    : fromPaletteRam6Bits & 0b00110000;
                int bits6And7 = _state.AttributeControllerRegisters.ColorSelectRegister.Bits67 << 6;
                int dacIndex8Bits = bits6And7 | bits4And5 | bits0To3;
                int paletteIndex = dacIndex8Bits & _state.DacRegisters.PixelMask;
                return _state.DacRegisters.ArgbPalette[paletteIndex];
            }
        }
    }
}

file enum MemoryWidth {
    Byte,
    Word13,
    Word15,
    DoubleWord
}