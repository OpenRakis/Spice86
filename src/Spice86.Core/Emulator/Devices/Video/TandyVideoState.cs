// SPDX-FileCopyrightText: 2020-2026 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>Native Tandy gate array and 6845 registers.</summary>
public sealed class TandyVideoState {
    private bool _resizePending;

    /// <summary>Initializes the native scanout latch.</summary>
    public TandyVideoState() {
        Scanout = ReadScanout();
    }

    /// <summary>Requests a coalesced emulation-time drawing setup, in milliseconds.</summary>
    public event Action<int>? ResizeRequested;

    /// <summary>Geometry and beam periods latched by the last drawing setup.</summary>
    public ScanoutParameters Scanout { get; private set; }

    /// <summary>6845 registers, indexed by ports 3D4h/3D5h.</summary>
    public byte[] Crtc { get; } = new byte[32];
    /// <summary>Gate array registers, including the sixteen palette entries.</summary>
    public byte[] Registers { get; } = new byte[32];
    /// <summary>Selected 6845 register.</summary>
    public byte CrtcIndex { get; set; }
    /// <summary>Selected gate array register.</summary>
    public byte RegisterIndex { get; set; }
    /// <summary>CGA-compatible mode control.</summary>
    public byte ModeControl { get; private set; }
    /// <summary>CGA-compatible colour select.</summary>
    public byte ColorSelect { get; set; }
    /// <summary>Last CRT/processor page register value.</summary>
    public byte PageBank { get; private set; }
    /// <summary>Display bank in 16 KiB units.</summary>
    public int DrawBank { get; private set; }
    /// <summary>Processor bank in 16 KiB units.</summary>
    public int MemoryBank { get; private set; }
    /// <summary>Scanline bits selecting 8 KiB banks.</summary>
    public int LineMask { get; private set; }
    /// <summary>Whether extended, dedicated video RAM is selected.</summary>
    public bool ExtendedRam => (Registers[5] & 1) != 0;
    /// <summary>Whether graphics output is selected.</summary>
    public bool IsGraphics => (ModeControl & 2) != 0;
    /// <summary>Whether the display output is enabled.</summary>
    public bool IsEnabled => (ModeControl & 8) != 0;
    /// <summary>Whether attributes blink instead of selecting bright backgrounds.</summary>
    public bool BlinkEnabled => (ModeControl & 0x20) != 0;
    /// <summary>Whether the programmed cursor is visible.</summary>
    public bool CursorEnabled { get; private set; } = true;
    /// <summary>Whether the light pen position has been latched since the last reset.</summary>
    public bool LightPenTriggered { get; set; }
    /// <summary>Whether high resolution interleaved two-bit pixels are selected.</summary>
    public bool IsHighResolutionFourColor => (Registers[3] & 8) != 0 && BitsPerPixel == 2;
    /// <summary>Number of bits in a graphics pixel.</summary>
    public int BitsPerPixel {
        get {
            if ((Registers[3] & 0x10) != 0) {
                return 4;
            }
            return (ModeControl & 0x10) != 0 && (Registers[3] & 8) == 0 ? 1 : 2;
        }
    }
    /// <summary>Physical scanlines in a character row.</summary>
    public int CharacterHeight => (Crtc[9] & 0x1F) + 1;
    /// <summary>Logical output width.</summary>
    public int Width {
        get {
            int columns = Math.Max(1, (int)Crtc[1]);
            if (!IsGraphics) {
                return columns * 8;
            }
            int clocks = columns;
            if (BitsPerPixel == 4) {
                return clocks * ((ModeControl & 0x11) == 0x11 ? 8 : 4);
            }
            if (BitsPerPixel == 1) {
                return clocks * ((ModeControl & 0x10) != 0 ? 16 : 8);
            }
            return clocks * 8;
        }
    }
    /// <summary>Visible scanlines.</summary>
    public int Height => Math.Max(1, (int)Crtc[6]) * CharacterHeight;
    /// <summary>CRTC display start in byte units.</summary>
    public int DisplayStart => (Crtc[12] << 8 | Crtc[13]) * 2;

    /// <summary>Writes mode control and recomputes scanline addressing.</summary>
    public void WriteMode(byte value) {
        byte mode = (byte)(value & 0x3F);
        if (ModeControl == mode) {
            return;
        }
        bool wasGraphics = IsGraphics;
        int previousBits = BitsPerPixel;
        ModeControl = mode;
        CheckLineMask();
        bool immediate = wasGraphics && IsGraphics && IsImmediateFormatChange(previousBits, BitsPerPixel);
        RequestResize(immediate ? 0 : DefaultResizeDelayMs());
    }

    /// <summary>Writes the independent CRT and processor page selectors.</summary>
    public void WritePageBank(byte value) {
        PageBank = value;
        LineMask = value >> 6;
        DrawBank = value & ((LineMask & 2) != 0 ? 6 : 7);
        MemoryBank = (value >> 3) & 7;
        CheckLineMask();
    }

    /// <summary>Writes an indexed gate array register.</summary>
    public void WriteRegister(byte value) {
        int index = RegisterIndex;
        int previousBits = BitsPerPixel;
        if (index is 1 or 2 or 3 or 5) {
            Registers[index] = value;
        } else if (index is >= 0x10 and <= 0x1F) {
            Registers[index] = (byte)(value & 15);
        }
        if (index == 5) {
            CheckLineMask();
        }
        if (index == 3 && IsGraphics && previousBits != BitsPerPixel) {
            RequestResize(IsImmediateFormatChange(previousBits, BitsPerPixel) ? 0 : DefaultResizeDelayMs());
        }
    }

    /// <summary>Preserves the CRTC 17h line-address override.</summary>
    public void WriteCrtc(byte value) {
        if (CrtcIndex >= Crtc.Length) {
            return;
        }
        if (CrtcIndex == 6 && value == 0) {
            return;
        }
        switch (CrtcIndex) {
            case 9:
            case 11:
                value &= 0x1F;
                break;
            case 10:
                CursorEnabled = (value & 0x60) != 0x20;
                value &= 0x3F;
                break;
            case 12:
            case 16:
                value &= 0x3F;
                break;
        }
        byte previous = Crtc[CrtcIndex];
        Crtc[CrtcIndex] = value;
        if (CrtcIndex == 0x17) {
            LineMask = ~value & 3;
        }
        if (value != previous) {
            if (CrtcIndex == 6) {
                RequestResize(50);
            } else if (CrtcIndex is 0 or 1 or 4 or 5 or 9) {
                RequestResize(DefaultResizeDelayMs());
            }
        }
    }

    private static bool IsImmediateFormatChange(int previousBits, int bits) {
        return (previousBits == 4 && bits == 2) || (previousBits == 2 && bits == 4);
    }

    private int DefaultResizeDelayMs() {
        return (int)(Math.Clamp(Scanout.Timing.FrameDurationMs, 1000.0 / 120, 1000.0 / 50) / 2);
    }

    private void RequestResize(int delayMs) {
        if (_resizePending) {
            return;
        }
        if (ResizeRequested == null) {
            CommitResize();
            return;
        }
        _resizePending = true;
        ResizeRequested.Invoke(delayMs);
    }

    /// <summary>Applies all coalesced register changes at the scheduled setup deadline.</summary>
    public void CommitResize() {
        Scanout = ReadScanout();
        _resizePending = false;
    }

    private ScanoutParameters ReadScanout() {
        return new ScanoutParameters(Width, Height, CharacterHeight, IsGraphics, BitsPerPixel,
            IsHighResolutionFourColor, CalculateProgrammedTiming());
    }

    private void CheckLineMask() {
        if (ExtendedRam) {
            LineMask = 0;
        } else if (IsGraphics) {
            LineMask |= 1;
        }
    }

    /// <summary>Returns the physical RGBI colour for a decoded pixel.</summary>
    public uint GetColor(int pixel) {
        int index = pixel & 15;
        if (IsGraphics && BitsPerPixel == 1) {
            index = pixel == 0 ? 0 : ColorSelect & 15;
        } else if (IsGraphics && BitsPerPixel == 2 && !IsHighResolutionFourColor) {
            int colorSet = (ColorSelect & 0x10) >> 1;
            colorSet |= (ColorSelect >> 5) & 1;
            int redMask = 15;
            if ((ModeControl & 4) != 0) {
                colorSet |= 1;
                redMask &= ~1;
            }
            index = pixel switch {
                0 => ColorSelect & 15,
                1 => (2 | colorSet) & Registers[1],
                2 => (4 | (colorSet & redMask)) & Registers[1],
                _ => (6 | colorSet) & Registers[1]
            };
        }
        return RgbColor(Registers[0x10 + index]);
    }

    /// <summary>Colour driven while the video signal is disabled.</summary>
    public uint BlankedColor {
        get {
            if ((Registers[3] & 4) != 0) {
                return RgbColor(Registers[2]);
            }
            int color = IsGraphics && BitsPerPixel == 2 ? Registers[0x10] : 0;
            return RgbColor(color);
        }
    }

    private static uint RgbColor(int value) {
        int color = value & 15;
        int intensity = (color & 8) != 0 ? 0x55 : 0;
        int red = ((color & 4) != 0 ? 0xAA : 0) + intensity;
        int green = ((color & 2) != 0 ? 0xAA : 0) + intensity;
        int blue = ((color & 1) != 0 ? 0xAA : 0) + intensity;
        if (color == 6) {
            green = 0x55;
        }
        return 0xFF000000U | (uint)(red << 16 | green << 8 | blue);
    }

    /// <summary>Returns the beam periods committed by the scheduled drawing setup.</summary>
    public VgaTimingParameters CalculateTiming() {
        return Scanout.Timing;
    }

    private VgaTimingParameters CalculateProgrammedTiming() {
        const int oscillator = 28636360;
        int divisor = (ModeControl & 1) != 0 ? 16 : 32;
        double characterMs = 1000.0 * divisor / oscillator;
        int horizontalTotal = Crtc[0] + 1;
        double lineMs = horizontalTotal * characterMs;
        int totalLines = (Crtc[4] + 1) * CharacterHeight + Crtc[5];
        double displayEnd = Math.Min(Crtc[1], horizontalTotal) * characterMs;
        double syncStart = Crtc[7] * CharacterHeight * lineMs;
        return new VgaTimingParameters {
            PixelClockHz = oscillator / 2,
            HorizontalTotal = horizontalTotal,
            LineDurationMs = lineMs,
            HorizontalDisplayEndMs = displayEnd,
            HorizontalBlankingStartMs = displayEnd,
            HorizontalBlankingEndMs = lineMs,
            HorizontalRetraceStartMs = Crtc[2] * characterMs,
            HorizontalRetraceEndMs = (Crtc[2] + (Crtc[3] & 15)) * characterMs,
            TotalScanlines = Math.Max(1, totalLines),
            VisibleScanlines = Math.Min(Height, Math.Max(1, totalLines)),
            VerticalBlankingStartMs = Height * lineMs,
            VerticalBlankingEndMs = totalLines * lineMs,
            VerticalRetraceStartMs = syncStart,
            VerticalRetraceEndMs = syncStart + 16 * lineMs
        };
    }

    /// <summary>Immutable native drawing setup shared by beam timing and scanout.</summary>
    public sealed record ScanoutParameters(int Width, int Height, int CharacterHeight, bool IsGraphics,
        int BitsPerPixel, bool IsHighResolutionFourColor, VgaTimingParameters Timing);
}