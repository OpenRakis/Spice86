namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Devices.Video.Registers.CrtController;
using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Devices.Video.Registers.Graphics;
using Spice86.Core.Emulator.Memory;
using Spice86.Logging;

using ClockSelect = Registers.General.MiscellaneousOutput.ClockSelectValue;

/// <inheritdoc cref="IVgaRenderer" />
public class Renderer : IVgaRenderer {
    private readonly VideoMemory _memory;
    private readonly IVideoState _state;
    private readonly VgaBlinkState _blinkState;

    private uint[] _frontBuffer = Array.Empty<uint>();
    private uint[] _backBuffer = Array.Empty<uint>();
    private uint[] _renderBuffer = Array.Empty<uint>();
    // Holds the most recently completed frame. Written only by the renderer
    // CompleteFrame(), read only by the renderer in InitBackBufferFromFront().
    // Never touched by the UI-thread buffer rotation, so it is always exactly one frame old.
    private uint[] _lastPublishedFrame = Array.Empty<uint>();
    private int _hasPendingFrame;

    // Dirty-skip state: true if all scanlines should skip pixel drawing this frame.
    private bool _skipThisFrame;
    private int _frameOutputRows;

    // Per-frame latched state (set in BeginFrame, constant for the frame).
    private int _frameSkew;
    private int _frameCharacterClockMask;
    private MemoryWidth _frameMemoryWidthMode;
    private bool _frameScanLineBit0ForAddressBit13;
    private bool _frameScanLineBit0ForAddressBit14;
    private int _frameVerticalDisplayEnd;
    private int _frameTotalHeight;
    private bool _frameActive;

    // Mutable state (advances per scanline via RenderScanline).
    private int _frameRowMemoryAddressCounter;
    private int _frameDestinationAddress;
    private bool _frameVerticalBlanking;
    private int _frameLineCounter;
    private int _frameCharRowScanline;
    private int _frameDoubleScanIndex;
    private int _frameDestinationAddressLatch;

    // Per-character-row state (re-read from registers at each row boundary).
    private int _frameHorizontalDisplayEnd;
    private int _frameTotalWidth;
    private bool[] _framePlanesEnabled = Array.Empty<bool>();
    private byte _frameMaximumScanline;
    private int _frameDrawLinesPerScanLine;

    /// <summary>
    ///     Create a new VGA renderer.
    /// </summary>
    /// <param name="memory">The video memory implementation.</param>
    /// <param name="state">The video state implementation.</param>
    /// <param name="blinkState">Shared blink state for text-mode attribute blinking.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public Renderer(IMemory memory, IVideoState state, VgaBlinkState blinkState, LoggerService loggerService) {
        _state = state;
        _blinkState = blinkState;
        const uint videoBaseAddress = MemoryMap.GraphicVideoMemorySegment << 4;
        _memory = new VideoMemory(_state, loggerService);
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

    /// <summary>
    ///     Called by <see cref="VgaTimingEngine"/> at frame start on the emulation thread.
    ///     Latches per-frame register values and prepares the back buffer for scanline rendering.
    /// </summary>
    public void BeginFrame() {
        bool isDirty = _memory.HasChanged || _state.IsRenderingDirty || _blinkState.HasChanged;
        _memory.ResetChanged();
        _state.IsRenderingDirty = false;
        _blinkState.ResetChanged();

        _skipThisFrame = !isDirty;
        _frameOutputRows = 0;

        int width = Width;
        int height = Height;
        int requiredSize = width * height;
        if (requiredSize <= 0) {
            _frameActive = false;
            return;
        }
        if (_backBuffer.Length != requiredSize) {
            _backBuffer = new uint[requiredSize];
        }
        BufferSize = requiredSize;
        _frameActive = true;

        _frameVerticalDisplayEnd = _state.CrtControllerRegisters.VerticalDisplayEndValue;
        _frameTotalHeight = _state.CrtControllerRegisters.VerticalTotalValue + 2;
        _frameSkew = _state.CrtControllerRegisters.HorizontalBlankingEndRegister.DisplayEnableSkew;
        _frameCharacterClockMask = _state.CrtControllerRegisters.UnderlineRowScanlineRegister.CountByFour
            ? 3
            : _state.CrtControllerRegisters.CrtModeControlRegister.CountByTwo
                ? 1
                : 0;
        _frameRowMemoryAddressCounter = _state.CrtControllerRegisters.ScreenStartAddress
            + _state.CrtControllerRegisters.PresetRowScanRegister.BytePanning;
        _frameMemoryWidthMode = DetermineMemoryWidthMode();
        _frameScanLineBit0ForAddressBit13 = !_state.CrtControllerRegisters.CrtModeControlRegister.CompatibilityModeSupport;
        _frameScanLineBit0ForAddressBit14 = !_state.CrtControllerRegisters.CrtModeControlRegister.SelectRowScanCounter;

        _frameDestinationAddress = 0;
        _frameVerticalBlanking = false;
        _frameLineCounter = _state.CrtControllerRegisters.PresetRowScanRegister.PresetRowScan;
        _frameCharRowScanline = 0;
        _frameDoubleScanIndex = 0;
        _frameDestinationAddressLatch = 0;

        InitCharRow();
    }

    /// <summary>
    ///     Called by <see cref="VgaTimingEngine"/> for each physical scanline on the emulation thread.
    ///     Renders one horizontal line of pixels from VRAM into the back buffer and advances state.
    /// </summary>
    public void RenderScanline() {
        if (!_frameActive || _frameLineCounter >= _frameTotalHeight) {
            return;
        }

        if (_skipThisFrame && (_memory.HasChanged || _state.IsRenderingDirty)) {
            _skipThisFrame = false;
            InitBackBufferFromFront();
            _frameDestinationAddressLatch = _frameOutputRows * Width;
            _frameDestinationAddress = _frameDestinationAddressLatch;
        }

        Span<uint> frameBuffer = _backBuffer.AsSpan();

        if (_frameDoubleScanIndex == 0) {
            _frameDestinationAddressLatch = _frameDestinationAddress;
        }

        int memoryAddressCounter = _frameRowMemoryAddressCounter;
        _frameDestinationAddress = _frameDestinationAddressLatch;

        bool horizontalBlanking = true;

        if (!_skipThisFrame) {
            // Hoist per-scanline constants out of the per-character loop to avoid
            // repeated interface dispatch and property-chain traversals.
            uint[] paletteMap = _state.DacRegisters.PaletteMap;
            uint[] attrMap = _state.DacRegisters.AttributeMap;
            bool in256ColorMode = _state.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode;
            bool inGraphicsMode = _state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode;

            VgaAddressMapper addressMapper = new VgaAddressMapper(
                _frameMemoryWidthMode,
                _frameScanLineBit0ForAddressBit13,
                _frameScanLineBit0ForAddressBit14,
                _frameCharRowScanline & 1);

            for (int characterCounter = 0; characterCounter < _frameTotalWidth; characterCounter++) {
                if (characterCounter == _frameSkew) {
                    horizontalBlanking = false;
                }
                if (characterCounter == _frameHorizontalDisplayEnd) {
                    horizontalBlanking = true;
                }
                if (horizontalBlanking || _frameVerticalBlanking) {
                    continue;
                }

                if (_frameDestinationAddress + 9 > frameBuffer.Length) {
                    break;
                }

                (byte plane0, byte plane1, byte plane2, byte plane3) = ReadVideoMemory(memoryAddressCounter, addressMapper);

                if (in256ColorMode) {
                    Draw256ColorMode(frameBuffer, paletteMap, ref _frameDestinationAddress, plane0, plane1, plane2, plane3);
                } else if (inGraphicsMode) {
                    DrawGraphicsMode(frameBuffer, attrMap, ref _frameDestinationAddress, plane0, plane1, plane2, plane3);
                } else {
                    DrawTextMode(frameBuffer, attrMap, ref _frameDestinationAddress, plane0, plane1, _frameCharRowScanline);
                }
                memoryAddressCounter += (characterCounter & _frameCharacterClockMask) == 0 ? 1 : 0;
            }
        }

        if (_frameLineCounter == _state.CrtControllerRegisters.LineCompareValue) {
            _frameRowMemoryAddressCounter = 0;
        }
        if (_frameLineCounter == _frameVerticalDisplayEnd) {
            _frameVerticalBlanking = true;
        }

        if (!(_state.CrtControllerRegisters.CrtModeControlRegister.VerticalTimingHalved
              && (_frameCharRowScanline & 1) != 1)) {
            _frameLineCounter++;
        }

        _frameDoubleScanIndex++;
        if (_frameDoubleScanIndex >= _frameDrawLinesPerScanLine) {
            _frameDoubleScanIndex = 0;
            if (!_frameVerticalBlanking) {
                _frameOutputRows++;
            }
            _frameCharRowScanline++;
            if (_frameCharRowScanline > _frameMaximumScanline) {
                _frameCharRowScanline = 0;
                _frameRowMemoryAddressCounter += _state.CrtControllerRegisters.Offset << 1;
                if (_frameLineCounter < _frameTotalHeight) {
                    InitCharRow();
                }
            }
        }
    }

    /// <summary>
    ///     Called by <see cref="VgaTimingEngine"/> at vertical retrace on the emulation thread.
    ///     Publishes the completed back buffer by swapping it with the front buffer.
    /// </summary>
    public void CompleteFrame() {
        if (!_frameActive) {
            return;
        }
        if (_skipThisFrame) {
            _frameActive = false;
            return;
        }
        _backBuffer = Interlocked.Exchange(ref _frontBuffer, _backBuffer);
        _lastPublishedFrame = Volatile.Read(ref _frontBuffer);
        Volatile.Write(ref _hasPendingFrame, 1);
        _frameActive = false;
    }

    /// <inheritdoc />
    public void Render(Span<uint> frameBuffer) {
        if (Interlocked.Exchange(ref _hasPendingFrame, 0) == 0) {
            return;
        }
        _renderBuffer = Interlocked.Exchange(ref _frontBuffer, _renderBuffer);

        uint[] render = Volatile.Read(ref _renderBuffer);
        if (render.Length > 0 && render.Length <= frameBuffer.Length) {
            render.AsSpan().CopyTo(frameBuffer);
        }
    }

    private void InitCharRow() {
        _frameHorizontalDisplayEnd = _state.CrtControllerRegisters.HorizontalDisplayEnd + 1 + _frameSkew;
        _frameTotalWidth = _state.CrtControllerRegisters.HorizontalTotal + 3;
        _framePlanesEnabled = _state.AttributeControllerRegisters.ColorPlaneEnableRegister.PlanesEnabled;
        _frameMaximumScanline = _state.CrtControllerRegisters.MaximumScanlineRegister.MaximumScanline;
        _frameDrawLinesPerScanLine = _state.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble ? 2 : 1;
    }

    private void InitBackBufferFromFront() {
        uint[] last = _lastPublishedFrame;
        if (last.Length == _backBuffer.Length) {
            last.AsSpan().CopyTo(_backBuffer.AsSpan());
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

    private (byte plane0, byte plane1, byte plane2, byte plane3) ReadVideoMemory(int memoryAddressCounter, VgaAddressMapper addressMapper) {
        ushort physicalAddress = addressMapper.ComputeAddress(memoryAddressCounter);

        byte plane0 = (byte)(_framePlanesEnabled[0] ? _memory.Planes[0, physicalAddress] : 0);
        byte plane1 = (byte)(_framePlanesEnabled[1] ? _memory.Planes[1, physicalAddress] : 0);
        byte plane2 = (byte)(_framePlanesEnabled[2] ? _memory.Planes[2, physicalAddress] : 0);
        byte plane3 = (byte)(_framePlanesEnabled[3] ? _memory.Planes[3, physicalAddress] : 0);

        return (plane0, plane1, plane2, plane3);
    }

    private readonly struct VgaAddressMapper {
        private readonly MemoryWidth _mode;
        private readonly ushort _andMask13;
        private readonly ushort _orMask13;
        private readonly ushort _andMask14;
        private readonly ushort _orMask14;

        public VgaAddressMapper(MemoryWidth mode, bool scanLineBit0ForAddressBit13, bool scanLineBit0ForAddressBit14, int scanlineBit0) {
            _mode = mode;
            _andMask13 = scanLineBit0ForAddressBit13 ? (ushort)(~0x2000 & 0xFFFF) : (ushort)0xFFFF;
            _orMask13 = (scanLineBit0ForAddressBit13 && scanlineBit0 != 0) ? (ushort)0x2000 : (ushort)0;
            _andMask14 = scanLineBit0ForAddressBit14 ? (ushort)(~0x4000 & 0xFFFF) : (ushort)0xFFFF;
            _orMask14 = (scanLineBit0ForAddressBit14 && scanlineBit0 != 0) ? (ushort)0x4000 : (ushort)0;
        }

        public ushort ComputeAddress(int counter) {
            ushort p = _mode switch {
                MemoryWidth.Byte => (ushort)counter,
                MemoryWidth.Word13 => (ushort)((counter << 1) | ((counter >> 13) & 1)),
                MemoryWidth.Word15 => (ushort)((counter << 1) | ((counter >> 15) & 1)),
                MemoryWidth.DoubleWord => (ushort)((counter << 2) | ((counter >> 14) & 3)),
                _ => throw new InvalidOperationException($"Unsupported memory width: {_mode}")
            };
            p = (ushort)((p & _andMask13) | _orMask13);
            p = (ushort)((p & _andMask14) | _orMask14);
            return p;
        }
    }

    private void DrawTextMode(Span<uint> frameBuffer, uint[] attrMap, ref int destinationAddress, byte plane0, byte plane1, int scanline) {
        // Text mode
        // Plane 0 contains the character codes.
        int fontAddress = 32 * plane0; // No idea why this seems to work for all character heights. It shouldn't.
        // The byte in plane 1 contains the foreground and background colors.
        uint backGroundColor = attrMap[(plane1 >> 4) & 0xF];
        int index;
        SequencerRegisters sequencer = _state.SequencerRegisters;
        if (sequencer.MemoryModeRegister.ExtendedMemory) {
            // Bit 3 controls which font is used.
            if ((plane1 & 0x8) != 0) {
                fontAddress += sequencer.CharacterMapSelectRegister.CharacterMapA;
            } else {
                fontAddress += sequencer.CharacterMapSelectRegister.CharacterMapB;
            }
            index = plane1 & 0x7;
        } else {
            // Bit 3 controls color intensity.
            index = plane1 & 0xF;
        }
        uint foreGroundColor = attrMap[index];
        if (_state.AttributeControllerRegisters.AttributeControllerModeRegister.BlinkingEnabled
            && (plane1 & 0x80) != 0
            && !_blinkState.IsBlinkPhaseHigh) {
            (foreGroundColor, backGroundColor) = (backGroundColor & 0x7, foreGroundColor);
        }

        int dotsPerClock = sequencer.ClockingModeRegister.DotsPerClock;
        // The 8 pixels to render this line come from the font which is stored in plane 2.
        byte fontByte = _memory.Planes[2, fontAddress + scanline];
        byte mask = 0x80;
        for (int x = 0; x < dotsPerClock; x++, mask >>= 1) {
            uint pixel = (fontByte & mask) != 0 ? foreGroundColor : backGroundColor;
            frameBuffer[destinationAddress++] = pixel;
        }
    }

    private void DrawGraphicsMode(Span<uint> frameBuffer, uint[] attrMap, ref int destinationAddress, byte plane0, byte plane1, byte plane2, byte plane3) {
        // There are 2 graphics modes, Ega compatible and Cga compatible.
        if (_state.GraphicsControllerRegisters.GraphicsModeRegister.ShiftRegisterMode == ShiftRegisterMode.Ega) {
            DrawEgaModeGraphics(frameBuffer, attrMap, ref destinationAddress, plane0, plane1, plane2, plane3);
        } else {
            DrawCgaModeGraphics(frameBuffer, attrMap, ref destinationAddress, plane0, plane1, plane2, plane3);
        }
    }

    private void DrawCgaModeGraphics(Span<uint> frameBuffer, uint[] attrMap, ref int destinationAddress, byte plane0, byte plane1, byte plane2, byte plane3) {
        // Cga mode has a different shift register mode, where the bits are interleaved.
        // First 4 pixels are created from planes 0 and 2.
        for (int bitNr = 6; bitNr >= 0; bitNr -= 2) {
            int index = (plane2 >> bitNr & 3) << 2 | plane0 >> bitNr & 3;
            frameBuffer[destinationAddress++] = attrMap[index];
        }
        // Then 4 pixels are created from planes 1 and 3.
        for (int bitNr = 6; bitNr >= 0; bitNr -= 2) {
            int index = (plane3 >> bitNr & 3) << 2 | plane1 >> bitNr & 3;
            frameBuffer[destinationAddress++] = attrMap[index];
        }
    }

    private void DrawEgaModeGraphics(Span<uint> frameBuffer, uint[] attrMap, ref int destinationAddress, byte plane0, byte plane1, byte plane2, byte plane3) {
        // Loop through those bytes and create an index from the 4 planes for each bit,
        // outputting 8 pixels.
        for (int bitNr = 7; bitNr >= 0; bitNr--) {
            int index = (plane3 >> bitNr & 1) << 3 | (plane2 >> bitNr & 1) << 2 | (plane1 >> bitNr & 1) << 1 | plane0 >> bitNr & 1;
            frameBuffer[destinationAddress++] = attrMap[index];
        }
    }

    private void Draw256ColorMode(Span<uint> frameBuffer, uint[] paletteMap, ref int destinationAddress, byte plane0, byte plane1, byte plane2, byte plane3) {
        // 256-color mode is simply using the video memory bytes directly.
        // Output 8 pixels by drawing each of the 4 pixels twice.
        uint pixel12Color = paletteMap[plane0];
        uint pixel34Color = paletteMap[plane1];
        uint pixel56Color = paletteMap[plane2];
        uint pixel78Color = paletteMap[plane3];
        frameBuffer[destinationAddress++] = pixel12Color;
        frameBuffer[destinationAddress++] = pixel12Color;
        frameBuffer[destinationAddress++] = pixel34Color;
        frameBuffer[destinationAddress++] = pixel34Color;
        frameBuffer[destinationAddress++] = pixel56Color;
        frameBuffer[destinationAddress++] = pixel56Color;
        frameBuffer[destinationAddress++] = pixel78Color;
        frameBuffer[destinationAddress++] = pixel78Color;
    }


}

internal enum MemoryWidth {
    Byte,
    Word13,
    Word15,
    DoubleWord
}