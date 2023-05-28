namespace Spice86.Core.Emulator.Devices.Video;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Devices.Video.Registers.CrtController;
using Spice86.Core.Emulator.Devices.Video.Registers.Graphics;

using System.Diagnostics;

/// <inheritdoc />
public class Renderer : IVgaRenderer {
    private static bool _rendering;
    private readonly IVideoMemory _memory;
    private readonly IVideoState _state;

    /// <summary>
    ///     Create a new VGA renderer.
    /// </summary>
    public Renderer(IVideoState state, IVideoMemory memory) {
        _state = state;
        _memory = memory;
    }

    /// <inheritdoc />
    public int Width => (_state.GeneralRegisters.MiscellaneousOutput.ClockSelect, _state.SequencerRegisters.ClockingModeRegister.HalfDotClock) switch {
        (ClockSelect.Use25175Khz, true) => 320,
        (ClockSelect.Use25175Khz, false) => 640,
        (ClockSelect.Use28322Khz, true) => 360,
        (ClockSelect.Use28322Khz, false) => 720,
        _ => throw new ArgumentOutOfRangeException()
    };

    /// <inheritdoc />
    public int Height => (_state.CrtControllerRegisters.VerticalDisplayEndValue + 1) / (_state.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble ? 2 : 1);

    /// <inheritdoc />
    public int BufferSize { get; private set; }

    /// <inheritdoc />
    public void Render(Span<uint> frameBuffer) {
        if (_rendering) {
            return;
        }
        BufferSize = frameBuffer.Length;
        if (Width * Height > frameBuffer.Length) {
            // Resolution change hasn't caught up yet. Skip a frame.
            return;
        }
        _rendering = true;
        var sw = Stopwatch.StartNew();

        int characterWidth = _state.SequencerRegisters.ClockingModeRegister.DotsPerClock;

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

        bool verticalBlanking = false;
        bool overscan = false;
        int memoryAddressCounter = startAddress + _state.CrtControllerRegisters.PresetRowScanRegister.BytePanning;
        int previousRowMemoryAddress = memoryAddressCounter;

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

        /////////////////////////
        // Start Vertical loop //
        /////////////////////////
        int lineCounter = _state.CrtControllerRegisters.PresetRowScanRegister.PresetRowScan;
        while (lineCounter < totalHeight) {
            byte maximumScanline = _state.CrtControllerRegisters.MaximumScanlineRegister.MaximumScanline;

            int memoryAddressCounterLatch = memoryAddressCounter;

            for (int scanline = 0; scanline <= maximumScanline; scanline++) {
                int destinationAddressLatch = destinationAddress;
                for (int doubleScan = 0; doubleScan < (_state.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble ? 2 : 1); doubleScan++) {
                    memoryAddressCounter = memoryAddressCounterLatch;
                    destinationAddress = destinationAddressLatch;

                    bool horizontalBlanking = false;
                    int horizontalDisplayEnd = _state.CrtControllerRegisters.HorizontalDisplayEnd + 1;
                    int horizontalBlankStart = _state.CrtControllerRegisters.HorizontalBlankingStart;
                    int horizontalBlankEnd = _state.CrtControllerRegisters.HorizontalBlankingEndValue;
                    int totalWidth = _state.CrtControllerRegisters.HorizontalTotal + 3;
                    bool[] planesEnabled = _state.AttributeControllerRegisters.ColorPlaneEnableRegister.PlanesEnabled;

                    ///////////////////////////
                    // Start Horizontal loop //
                    ///////////////////////////
                    for (int characterCounter = 0; characterCounter < totalWidth; characterCounter++) {
                        if (characterCounter == skew && lineCounter < verticalDisplayEnd) {
                            overscan = false;
                        }
                        if (characterCounter == horizontalDisplayEnd + skew || lineCounter >= verticalDisplayEnd) {
                            overscan = true;
                        }
                        if (characterCounter == horizontalBlankStart) {
                            horizontalBlanking = true;
                        }
                        if (horizontalBlanking && horizontalBlankEnd == (characterCounter & 0b111111)) {
                            horizontalBlanking = false;
                        }
                        if (horizontalBlanking || verticalBlanking) {
                            _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = true;
                            // No need to read memory or render pixels.
                            continue;
                        }
                        _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = false;

                        if (overscan) {
                            int pixelsPerCharacterCount = characterWidth / (_state.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode ? 2 : 1);
                            for (int i = 0; i < pixelsPerCharacterCount; i++) {
                                // frameBuffer[destinationAddress++] = GetDacPaletteColor(_state.AttributeControllerRegisters.OverscanColor);
                            }
                            continue;
                        }

                        // No blanking, no overscan, so we're rendering pixels.

                        // Convert logical address to physical address.
                        ushort physicalAddress = memoryWidthMode switch {
                            MemoryWidth.Byte => (ushort)memoryAddressCounter,
                            MemoryWidth.Word13 => (ushort)(memoryAddressCounter << 1 | memoryAddressCounter >> 13 & 1),
                            MemoryWidth.Word15 => (ushort)(memoryAddressCounter << 1 | memoryAddressCounter >> 15 & 1),
                            MemoryWidth.DoubleWord => (ushort)(memoryAddressCounter << 2 | memoryAddressCounter >> 14 & 3)
                        };
                        if (!_state.CrtControllerRegisters.CrtModeControlRegister.CompatibilityModeSupport) {
                            // Use the row scan counter rather than the memory address counter for bit 13.
                            physicalAddress = (ushort)(physicalAddress & ~0x2000 | ((scanline & 1) != 0 ? 0x2000 : 0));
                        }
                        if (!_state.CrtControllerRegisters.CrtModeControlRegister.SelectRowScanCounter) {
                            // Use the row scan counter rather than the memory address counter for bit 14.
                            physicalAddress = (ushort)(physicalAddress & ~0x4000 | ((scanline & 2) != 0 ? 0x4000 : 0));
                        }

                        // if (characterCounter == 0) {
                        //     if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                        //         _logger.Verbose("Line: {LineCounter} ScanLine: {ScanLine} DoubleScan: {DoubleScan} Current MemoryAddress: {MemoryAddress} Physical read address {PhysicalAddress} DestinationAddress: {DestinationAddress}",
                        //             lineCounter, scanline, doubleScan, memoryAddressCounter, physicalAddress, destinationAddress);
                        //     }
                        // }

                        // Read 4 bytes from the 4 planes.
                        byte plane0 = (byte)(planesEnabled[0] ? _memory.Planes[0, physicalAddress] : 0);
                        byte plane1 = (byte)(planesEnabled[1] ? _memory.Planes[1, physicalAddress] : 0);
                        byte plane2 = (byte)(planesEnabled[2] ? _memory.Planes[2, physicalAddress] : 0);
                        byte plane3 = (byte)(planesEnabled[3] ? _memory.Planes[3, physicalAddress] : 0);

                        if (_state.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode) {
                            // Create 8 pixels from them.
                            frameBuffer[destinationAddress++] = GetDacPaletteColor(plane0);
                            frameBuffer[destinationAddress++] = GetDacPaletteColor(plane0);
                            frameBuffer[destinationAddress++] = GetDacPaletteColor(plane1);
                            frameBuffer[destinationAddress++] = GetDacPaletteColor(plane1);
                            frameBuffer[destinationAddress++] = GetDacPaletteColor(plane2);
                            frameBuffer[destinationAddress++] = GetDacPaletteColor(plane2);
                            frameBuffer[destinationAddress++] = GetDacPaletteColor(plane3);
                            frameBuffer[destinationAddress++] = GetDacPaletteColor(plane3);
                        } else if (_state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode) {
                            if (_state.GraphicsControllerRegisters.GraphicsModeRegister.ShiftRegisterMode == ShiftRegisterMode.Ega) {
                                // Loop through those bytes and create an index from the 4 planes for each bit.
                                // outputting 8 pixels.
                                for (int bitNr = 7; bitNr >= 0; bitNr--) {
                                    int index = (plane3 >> bitNr & 1) << 3 | (plane2 >> bitNr & 1) << 2 | (plane1 >> bitNr & 1) << 1 | plane0 >> bitNr & 1;
                                    frameBuffer[destinationAddress++] = GetDacPaletteColor(index);
                                }
                            } else {
                                // Cga mode has a different shift register mode, where the bits are interleaved.
                                for (int bitNr = 6; bitNr >= 0; bitNr -= 2) {
                                    int index = (plane2 >> bitNr & 3) << 2 | plane0 >> bitNr & 3;
                                    frameBuffer[destinationAddress++] = GetDacPaletteColor(index);
                                }
                                for (int bitNr = 6; bitNr >= 0; bitNr -= 2) {
                                    int index = (plane3 >> bitNr & 3) << 2 | plane1 >> bitNr & 3;
                                    frameBuffer[destinationAddress++] = GetDacPaletteColor(index);
                                }
                            }
                        } else {
                            // Text mode
                            int fontAddress = 32 * plane0; // No idea why this seems to work for all character heights. It shouldn't.
                            uint backGroundColor = GetDacPaletteColor(plane1 >> 4 & 0b1111);
                            uint foreGroundColor = GetDacPaletteColor(plane1 & 0b1111);
                            byte fontByte = _memory.Planes[2, fontAddress + scanline];
                            for (int x = 0; x < characterWidth; x++) {
                                uint pixel = (fontByte & 0x80 >> x) != 0 ? foreGroundColor : backGroundColor;
                                frameBuffer[destinationAddress++] = pixel;
                            }
                        }
                        // This increases the memory address counter after each character, taking count-by-two and count-by-four into account.
                        memoryAddressCounter += (characterCounter & characterClockMask) == 0 ? 1 : 0;
                    } // End of X loop

                    if (lineCounter == _state.CrtControllerRegisters.LineCompareValue) {
                        previousRowMemoryAddress = 0;
                    }
                    if (lineCounter == verticalDisplayEnd) {
                        overscan = true;
                        _state.GeneralRegisters.InputStatusRegister1.VerticalRetrace = true;
                    }
                    if (lineCounter == verticalBlankStart) {
                        verticalBlanking = true;
                    }
                    if (lineCounter == verticalSyncStart) {
                        _state.GeneralRegisters.InputStatusRegister1.VerticalRetrace = true;
                    }
                    if (verticalSyncEnd == (lineCounter & 0b1111)) {
                        // _state.GeneralRegisters.InputStatusRegister1.VerticalRetrace = false;
                    }
                    if (verticalBlankEnd == (lineCounter & 0b11111111)) {
                        verticalBlanking = false;
                    }
                    if (!_state.CrtControllerRegisters.CrtModeControlRegister.VerticalTimingHalved || (scanline & 1) == 1) {
                        lineCounter++;
                    }
                } // End of doublescan
            } // end of scanline loop
            memoryAddressCounter = previousRowMemoryAddress + (_state.CrtControllerRegisters.Offset << 1);
            previousRowMemoryAddress = memoryAddressCounter;
        } // End of Y loop

        // if (_logger.IsEnabled(LogEventLevel.Verbose)) {
        //     _logger.Verbose("Rendering frame took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
        // }

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

internal enum MemoryWidth {
    Byte,
    Word13,
    Word15,
    DoubleWord
}