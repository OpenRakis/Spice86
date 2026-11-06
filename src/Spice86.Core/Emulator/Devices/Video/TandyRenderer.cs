// Tandy scanout adapted from DOSBox Staging.
// SPDX-FileCopyrightText: 2020-2026 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Video;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Data;
using Spice86.Core.Emulator.Memory;

/// <summary>Renders native Tandy video while sharing completed-frame publication.</summary>
public sealed class TandyRenderer : Renderer {
    private readonly TandyVideoState _tandy;
    private readonly TandyVideoMemory _memory;
    private readonly VgaBlinkState _blinkState;

    /// <summary>Creates the native renderer and registers its RAM-backed video aperture.</summary>
    public TandyRenderer(IMemory memory, IVideoState state, TandyVideoState tandy, VgaBlinkState blinkState,
        ILogger loggerService, IVgaRenderer256Color renderer256Color)
        : base(state, blinkState, loggerService, renderer256Color) {
        _tandy = tandy;
        _blinkState = blinkState;
        _memory = new TandyVideoMemory(memory.Ram, tandy);
        const uint videoBaseAddress = MemoryMap.GraphicVideoMemorySegment << 4;
        memory.RegisterMapping(videoBaseAddress, _memory.Size, _memory);
    }

    /// <inheritdoc />
    protected override VideoFrameInfo CalculateFrameInfo() {
        TandyVideoState.ScanoutParameters scanout = _tandy.Scanout;
        double aspect = 3.0 * scanout.Width / (4.0 * scanout.Height);
        return new VideoFrameInfo(scanout.Width, scanout.Height, aspect);
    }

    /// <inheritdoc />
    protected override bool CorrectPixelAspectRatio => true;

    /// <inheritdoc />
    public override void BeginFrame() {
        BeginUnconditionallyRenderedFrame(CalculateFrameInfo(), _tandy.Scanout.IsGraphics);
    }

    /// <inheritdoc />
    public override void RenderScanline() {
        if (!TryGetNextScanline(out Span<uint> row, out int y)) {
            return;
        }
        if (!_tandy.IsEnabled) {
            row.Fill(_tandy.BlankedColor);
            return;
        }
        TandyVideoState.ScanoutParameters scanout = _tandy.Scanout;
        if (!scanout.IsGraphics) {
            DrawTextRow(row, y, scanout);
            return;
        }
        int bits = scanout.BitsPerPixel;
        int stride = scanout.Width * bits / 8;
        int addressMask = _tandy.LineMask != 0 ? 0x1FFF : 0x1FFFF;
        int address = _tandy.DisplayStart + y / scanout.CharacterHeight * stride;
        int bank = (y % scanout.CharacterHeight & _tandy.LineMask) << 13;
        for (int x = 0; x < row.Length; x++) {
            int pixel;
            if (scanout.IsHighResolutionFourColor) {
                int offset = (address + (x / 8) * 2) & addressMask;
                int low = _memory.ReadDisplay(bank + offset);
                int high = _memory.ReadDisplay(bank + ((offset + 1) & addressMask));
                pixel = ((low >> (7 - (x & 7))) & 1) | (((high >> (7 - (x & 7))) & 1) << 1);
            } else {
                int pixelsPerByte = 8 / bits;
                int offset = (address + x / pixelsPerByte) & addressMask;
                byte data = _memory.ReadDisplay(bank + offset);
                int shift = (pixelsPerByte - 1 - x % pixelsPerByte) * bits;
                pixel = (data >> shift) & ((1 << bits) - 1);
            }
            row[x] = _tandy.GetColor(pixel);
        }
    }

    private void DrawTextRow(Span<uint> row, int y, TandyVideoState.ScanoutParameters scanout) {
        int glyphLine = y % scanout.CharacterHeight;
        int columns = scanout.Width / 8;
        int address = _tandy.DisplayStart + y / scanout.CharacterHeight * columns * 2;
        int cursorAddress = (_tandy.Crtc[14] << 8 | _tandy.Crtc[15]) * 2;
        int cursorStart = _tandy.Crtc[10] & 31;
        int cursorEnd = _tandy.Crtc[11] & 31;
        bool cursorEnabled = _tandy.CursorEnabled && _blinkState.IsBlinkPhaseHigh;
        for (int x = 0; x < row.Length; x++) {
            int cellAddress = (address + x / 8 * 2) & 0x3FFF;
            byte character = _memory.ReadDisplay(cellAddress);
            byte attribute = _memory.ReadDisplay((cellAddress + 1) & 0x3FFF);
            int glyph = glyphLine < 8 ? Fonts.VgaFont8[character * 8 + glyphLine] : 0;
            bool foreground = (glyph & (0x80 >> (x & 7))) != 0;
            if (_tandy.BlinkEnabled && (attribute & 0x80) != 0 && !_blinkState.IsBlinkPhaseHigh) {
                foreground = false;
            }
            if (cursorEnabled && cellAddress == (cursorAddress & 0x3FFF) && glyphLine >= cursorStart && glyphLine <= cursorEnd) {
                foreground = true;
            }
            int backgroundMask = _tandy.BlinkEnabled ? 7 : 15;
            int color = foreground ? attribute & 15 : (attribute >> 4) & backgroundMask;
            row[x] = _tandy.GetColor(color);
        }
    }
}