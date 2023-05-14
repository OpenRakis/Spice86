namespace Spice86.Core.Emulator.Devices.Video;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.Video.Registers.CrtController;
using Spice86.Shared.Interfaces;

using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

public class Renderer : IVgaRenderer {
    private readonly IVideoMemory _memory;
    private readonly IVideoState _state;

    private static bool _rendering;

    // private readonly byte[] _frameBuffer = new byte[2000000];
    private readonly ILoggerService _logger;
    private int _previousDacIndex4Bits;

    public Renderer(IVideoState state, IVideoMemory memory, ILoggerService loggerService) {
        _state = state;
        _memory = memory;
        _logger = loggerService.WithLogLevel(LogEventLevel.Verbose);
    }

    public int Width { get; set; }
    public int Height { get; set; }
    public int BitsPerPixel { get; set; }
    public int Stride => BitsPerPixel * Width;
    public int Size => Stride * Height;

    public void Render(IntPtr bufferAddress, int size) {
        throw new NotImplementedException();
    }

    public void Render(Span<uint> frameBuffer) {
        if (_rendering) {
            return;
        }
        _rendering = true;

        int characterWidth = _state.SequencerRegisters.ClockingModeRegister.DotsPerClock;
        int horizontalDisplayEnd = _state.CrtControllerRegisters.HorizontalDisplayEnd + 1;
        int horizontalBlankStart = _state.CrtControllerRegisters.HorizontalBlankingStart;
        int horizontalRetraceStart = _state.CrtControllerRegisters.HorizontalSyncStart;
        int horizontalRetraceEnd = _state.CrtControllerRegisters.HorizontalSyncEndRegister.HorizontalSyncEnd;
        int horizontalBlankEnd = _state.CrtControllerRegisters.HorizontalBlankingEndValue;
        int totalWidth = _state.CrtControllerRegisters.HorizontalTotal + 4;
        int verticalDisplayEnd = _state.CrtControllerRegisters.VerticalDisplayEndValue;
        int verticalBlankStart = _state.CrtControllerRegisters.VerticalBlankingStartValue;
        int verticalSyncStart = _state.CrtControllerRegisters.VerticalSyncStartValue;
        int verticalSyncEnd = _state.CrtControllerRegisters.VerticalSyncEndRegister.VerticalSyncEnd;
        int verticalBlankEnd = _state.CrtControllerRegisters.VerticalBlankingEnd - 1;
        int totalHeight = _state.CrtControllerRegisters.VerticalTotalValue + 2;
        int verticalSize = _state.GeneralRegisters.MiscellaneousOutput.VerticalSize;

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
        int bytesAtAtime;

        bool horizontalBlanking = false;
        bool verticalBlanking = false;
        bool horizontalRetrace = false;
        bool overscan = false;
        int memoryAddress = startAddress;
        int previousRowMemoryAddress = memoryAddress;

        bool chain4 = _state.SequencerRegisters.MemoryModeRegister.Chain4Mode;

        byte[,] palette = _state.DacRegisters.Palette;

        MemoryWidth memoryWidthMode;
        if (_state.CrtControllerRegisters.UnderlineRowScanlineRegister.DoubleWordMode) {
            memoryWidthMode = MemoryWidth.DoubleWord;
            bytesAtAtime = 4;
        } else if (_state.CrtControllerRegisters.CrtModeControlRegister.ByteWordMode == ByteWordMode.Byte) {
            memoryWidthMode = MemoryWidth.Byte;
            bytesAtAtime = 1;
        } else if (_state.CrtControllerRegisters.CrtModeControlRegister.AddressWrap) {
            memoryWidthMode = MemoryWidth.Word15;
            bytesAtAtime = 2;
        } else {
            memoryWidthMode = MemoryWidth.Word13;
            bytesAtAtime = 2;
        }
        int scanLinesPerRowMask = _state.CrtControllerRegisters.CharacterCellHeightRegister.CharacterCellHeight;

        int offsetShift = 1; //_state.CrtControllerRegisters.CrtModeControlRegister.ByteWordMode == ByteWordMode.Word ? 0 : 1;

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
                if (characterCounter == horizontalRetraceStart) {
                    horizontalRetrace = true;
                }
                if (horizontalRetrace && horizontalRetraceEnd == (characterCounter & 0b11111)) {
                    horizontalRetrace = false;
                }
                if (horizontalBlanking && horizontalBlankEnd == (characterCounter & 0b111111)) {
                    horizontalBlanking = false;
                }
                _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = horizontalBlanking || verticalBlanking;
                if (rowCounter < verticalDisplayEnd && characterCounter < horizontalDisplayEnd && (rowCounter & _state.CrtControllerRegisters.CharacterCellHeightRegister.CharacterCellHeight) == 0) {
                    // draw
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
                    byte d0 = _memory.Planes[physicalAddress, 0];
                    byte d1 = _memory.Planes[physicalAddress, 1];
                    byte d2 = _memory.Planes[physicalAddress, 2];
                    byte d3 = _memory.Planes[physicalAddress, 3];

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
                    } else {
                        // Loop through those bytes and create an index from the 4 planes for each bit.
                        // outputting 8 pixels.
                        for (int i = 7; i >= 0; i--) {
                            int index = (d3 >> i & 1) << 3 | (d2 >> i & 1) << 2 | (d1 >> i & 1) << 1 | d0 >> i & 1;
                            frameBuffer[destinationAddress++] = GetDacPaletteColor(index);
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

            // check double scanning
            if (_state.CrtControllerRegisters.CharacterCellHeightRegister.CrtcScanDouble) {
                rowCounter++; // todo: check
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

public enum MemoryWidth {
    Byte,
    Word13,
    Word15,
    DoubleWord
}