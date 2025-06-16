namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Devices.Video.Registers.CrtController;
using Spice86.Core.Emulator.Devices.Video.Registers.Graphics;
using Spice86.Core.Emulator.Memory;
using ClockSelect = Spice86.Core.Emulator.Devices.Video.Registers.General.MiscellaneousOutput.ClockSelectValue;

using System.Diagnostics;

/// <inheritdoc cref="IVgaRenderer" />
public class Renderer : IVgaRenderer {
    private static readonly object RenderLock = new();
    private readonly VideoMemory _memory;
    private readonly IVideoState _state;

    /// <summary>
    ///     Create a new VGA renderer.
    /// </summary>
    /// <param name="memory">The video memory implementation.</param>
    /// <param name="state">The video state implementation.</param>
    public Renderer(IMemory memory, IVideoState state) {
        _state = state;
        const uint videoBaseAddress = MemoryMap.GraphicVideoMemorySegment << 4;
        _memory = new VideoMemory(_state);
        memory.RegisterMapping(videoBaseAddress, _memory.Size, _memory);
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
    public TimeSpan LastFrameRenderTime { get; private set; }

    /// <inheritdoc />
    public void Render(Span<uint> frameBuffer) {
        if (!Monitor.TryEnter(RenderLock)) {
            // We're already rendering. Get out of here.
            return;
        }
        try {
            BufferSize = frameBuffer.Length;
            if (Width * Height > BufferSize) {
                // Resolution change hasn't caught up yet. Skip a frame.
                return;
            }

            // Some timing helpers.
            long horizontalTickTarget = Stopwatch.Frequency / 31469L; // Number of ticks per horizontal line.
            var waitSpinner = new SpinWait();
            var stopwatch = Stopwatch.StartNew();

            // I _think_ changes to these are ignored during the frame, so we latch them here.
            int verticalDisplayEnd = _state.CrtControllerRegisters.VerticalDisplayEndValue;
            int totalHeight = _state.CrtControllerRegisters.VerticalTotalValue + 2;
            int skew = _state.CrtControllerRegisters.HorizontalBlankingEndRegister.DisplayEnableSkew; // Skew controls the delay of enabling the display at the start of the line.
            int characterClockMask = _state.CrtControllerRegisters.UnderlineRowScanlineRegister.CountByFour
                ? 3
                : _state.CrtControllerRegisters.CrtModeControlRegister.CountByTwo
                    ? 1
                    : 0;
            int rowMemoryAddressCounter = _state.CrtControllerRegisters.ScreenStartAddress + _state.CrtControllerRegisters.PresetRowScanRegister.BytePanning;

            // Memory reading parameters.
            MemoryWidth memoryWidthMode = DetermineMemoryWidthMode();
            bool scanLineBit0ForAddressBit13 = !_state.CrtControllerRegisters.CrtModeControlRegister.CompatibilityModeSupport;
            bool scanLineBit0ForAddressBit14 = !_state.CrtControllerRegisters.CrtModeControlRegister.SelectRowScanCounter;

            _state.GeneralRegisters.InputStatusRegister1.VerticalRetrace = false;
            _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = true;
            int destinationAddress = 0;
            bool verticalBlanking = false;

            /////////////////////////
            // Start Vertical loop //
            /////////////////////////
            int lineCounter = _state.CrtControllerRegisters.PresetRowScanRegister.PresetRowScan;
            while (lineCounter < totalHeight) {
                // These registers can change mid-frame, so we need to check them every scanline.
                // I _think_ changes to these are ignored during a scanline, so we latch them here. 
                int horizontalDisplayEnd = _state.CrtControllerRegisters.HorizontalDisplayEnd + 1 + skew;
                int totalWidth = _state.CrtControllerRegisters.HorizontalTotal + 3;
                bool[] planesEnabled = _state.AttributeControllerRegisters.ColorPlaneEnableRegister.PlanesEnabled;
                byte maximumScanline = _state.CrtControllerRegisters.MaximumScanlineRegister.MaximumScanline;
                int drawLinesPerScanLine = _state.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble ? 2 : 1;

                // For every row we can have multiple lines. In text-modes this is used for character height, and in graphics
                // modes this is used for double-scanning.
                for (int scanline = 0; scanline <= maximumScanline; scanline++) {
                    // We latch the destination address here, so that the double-scan lines are drawn on top of each other.
                    int destinationAddressLatch = destinationAddress;
                    for (int doubleScan = 0; doubleScan < drawLinesPerScanLine; doubleScan++) {
                        int memoryAddressCounter = rowMemoryAddressCounter;
                        destinationAddress = destinationAddressLatch;

                        long ticksAtStartOfRow = stopwatch.ElapsedTicks;
                        bool horizontalBlanking = true;

                        ///////////////////////////
                        // Start Horizontal loop //
                        ///////////////////////////
                        // We loop through the entire horizontal line, even if we're in blanking. This allows programs
                        // to detect the brief disabling and enabling of the display during horizontal blanking.
                        for (int characterCounter = 0; characterCounter < totalWidth; characterCounter++) {
                            // Skew controls the delay of enabling the display at the start of the line.
                            if (characterCounter == skew) {
                                horizontalBlanking = false;
                            }
                            // For simplicity we ignore horizontal blanking registers and use the display end register.
                            if (characterCounter == horizontalDisplayEnd) {
                                horizontalBlanking = true;
                            }
                            if (horizontalBlanking || verticalBlanking) {
                                _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = true;
                                // No need to read memory or render pixels.
                                continue;
                            }

                            // No blanking, so we're rendering pixels.
                            _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = false;

                            (byte plane0, byte plane1, byte plane2, byte plane3) = ReadVideoMemory(memoryWidthMode, memoryAddressCounter, scanLineBit0ForAddressBit13, scanline, scanLineBit0ForAddressBit14, planesEnabled);

                            // Convert the 4 bytes into 8 pixels.
                            if (_state.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode) {
                                Draw256ColorMode(frameBuffer, ref destinationAddress, plane0, plane1, plane2, plane3);
                            } else if (_state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode) {
                                DrawGraphicsMode(frameBuffer, ref destinationAddress, plane0, plane1, plane2, plane3);
                            } else {
                                DrawTextMode(frameBuffer, ref destinationAddress, plane0, plane1, scanline);
                            }
                            // This increases the memory address counter after each character, taking count-by-two and count-by-four into account.
                            memoryAddressCounter += (characterCounter & characterClockMask) == 0 ? 1 : 0;
                        } // End of horizontal loop

                        // When the LineCompare register value is reached, the video memory address is reset to 0. This allows a split-screen effect.
                        if (lineCounter == _state.CrtControllerRegisters.LineCompareValue) {
                            rowMemoryAddressCounter = 0;
                        }
                        // To maximize the time spent in vertical retrace, we start it at the end of display.
                        if (lineCounter == verticalDisplayEnd) {
                            _state.GeneralRegisters.InputStatusRegister1.VerticalRetrace = true;
                            verticalBlanking = true;
                        }

                        // We wait at the end of each line to create the correct horizontal timing of 31.46875 kHz
                        // This allows programs running in the CPU thread to detect the horizontal retrace.
                        while (stopwatch.ElapsedTicks - ticksAtStartOfRow < horizontalTickTarget) {
                            waitSpinner.SpinOnce(-1);
                        }
                        // If the VerticalTiming is halved, we only increase the line counter every other scanline.
                        if (_state.CrtControllerRegisters.CrtModeControlRegister.VerticalTimingHalved && (scanline & 1) != 1) {
                            continue;
                        }
                        lineCounter++;
                    } // End of doubleScan
                } // end of scanline loop

                // Rather than simply continuing increasing memory addresses, an offset is added at the end of each line.
                // This allows creating a "virtual screen" that is larger than the displayed area.
                rowMemoryAddressCounter += _state.CrtControllerRegisters.Offset << 1;
            } // End of vertical loop

            LastFrameRenderTime = stopwatch.Elapsed;
        } catch (IndexOutOfRangeException) {
            // Resolution changed during rendering, discard the rest of this frame.
        } finally {
            Monitor.Exit(RenderLock);
        }
    }

    private MemoryWidth DetermineMemoryWidthMode() {
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
        return memoryWidthMode;
    }

    private (byte plane0, byte plane1, byte plane2, byte plane3) ReadVideoMemory(
        MemoryWidth memoryWidthMode, int memoryAddressCounter,
        bool scanLineBit0ForAddressBit13, int scanline,
        bool scanLineBit0ForAddressBit14, ReadOnlySpan<bool> planesEnabled) {
        // Convert logical address to physical address.
        ushort physicalAddress = memoryWidthMode switch {
            MemoryWidth.Byte => (ushort)memoryAddressCounter,
            MemoryWidth.Word13 => (ushort)(memoryAddressCounter << 1 | memoryAddressCounter >> 13 & 1),
            MemoryWidth.Word15 => (ushort)(memoryAddressCounter << 1 | memoryAddressCounter >> 15 & 1),
            MemoryWidth.DoubleWord => (ushort)(memoryAddressCounter << 2 | memoryAddressCounter >> 14 & 3)
        };
        if (scanLineBit0ForAddressBit13) {
            // Use the scan line counter rather than the memory address counter for bit 13.
            // In effect this causes all odd scanline reads to be read from memory address + 0x2000.
            physicalAddress = (ushort)(physicalAddress & ~0x2000 | ((scanline & 1) != 0 ? 0x2000 : 0));
        }
        if (scanLineBit0ForAddressBit14) {
            // Use the scan line counter rather than the memory address counter for bit 14.
            // In effect this causes all odd scanline reads to be read from memory address + 0x4000.
            physicalAddress = (ushort)(physicalAddress & ~0x4000 | ((scanline & 1) != 0 ? 0x4000 : 0));
        }

        // Read 4 bytes from the 4 planes.
        byte plane0 = (byte)(planesEnabled[0] ? _memory.Planes[0, physicalAddress] : 0);
        byte plane1 = (byte)(planesEnabled[1] ? _memory.Planes[1, physicalAddress] : 0);
        byte plane2 = (byte)(planesEnabled[2] ? _memory.Planes[2, physicalAddress] : 0);
        byte plane3 = (byte)(planesEnabled[3] ? _memory.Planes[3, physicalAddress] : 0);

        return (plane0, plane1, plane2, plane3);
    }

    private void DrawTextMode(Span<uint> frameBuffer, ref int destinationAddress, byte plane0, byte plane1, int scanline) {
        // Text mode
        // Plane 0 contains the character codes.
        int fontAddress = 32 * plane0; // No idea why this seems to work for all character heights. It shouldn't.
        // The byte in plane 1 contains the foreground and background colors.
        uint backGroundColor = GetDacPaletteColor(plane1 >> 4 & 0b1111);
        int index;
        if (_state.SequencerRegisters.MemoryModeRegister.ExtendedMemory) {
            // Bit 3 controls which font is used.
            if ((plane1 & 0x8) != 0) {
                fontAddress += _state.SequencerRegisters.CharacterMapSelectRegister.CharacterMapA;
            } else {
                fontAddress += _state.SequencerRegisters.CharacterMapSelectRegister.CharacterMapB;
            }
            index = plane1 & 0x7;
        } else {
            // Bit 3 controls color intensity.
            index = plane1 & 0xF;
        }
        uint foreGroundColor = GetDacPaletteColor(index);
        if (_state.AttributeControllerRegisters.AttributeControllerModeRegister.BlinkingEnabled
            && (plane1 & 0x80) != 0
            && DateTime.UtcNow.Millisecond % 1000 < 500) {
            // Blinking is enabled and the blink bit is set and the current time is in the first half of the second.
            // Swap the foreground and background colors.
            (foreGroundColor, backGroundColor) = (backGroundColor & 0x7, foreGroundColor);
        }
        // The 8 pixels to render this line come from the font which is stored in plane 2.
        byte fontByte = _memory.Planes[2, fontAddress + scanline];
        for (int x = 0; x < _state.SequencerRegisters.ClockingModeRegister.DotsPerClock; x++) {
            uint pixel = (fontByte & 0x80 >> x) != 0 ? foreGroundColor : backGroundColor;
            frameBuffer[destinationAddress++] = pixel;
        }
    }

    private void DrawGraphicsMode(Span<uint> frameBuffer, ref int destinationAddress, byte plane0, byte plane1, byte plane2, byte plane3) {
        // There are 2 graphics modes, Ega compatible and Cga compatible.
        if (_state.GraphicsControllerRegisters.GraphicsModeRegister.ShiftRegisterMode == ShiftRegisterMode.Ega) {
            DrawEgaModeGraphics(frameBuffer, ref destinationAddress, plane0, plane1, plane2, plane3);
        } else {
            DrawCgaModeGraphics(frameBuffer, ref destinationAddress, plane0, plane1, plane2, plane3);
        }
    }

    private void DrawCgaModeGraphics(Span<uint> frameBuffer, ref int destinationAddress, byte plane0, byte plane1, byte plane2, byte plane3) {
        // Cga mode has a different shift register mode, where the bits are interleaved.
        // First 4 pixels are created from planes 0 and 2.
        for (int bitNr = 6; bitNr >= 0; bitNr -= 2) {
            int index = (plane2 >> bitNr & 3) << 2 | plane0 >> bitNr & 3;
            frameBuffer[destinationAddress++] = GetDacPaletteColor(index);
        }
        // Then 4 pixels are created from planes 1 and 3.
        for (int bitNr = 6; bitNr >= 0; bitNr -= 2) {
            int index = (plane3 >> bitNr & 3) << 2 | plane1 >> bitNr & 3;
            frameBuffer[destinationAddress++] = GetDacPaletteColor(index);
        }
    }

    private void DrawEgaModeGraphics(Span<uint> frameBuffer, ref int destinationAddress, byte plane0, byte plane1, byte plane2, byte plane3) {
        // Loop through those bytes and create an index from the 4 planes for each bit,
        // outputting 8 pixels.
        for (int bitNr = 7; bitNr >= 0; bitNr--) {
            int index = (plane3 >> bitNr & 1) << 3 | (plane2 >> bitNr & 1) << 2 | (plane1 >> bitNr & 1) << 1 | plane0 >> bitNr & 1;
            frameBuffer[destinationAddress++] = GetDacPaletteColor(index);
        }
    }

    private void Draw256ColorMode(Span<uint> frameBuffer, ref int destinationAddress, byte plane0, byte plane1, byte plane2, byte plane3) {
        // 256-color mode is simply using the video memory bytes directly.
        // Output 8 pixels by drawing each of the 4 pixels twice.
        uint pixel12Color = GetDacPaletteColor(plane0);
        uint pixel34Color = GetDacPaletteColor(plane1);
        uint pixel56Color = GetDacPaletteColor(plane2);
        uint pixel78Color = GetDacPaletteColor(plane3);
        frameBuffer[destinationAddress++] = pixel12Color;
        frameBuffer[destinationAddress++] = pixel12Color;
        frameBuffer[destinationAddress++] = pixel34Color;
        frameBuffer[destinationAddress++] = pixel34Color;
        frameBuffer[destinationAddress++] = pixel56Color;
        frameBuffer[destinationAddress++] = pixel56Color;
        frameBuffer[destinationAddress++] = pixel78Color;
        frameBuffer[destinationAddress++] = pixel78Color;
    }

    private uint GetDacPaletteColor(int index) {
        switch (_state.AttributeControllerRegisters.AttributeControllerModeRegister.PixelWidth8) {
            case true:
                return _state.DacRegisters.ArgbPalette[index];
            default: {
                int fromPaletteRam6Bits = _state.AttributeControllerRegisters.InternalPalette[index & 0x0F];
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