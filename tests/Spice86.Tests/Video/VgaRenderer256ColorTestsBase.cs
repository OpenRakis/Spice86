namespace Spice86.Tests.Video;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers.CrtController;
using Spice86.Core.Emulator.Memory;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

using Xunit;

using ClockSelect = Spice86.Core.Emulator.Devices.Video.Registers.General.MiscellaneousOutput.ClockSelectValue;

/// <summary>
///     Base class for 256-color renderer tests, parameterized by renderer implementation.
///     Concrete subclasses supply a specific <see cref="IVgaRenderer256Color"/> and report
///     whether the required CPU instruction set is available.
/// </summary>
public abstract class VgaRenderer256ColorTestsBase {
    /// <summary>
    ///     Returns true when the renderer under test is runnable on the current machine.
    /// </summary>
    protected abstract bool IsSupported { get; }

    /// <summary>
    ///     Creates the renderer implementation under test.
    /// </summary>
    protected abstract IVgaRenderer256Color Create256ColorRenderer();

    /// <summary>
    ///     Skips the current test when the required instruction set is not supported.
    /// </summary>
    protected void EnsureSupported() =>
        Skip.IfNot(IsSupported, "The required CPU instruction set is not supported on this machine.");

    protected (Renderer renderer, VideoMemory videoMemory) CreateRenderer(VideoState state) {
        IMemory mockMemory = Substitute.For<IMemory>();
        VideoMemory? captured = null;
        mockMemory.When(m => m.RegisterMapping(Arg.Any<uint>(), Arg.Any<uint>(), Arg.Any<IMemoryDevice>()))
            .Do(callInfo => captured = (VideoMemory)callInfo.ArgAt<IMemoryDevice>(2));

        LoggerService loggerService = new();
        VgaBlinkState blinkState = new();
        IVgaRenderer256Color renderer256Color = Create256ColorRenderer();
        Renderer renderer = new(mockMemory, state, blinkState, loggerService, renderer256Color);

        if (captured is null) {
            throw new InvalidOperationException("VideoMemory was not captured from RegisterMapping call.");
        }

        return (renderer, captured);
    }

    // ─────────── Scalar doubled rendering ───────────

    [SkippableFact]
    public void Render256Color_ScalarDoubled_SinglePixel() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        vram.Planes[0, 0] = 42;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // Each plane byte → 2 identical pixels
        frame[0].Should().Be(state.DacRegisters.PaletteMap[42]);
        frame[1].Should().Be(state.DacRegisters.PaletteMap[42]);
    }

    [SkippableFact]
    public void Render256Color_AllFourPlanes_ProduceEightPixels() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        vram.Planes[0, 0] = 10;
        vram.Planes[1, 0] = 20;
        vram.Planes[2, 0] = 30;
        vram.Planes[3, 0] = 40;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.PaletteMap[10]);
        frame[1].Should().Be(state.DacRegisters.PaletteMap[10]);
        frame[2].Should().Be(state.DacRegisters.PaletteMap[20]);
        frame[3].Should().Be(state.DacRegisters.PaletteMap[20]);
        frame[4].Should().Be(state.DacRegisters.PaletteMap[30]);
        frame[5].Should().Be(state.DacRegisters.PaletteMap[30]);
        frame[6].Should().Be(state.DacRegisters.PaletteMap[40]);
        frame[7].Should().Be(state.DacRegisters.PaletteMap[40]);
    }

    [SkippableFact]
    public void Render256Color_MultipleCharacters_Contiguous() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // Two character positions
        vram.Planes[0, 0] = 1;
        vram.Planes[1, 0] = 2;
        vram.Planes[2, 0] = 3;
        vram.Planes[3, 0] = 4;

        vram.Planes[0, 1] = 5;
        vram.Planes[1, 1] = 6;
        vram.Planes[2, 1] = 7;
        vram.Planes[3, 1] = 8;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // First character: 8 pixels
        frame[0].Should().Be(state.DacRegisters.PaletteMap[1]);
        frame[1].Should().Be(state.DacRegisters.PaletteMap[1]);
        frame[6].Should().Be(state.DacRegisters.PaletteMap[4]);
        frame[7].Should().Be(state.DacRegisters.PaletteMap[4]);

        // Second character: starts at pixel 8
        frame[8].Should().Be(state.DacRegisters.PaletteMap[5]);
        frame[9].Should().Be(state.DacRegisters.PaletteMap[5]);
        frame[14].Should().Be(state.DacRegisters.PaletteMap[8]);
        frame[15].Should().Be(state.DacRegisters.PaletteMap[8]);
    }

    [SkippableFact]
    public void Render256Color_FullScanline_AllPixelsCorrect() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // Fill all 40 characters (160 VRAM bytes) with sequential values
        for (int charPos = 0; charPos < 40; charPos++) {
            for (int plane = 0; plane < 4; plane++) {
                vram.Planes[plane, charPos] = (byte)(charPos * 4 + plane);
            }
        }

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // Verify all 320 pixels
        for (int charPos = 0; charPos < 40; charPos++) {
            for (int plane = 0; plane < 4; plane++) {
                int pixelIndex = charPos * 8 + plane * 2;
                byte expectedPaletteIndex = (byte)(charPos * 4 + plane);
                uint expectedColor = state.DacRegisters.PaletteMap[expectedPaletteIndex];

                frame[pixelIndex].Should().Be(expectedColor,
                    $"pixel {pixelIndex} (char {charPos}, plane {plane}) should map to palette index {expectedPaletteIndex}");
                frame[pixelIndex + 1].Should().Be(expectedColor,
                    $"pixel {pixelIndex + 1} (doubled) should match");
            }
        }
    }

    [SkippableFact]
    public void Render256Color_MultipleScanlines_RowAddressAdvances() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(3);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // Line 0 at address 0
        vram.Planes[0, 0] = 10;
        // Line 1: row address advances by Offset<<1 = 80
        vram.Planes[0, 80] = 20;
        // Line 2: address = 160
        vram.Planes[0, 160] = 30;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        int width = renderer.Width; // 320
        frame[0].Should().Be(state.DacRegisters.PaletteMap[10], "line 0");
        frame[width].Should().Be(state.DacRegisters.PaletteMap[20], "line 1");
        frame[2 * width].Should().Be(state.DacRegisters.PaletteMap[30], "line 2");
    }

    [SkippableFact]
    public void Render256Color_WithScreenStartAddress_OffsetsCorrectly() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        state.CrtControllerRegisters.ScreenStartAddress = 5;
        vram.Planes[0, 5] = 99;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.PaletteMap[99], "should start from ScreenStartAddress");
    }

    [SkippableFact]
    public void Render256Color_PaletteMapApplied() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);

        uint specificColor = 0xFF_AA_BB_CC;
        state.DacRegisters.PaletteMap[77] = specificColor;
        vram.Planes[0, 0] = 77;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(specificColor);
        frame[1].Should().Be(specificColor);
    }

    [SkippableFact]
    public void Render256Color_ZeroValuePixels_UsesPaletteZero() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory _) = CreateRenderer(state);

        uint backgroundColor = 0xFF_00_00_00;
        state.DacRegisters.PaletteMap[0] = backgroundColor;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(backgroundColor, "zero VRAM values should use palette index 0");
    }

    [SkippableFact]
    public void Render256Color_MaxPaletteIndex_Works() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);

        uint maxColor = 0xFF_FF_FF_FF;
        state.DacRegisters.PaletteMap[255] = maxColor;
        vram.Planes[0, 0] = 255;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(maxColor);
    }

    [SkippableFact]
    public void Render256Color_DirtySkip_StillWorks() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        vram.Planes[0, 0] = 42;
        state.IsRenderingDirty = true;
        RenderFullFrame(renderer, state);
        uint[] first = new uint[renderer.Width * renderer.Height];
        renderer.Render(first);
        first[0].Should().Be(state.DacRegisters.PaletteMap[42]);

        // Second frame with no changes → skip
        RenderFullFrame(renderer, state);
        uint[] second = new uint[renderer.Width * renderer.Height];
        System.Array.Fill(second, 0xDEADBEEF);
        renderer.Render(second);
        second[0].Should().Be(0xDEADBEEF, "skipped frame should not overwrite");
    }

    [SkippableFact]
    public void Render256Color_BytePanning_OffsetsStart() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        state.CrtControllerRegisters.PresetRowScanRegister.BytePanning = 2;
        state.CrtControllerRegisters.ScreenStartAddress = 0;

        // With panning=2, start address becomes 0+2=2
        vram.Planes[0, 2] = 88;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.PaletteMap[88], "byte panning offsets 256-color start");
    }

    [SkippableFact]
    public void Render256Color_ConsecutiveFrames_UpdateCorrectly() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // Frame 1
        vram.Planes[0, 0] = 10;
        state.IsRenderingDirty = true;
        uint[] f1 = RenderAndCapture(renderer, state);
        f1[0].Should().Be(state.DacRegisters.PaletteMap[10]);

        // Frame 2: different data
        vram.Planes[0, 0] = 200;
        state.IsRenderingDirty = true;
        uint[] f2 = RenderAndCapture(renderer, state);
        f2[0].Should().Be(state.DacRegisters.PaletteMap[200]);
    }

    // ─────────── DoubleWord / Word addressing modes ───────────

    [SkippableFact]
    public void Render256Color_DoubleWordMode_ReadsCorrectPhysicalAddresses() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        // Enable DoubleWord addressing: physical = counter << 2.
        // Counter 0 → physical 0, counter 1 → physical 4, counter 2 → physical 8.
        // VRam layout: physical P → VRam[P*4 .. P*4+3].
        state.CrtControllerRegisters.UnderlineRowScanlineRegister.DoubleWordMode = true;
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // Char 0 reads counter=0 → physical=0 → VRam[0..3]
        vram.Planes[0, 0] = 10;
        vram.Planes[1, 0] = 20;
        vram.Planes[2, 0] = 30;
        vram.Planes[3, 0] = 40;

        // Char 1 reads counter=1 → physical=4 → VRam[16..19]
        vram.Planes[0, 4] = 50;
        vram.Planes[1, 4] = 60;
        vram.Planes[2, 4] = 70;
        vram.Planes[3, 4] = 80;

        // Poison the contiguous location that would be read by
        // a buggy implementation assuming stride-1 physical addressing.
        // Physical address 1 → VRam[4..7] (wrong for char 1).
        vram.Planes[0, 1] = 0xAA;
        vram.Planes[1, 1] = 0xBB;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // Char 0 pixels
        frame[0].Should().Be(state.DacRegisters.PaletteMap[10], "char 0, plane 0");
        frame[2].Should().Be(state.DacRegisters.PaletteMap[20], "char 0, plane 1");
        frame[4].Should().Be(state.DacRegisters.PaletteMap[30], "char 0, plane 2");
        frame[6].Should().Be(state.DacRegisters.PaletteMap[40], "char 0, plane 3");

        // Char 1 pixels — must use physical address 4, NOT physical address 1
        frame[8].Should().Be(state.DacRegisters.PaletteMap[50], "char 1, plane 0 — DoubleWord addressing");
        frame[10].Should().Be(state.DacRegisters.PaletteMap[60], "char 1, plane 1 — DoubleWord addressing");
        frame[12].Should().Be(state.DacRegisters.PaletteMap[70], "char 1, plane 2 — DoubleWord addressing");
        frame[14].Should().Be(state.DacRegisters.PaletteMap[80], "char 1, plane 3 — DoubleWord addressing");
    }

    [SkippableFact]
    public void Render256Color_Word13Mode_ReadsCorrectPhysicalAddresses() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        // Word13 mode: physical = (counter << 1) | (counter >> 13 & 1).
        // Counter 0 → physical 0, counter 1 → physical 2, counter 2 → physical 4.
        state.CrtControllerRegisters.CrtModeControlRegister.ByteWordMode = ByteWordMode.Word;
        state.CrtControllerRegisters.CrtModeControlRegister.AddressWrap = false;
        state.CrtControllerRegisters.UnderlineRowScanlineRegister.DoubleWordMode = false;
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // Char 0 reads counter=0 → physical=0 → VRam[0..3]
        vram.Planes[0, 0] = 11;
        vram.Planes[1, 0] = 22;
        vram.Planes[2, 0] = 33;
        vram.Planes[3, 0] = 44;

        // Char 1 reads counter=1 → physical=2 → VRam[8..11]
        vram.Planes[0, 2] = 55;
        vram.Planes[1, 2] = 66;
        vram.Planes[2, 2] = 77;
        vram.Planes[3, 2] = 88;

        // Poison location that a contiguous reader would pick up:
        // physical 1 → VRam[4..7]
        vram.Planes[0, 1] = 0xCC;
        vram.Planes[1, 1] = 0xDD;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.PaletteMap[11], "char 0, plane 0");
        frame[2].Should().Be(state.DacRegisters.PaletteMap[22], "char 0, plane 1");

        frame[8].Should().Be(state.DacRegisters.PaletteMap[55], "char 1, plane 0 — Word13 addressing");
        frame[10].Should().Be(state.DacRegisters.PaletteMap[66], "char 1, plane 1 — Word13 addressing");
        frame[12].Should().Be(state.DacRegisters.PaletteMap[77], "char 1, plane 2 — Word13 addressing");
        frame[14].Should().Be(state.DacRegisters.PaletteMap[88], "char 1, plane 3 — Word13 addressing");
    }

    [SkippableFact]
    public void Render256Color_CountByFour_RepeatsAddressForGroupedCharacters() {
        EnsureSupported();
        // With CountByFour (mask=3), the memory address counter advances only
        // every 4th character clock.  Groups of characters share the same
        // VRAM address and must produce identical pixel data.
        VideoState state = ConfigureGraphics256Color(1);
        state.CrtControllerRegisters.UnderlineRowScanlineRegister.CountByFour = true;
        state.CrtControllerRegisters.HorizontalDisplayEnd = 7;
        state.CrtControllerRegisters.HorizontalTotal = 10;
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // Counter advances: char 0 reads C=0, advance → C=1.
        // Chars 1-3 read C=1 (no advance). Char 4 reads C=1, advance → C=2.
        // Chars 5-7 read C=2 (no advance).
        vram.Planes[0, 0] = 10;  // read by char 0
        vram.Planes[0, 1] = 50;  // read by chars 1–4
        vram.Planes[0, 2] = 90;  // read by chars 5–7

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // Char 0: unique address (counter=0)
        frame[0].Should().Be(state.DacRegisters.PaletteMap[10], "char 0 uses counter 0");

        // Chars 1–4: all share counter=1
        frame[8].Should().Be(state.DacRegisters.PaletteMap[50], "char 1 uses counter 1");
        frame[16].Should().Be(state.DacRegisters.PaletteMap[50], "char 2 uses counter 1");
        frame[24].Should().Be(state.DacRegisters.PaletteMap[50], "char 3 uses counter 1");
        frame[32].Should().Be(state.DacRegisters.PaletteMap[50], "char 4 uses counter 1");

        // Chars 5–7: all share counter=2
        frame[40].Should().Be(state.DacRegisters.PaletteMap[90], "char 5 uses counter 2");
        frame[48].Should().Be(state.DacRegisters.PaletteMap[90], "char 6 uses counter 2");
        frame[56].Should().Be(state.DacRegisters.PaletteMap[90], "char 7 uses counter 2");
    }

    // ─────────── Helpers ───────────

    protected static void RenderFullFrame(Renderer renderer, VideoState state) {
        renderer.BeginFrame();
        int totalLines = state.CrtControllerRegisters.VerticalTotalValue + 10;
        for (int i = 0; i < totalLines; i++) {
            renderer.RenderScanline();
        }
        renderer.CompleteFrame();
    }

    protected static uint[] RenderAndCapture(Renderer renderer, VideoState state) {
        RenderFullFrame(renderer, state);
        uint[] output = new uint[renderer.Width * renderer.Height];
        renderer.Render(output);
        return output;
    }

    protected static VideoState ConfigureGraphics256Color(int lines) {
        VideoState state = new();
        state.GeneralRegisters.MiscellaneousOutput.ClockSelect = ClockSelect.Use25175Khz;
        state.SequencerRegisters.ClockingModeRegister.HalfDotClock = true;
        state.SequencerRegisters.ClockingModeRegister.DotsPerClock = 8;
        state.AttributeControllerRegisters.ColorPlaneEnableRegister.Value = 0x0F;
        state.CrtControllerRegisters.VerticalDisplayEnd = (byte)(lines - 1);
        state.CrtControllerRegisters.VerticalTotal = (byte)(lines - 1);
        state.CrtControllerRegisters.MaximumScanlineRegister.MaximumScanline = 0;
        state.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble = false;
        state.CrtControllerRegisters.HorizontalDisplayEnd = 39;
        state.CrtControllerRegisters.HorizontalTotal = 49;
        state.CrtControllerRegisters.Offset = 40;
        state.CrtControllerRegisters.CrtModeControlRegister.ByteWordMode = ByteWordMode.Byte;
        state.CrtControllerRegisters.CrtModeControlRegister.CompatibilityModeSupport = true;
        state.CrtControllerRegisters.CrtModeControlRegister.SelectRowScanCounter = true;
        state.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode = true;
        state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode = true;
        return state;
    }

    protected static void SetIdentityPaletteMap(VideoState state) {
        for (int i = 0; i < 256; i++) {
            state.DacRegisters.PaletteMap[i] = 0xFF000000u | ((uint)i << 16) | ((uint)i << 8) | (uint)i;
        }
    }
}
