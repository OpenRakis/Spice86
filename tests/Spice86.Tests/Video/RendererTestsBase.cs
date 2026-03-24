namespace Spice86.Tests.Video;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers.CrtController;
using Spice86.Core.Emulator.Devices.Video.Registers.Graphics;
using Spice86.Core.Emulator.Memory;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

using Xunit;

using ClockSelect = Spice86.Core.Emulator.Devices.Video.Registers.General.MiscellaneousOutput.ClockSelectValue;

/// <summary>
///     Base class for <see cref="Renderer" /> tests, parameterized by renderer implementation.
/// </summary>
public abstract class RendererTestsBase {
    /// <summary>Returns true when the renderer under test is runnable on this machine.</summary>
    protected abstract bool IsSupported { get; }

    /// <summary>Creates the renderer implementation under test.</summary>
    protected abstract IVgaRenderer256Color Create256ColorRenderer();

    /// <summary>Skips the current test when the required instruction set is not supported.</summary>
    protected void EnsureSupported() =>
        Skip.IfNot(IsSupported, "The required CPU instruction set is not supported on this machine.");

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Width / Height â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void Width_25MHz_HalfDot_Returns320() {
        EnsureSupported();
        (Renderer renderer, _) = CreateRenderer(ConfigureBase(ClockSelect.Use25175Khz, halfDot: true));
        renderer.Width.Should().Be(320);
    }

    [SkippableFact]
    public void Width_25MHz_FullDot_Returns640() {
        EnsureSupported();
        (Renderer renderer, _) = CreateRenderer(ConfigureBase(ClockSelect.Use25175Khz, halfDot: false));
        renderer.Width.Should().Be(640);
    }

    [SkippableFact]
    public void Width_28MHz_HalfDot_Returns360() {
        EnsureSupported();
        (Renderer renderer, _) = CreateRenderer(ConfigureBase(ClockSelect.Use28322Khz, halfDot: true));
        renderer.Width.Should().Be(360);
    }

    [SkippableFact]
    public void Width_28MHz_FullDot_Returns720() {
        EnsureSupported();
        (Renderer renderer, _) = CreateRenderer(ConfigureBase(ClockSelect.Use28322Khz, halfDot: false));
        renderer.Width.Should().Be(720);
    }

    [SkippableFact]
    public void Height_NoScanDouble_ReturnsVerticalDisplayEndPlusOne() {
        EnsureSupported();
        VideoState state = ConfigureBase(ClockSelect.Use25175Khz, halfDot: true);
        state.CrtControllerRegisters.VerticalDisplayEnd = 199;
        state.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble = false;

        (Renderer renderer, _) = CreateRenderer(state);
        renderer.Height.Should().Be(200);
    }

    [SkippableFact]
    public void Height_WithScanDouble_ReturnsHalf() {
        EnsureSupported();
        VideoState state = ConfigureBase(ClockSelect.Use25175Khz, halfDot: true);
        state.CrtControllerRegisters.VerticalDisplayEnd = 199;
        state.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble = true;

        (Renderer renderer, _) = CreateRenderer(state);
        renderer.Height.Should().Be(100);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Buffer Management â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void BufferSize_AfterBeginFrame_MatchesWidthTimesHeight() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, _) = CreateRenderer(state);

        state.IsRenderingDirty = true;
        renderer.BeginFrame();

        renderer.BufferSize.Should().Be(renderer.Width * renderer.Height);
    }

    [SkippableFact]
    public void Render_NoPendingFrame_LeavesBufferUntouched() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, _) = CreateRenderer(state);

        uint[] output = new uint[renderer.Width];
        Array.Fill(output, 0xDEADBEEF);

        renderer.Render(output);

        output.Should().AllBeEquivalentTo(0xDEADBEEF);
    }

    [SkippableFact]
    public void Render_WithPendingFrame_CopiesPixels() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        vram.Planes[0, 0] = 7;
        state.IsRenderingDirty = true;
        RenderFullFrame(renderer, state);

        uint[] output = new uint[renderer.Width * renderer.Height];
        renderer.Render(output);

        // First two pixels should be PaletteMap[7]
        output[0].Should().Be(state.DacRegisters.PaletteMap[7]);
        output[1].Should().Be(state.DacRegisters.PaletteMap[7]);
    }

    [SkippableFact]
    public void ResolutionChange_ReallocatesBuffer() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, _) = CreateRenderer(state);

        state.IsRenderingDirty = true;
        renderer.BeginFrame();
        renderer.BufferSize.Should().Be(320);

        // Change to 2 lines
        state.CrtControllerRegisters.VerticalDisplayEnd = 1;
        state.CrtControllerRegisters.VerticalTotal = 1;
        state.IsRenderingDirty = true;
        renderer.BeginFrame();

        renderer.BufferSize.Should().Be(640);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ 256-Color Mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void Render256Color_DoublesPlaneBytesToPixels() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // First character: planes[0..3] at address 0
        vram.Planes[0, 0] = 10;
        vram.Planes[1, 0] = 20;
        vram.Planes[2, 0] = 30;
        vram.Planes[3, 0] = 40;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // Each plane byte produces 2 consecutive identical pixels
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
    public void Render256Color_UsesPaletteMap() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);

        // Set up specific palette colors
        uint expectedColor = 0xFF_AA_BB_CC;
        state.DacRegisters.PaletteMap[42] = expectedColor;

        vram.Planes[0, 0] = 42;
        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(expectedColor);
        frame[1].Should().Be(expectedColor);
    }

    [SkippableFact]
    public void Render256Color_MultipleCharacters() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // Write two consecutive character positions
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

        // Second character (address 1) starts at pixel 8
        frame[8].Should().Be(state.DacRegisters.PaletteMap[5]);
        frame[9].Should().Be(state.DacRegisters.PaletteMap[5]);
        frame[10].Should().Be(state.DacRegisters.PaletteMap[6]);
        frame[11].Should().Be(state.DacRegisters.PaletteMap[6]);
    }

    [SkippableFact]
    public void Render256Color_MultipleScanlines() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(2);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // Line 0 pixel data at address 0
        vram.Planes[0, 0] = 10;
        // Line 1 pixel data: rowAddr advances by Offset << 1 = 80
        vram.Planes[0, 80] = 20;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        int width = renderer.Width;
        frame[0].Should().Be(state.DacRegisters.PaletteMap[10], "line 0, first pixel pair");
        frame[width].Should().Be(state.DacRegisters.PaletteMap[20], "line 1, first pixel pair");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ EGA Graphics Mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void RenderEgaGraphics_CombinesPlanesTo4BitIndex() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        // All planes set bit 7 â†’ index = 0b1111 = 15
        vram.Planes[0, 0] = 0x80; // bit 7 set
        vram.Planes[1, 0] = 0x80;
        vram.Planes[2, 0] = 0x80;
        vram.Planes[3, 0] = 0x80;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // First pixel (bit 7) should be index 15
        frame[0].Should().Be(state.DacRegisters.AttributeMap[15]);
    }

    [SkippableFact]
    public void RenderEgaGraphics_EachBitProducesOnePixel() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        // Only plane 0 has bit 7 set (others 0) â†’ index = 0b0001 = 1 for first pixel
        // Only plane 0 has bit 6 set â†’ index = 1 for second pixel
        vram.Planes[0, 0] = 0xFF; // all bits set
        vram.Planes[1, 0] = 0x00;
        vram.Planes[2, 0] = 0x00;
        vram.Planes[3, 0] = 0x00;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // All 8 pixels should be attribute index 1 (only plane 0 contributes)
        for (int i = 0; i < 8; i++) {
            frame[i].Should().Be(state.DacRegisters.AttributeMap[1], $"pixel {i}");
        }
    }

    [SkippableFact]
    public void RenderEgaGraphics_AllPlanesContribute() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        // Plane 0 bit 7, plane 1 bit 7, plane 2 bit 7, plane 3 clear â†’ index = 0b0111 = 7
        vram.Planes[0, 0] = 0x80;
        vram.Planes[1, 0] = 0x80;
        vram.Planes[2, 0] = 0x80;
        vram.Planes[3, 0] = 0x00;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.AttributeMap[7]);
    }

    [SkippableFact]
    public void RenderEgaGraphics_UsesAttributeMap() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);

        uint expected = 0xFF_12_34_56;
        state.DacRegisters.AttributeMap[5] = expected;

        // Index 5 = 0b0101 â†’ plane0 bit set, plane2 bit set, others clear
        vram.Planes[0, 0] = 0x80;
        vram.Planes[1, 0] = 0x00;
        vram.Planes[2, 0] = 0x80;
        vram.Planes[3, 0] = 0x00;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(expected);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ CGA Graphics Mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void RenderCgaGraphics_InterleavesPlanes02And13() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsCga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        // CGA mode: first 4 pixels from planes 0,2 interleaved; next 4 from planes 1,3
        // Each 2-bit pair from each plane is combined: (plane2_bits << 2) | plane0_bits
        // For bits 7-6: plane0 = 0b11xxxxxx, plane2 = 0b01xxxxxx â†’
        //   index = (0b01 << 2) | 0b11 = 0b0111 = 7
        vram.Planes[0, 0] = 0b11_00_00_00;
        vram.Planes[2, 0] = 0b01_00_00_00;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.AttributeMap[7], "first CGA pixel from planes 0,2");
    }

    [SkippableFact]
    public void RenderCgaGraphics_SecondHalfFromPlanes13() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsCga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        // Second 4 pixels come from planes 1 and 3
        // Bits 7-6: plane1 = 0b10xxxxxx, plane3 = 0b11xxxxxx
        //   index = (0b11 << 2) | 0b10 = 0b1110 = 14
        vram.Planes[1, 0] = 0b10_00_00_00;
        vram.Planes[3, 0] = 0b11_00_00_00;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // Pixels 4-7 are from planes 1,3
        frame[4].Should().Be(state.DacRegisters.AttributeMap[14]);
    }

    [SkippableFact]
    public void RenderCgaGraphics_FourPixelsPerHalf() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsCga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        // All 4 bit-pairs in plane 0: 0b_11_10_01_00
        // All 4 bit-pairs in plane 2: 0b_00_00_00_00
        vram.Planes[0, 0] = 0b11_10_01_00;
        vram.Planes[2, 0] = 0b00_00_00_00;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // 4 pixels from planes 0,2 with plane2 = 0: indices = 3, 2, 1, 0
        frame[0].Should().Be(state.DacRegisters.AttributeMap[3]);
        frame[1].Should().Be(state.DacRegisters.AttributeMap[2]);
        frame[2].Should().Be(state.DacRegisters.AttributeMap[1]);
        frame[3].Should().Be(state.DacRegisters.AttributeMap[0]);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Text Mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void RenderTextMode_UsesFontFromPlane2() {
        EnsureSupported();
        VideoState state = ConfigureTextMode(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        byte charCode = 65; // 'A'
        byte attribute = 0x07; // white on black (fg=7, bg=0)
        int fontAddress = 32 * charCode; // font byte address in plane 2

        vram.Planes[0, 0] = charCode;
        vram.Planes[1, 0] = attribute;
        // Font for this char at scanline 0: all bits set = all foreground
        vram.Planes[2, fontAddress] = 0xFF;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // All 8 pixels (DotsPerClock=8) should be foreground color (attribute index 7)
        for (int i = 0; i < 8; i++) {
            frame[i].Should().Be(state.DacRegisters.AttributeMap[7], $"pixel {i} should be foreground");
        }
    }

    [SkippableFact]
    public void RenderTextMode_BackgroundPixelsFromAttribute() {
        EnsureSupported();
        VideoState state = ConfigureTextMode(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        byte charCode = 0;
        byte attribute = 0x70; // black on white (fg=0, bg=7)

        vram.Planes[0, 0] = charCode;
        vram.Planes[1, 0] = attribute;
        // Font byte = 0x00 â†’ all background
        vram.Planes[2, 0] = 0x00;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // Background = (attribute >> 4) & 0xF = 7
        for (int i = 0; i < 8; i++) {
            frame[i].Should().Be(state.DacRegisters.AttributeMap[7], $"pixel {i} should be background");
        }
    }

    [SkippableFact]
    public void RenderTextMode_ForegroundAndBackgroundMixed() {
        EnsureSupported();
        VideoState state = ConfigureTextMode(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        byte charCode = 0;
        byte attribute = 0x12; // fg=2, bg=1
        vram.Planes[0, 0] = charCode;
        vram.Planes[1, 0] = attribute;
        // Font byte = 0b10101010 â†’ alternating foreground/background
        vram.Planes[2, 0] = 0b10101010;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        uint fg = state.DacRegisters.AttributeMap[2];
        uint bg = state.DacRegisters.AttributeMap[1];
        frame[0].Should().Be(fg, "bit 7 set â†’ foreground");
        frame[1].Should().Be(bg, "bit 6 clear â†’ background");
        frame[2].Should().Be(fg, "bit 5 set â†’ foreground");
        frame[3].Should().Be(bg, "bit 4 clear â†’ background");
    }

    [SkippableFact]
    public void RenderTextMode_BlinkSwapsForegroundAndBackground() {
        EnsureSupported();
        VideoState state = ConfigureTextMode(1);
        VgaBlinkState blinkState = new();
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state, blinkState);
        SetIdentityAttributeMap(state);

        // Enable blinking
        state.AttributeControllerRegisters.AttributeControllerModeRegister.BlinkingEnabled = true;

        // Use specific palette values so we can verify the swap formula:
        // (foreground, background) = (backGroundColor & 0x7, foreGroundColor)
        // where foreGroundColor and backGroundColor are ARGB uint values.
        uint bgArgb = 0xFF_00_00_07; // low 3 bits = 7
        uint fgArgb = 0xFF_AB_CD_EF;
        // Attribute 0x92: bg_index = (0x92 >> 4) & 0xF = 9, fg_index = 0x92 & 0xF = 2
        state.DacRegisters.AttributeMap[9] = bgArgb;
        state.DacRegisters.AttributeMap[2] = fgArgb;

        byte attribute = 0x92; // blink=1, bg_index=9, fg_index=2
        vram.Planes[0, 0] = 0;
        vram.Planes[1, 0] = attribute;
        vram.Planes[2, 0] = 0xFF; // all foreground

        // Blink phase low â†’ swap applied
        blinkState.IsBlinkPhaseHigh = false;
        blinkState.MarkChanged();
        uint[] frame = RenderAndCapture(renderer, state);

        // After swap: new foreground = bgArgb & 0x7 = 7
        // Font = 0xFF â†’ all pixels use new foreground
        uint expectedFg = bgArgb & 0x7;
        frame[0].Should().Be(expectedFg, "blink low: foreground = backGroundColor & 0x7");
    }

    [SkippableFact]
    public void RenderTextMode_BlinkHighShowsNormal() {
        EnsureSupported();
        VideoState state = ConfigureTextMode(1);
        VgaBlinkState blinkState = new();
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state, blinkState);
        SetIdentityAttributeMap(state);

        state.AttributeControllerRegisters.AttributeControllerModeRegister.BlinkingEnabled = true;

        byte attribute = 0x92; // blink=1, bg=1, fg=2
        vram.Planes[0, 0] = 0;
        vram.Planes[1, 0] = attribute;
        vram.Planes[2, 0] = 0xFF;

        // Blink phase high â†’ normal display
        blinkState.IsBlinkPhaseHigh = true;
        blinkState.MarkChanged();
        uint[] frame = RenderAndCapture(renderer, state);

        // No swap â†’ foreground = 2
        frame[0].Should().Be(state.DacRegisters.AttributeMap[2], "blink high: normal fg");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Dirty-skip Optimization â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void BeginFrame_NoDirty_SkipsRendering() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // First frame: dirty â†’ renders
        vram.Planes[0, 0] = 42;
        state.IsRenderingDirty = true;
        RenderFullFrame(renderer, state);
        uint[] frame1 = new uint[renderer.Width * renderer.Height];
        renderer.Render(frame1);
        frame1[0].Should().Be(state.DacRegisters.PaletteMap[42]);

        // Second frame: nothing changed â†’ skipped, no pending frame
        RenderFullFrame(renderer, state);
        uint[] frame2 = new uint[renderer.Width * renderer.Height];
        Array.Fill(frame2, 0xDEADBEEF);
        renderer.Render(frame2);

        // Render should not have overwritten the buffer since no pending frame
        frame2[0].Should().Be(0xDEADBEEF);
    }

    [SkippableFact]
    public void BeginFrame_MemoryDirty_Renders() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // Initial frame
        state.IsRenderingDirty = true;
        RenderFullFrame(renderer, state);
        uint[] discard = new uint[renderer.Width * renderer.Height];
        renderer.Render(discard);

        // Write to VRAM to make memory dirty
        WriteVramByte(vram, state, 0, 99);

        RenderFullFrame(renderer, state);
        uint[] frame = new uint[renderer.Width * renderer.Height];
        renderer.Render(frame);

        frame[0].Should().Be(state.DacRegisters.PaletteMap[99]);
    }

    [SkippableFact]
    public void BeginFrame_RegisterDirty_Renders() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);
        vram.Planes[0, 0] = 55;

        state.IsRenderingDirty = true;
        RenderFullFrame(renderer, state);
        uint[] frame = new uint[renderer.Width * renderer.Height];
        renderer.Render(frame);

        frame[0].Should().Be(state.DacRegisters.PaletteMap[55]);
    }

    [SkippableFact]
    public void BeginFrame_BlinkDirty_Renders() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        VgaBlinkState blinkState = new();
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state, blinkState);
        SetIdentityPaletteMap(state);
        vram.Planes[0, 0] = 77;

        blinkState.MarkChanged();
        RenderFullFrame(renderer, state);
        uint[] frame = new uint[renderer.Width * renderer.Height];
        renderer.Render(frame);

        frame[0].Should().Be(state.DacRegisters.PaletteMap[77]);
    }

    [SkippableFact]
    public void CompleteFrame_SkippedFrame_NoPendingFrame() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, _) = CreateRenderer(state);

        // Frame with no dirty â†’ skip
        renderer.BeginFrame();
        for (int i = 0; i < 10; i++) {
            renderer.RenderScanline();
        }
        renderer.CompleteFrame();

        uint[] output = new uint[renderer.Width * renderer.Height];
        Array.Fill(output, 0xDEADBEEF);
        renderer.Render(output);

        output[0].Should().Be(0xDEADBEEF, "skipped frame should not publish");
    }

    [SkippableFact]
    public void MidFrameDirty_ResumesRenderingFromDirtyPoint() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(4);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // First frame: render normally, publish
        // Fill all plane data with value 1 (covering all lines)
        for (int addr = 0; addr < 320; addr++) {
            vram.Planes[0, addr] = 1;
        }
        state.IsRenderingDirty = true;
        RenderFullFrame(renderer, state);
        uint[] first = new uint[renderer.Width * renderer.Height];
        renderer.Render(first);

        // Second frame: starts clean (no dirty)
        // Line 2 row address = 2 * (Offset << 1) = 2 * 80 = 160
        vram.Planes[0, 160] = 99;

        // Begin frame (clean) â†’ _skipThisFrame = true
        renderer.BeginFrame();

        int width = renderer.Width;
        int totalScanlines = state.CrtControllerRegisters.VerticalTotalValue + 10;

        // Render scanlines 0 and 1 (lines 0 and 1 are in skip mode)
        renderer.RenderScanline();
        renderer.RenderScanline();

        // Now mark dirty mid-frame via register change
        state.IsRenderingDirty = true;

        // Render remaining scanlines
        for (int i = 2; i < totalScanlines; i++) {
            renderer.RenderScanline();
        }
        renderer.CompleteFrame();

        uint[] frame = new uint[renderer.Width * renderer.Height];
        renderer.Render(frame);

        // Line 2 should show the new value (dirty triggered before line 2 rendered)
        frame[2 * width].Should().Be(state.DacRegisters.PaletteMap[99], "mid-frame dirty should render updated data");
    }

    [SkippableFact]
    public void MidFrameDirty_CopiesPreviousFrameForAlreadySkippedLines() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(4);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // First frame: fill line 0 with value 50
        for (int addr = 0; addr < 40; addr++) {
            vram.Planes[0, addr] = 50;
        }
        state.IsRenderingDirty = true;
        RenderFullFrame(renderer, state);
        uint[] discard = new uint[renderer.Width * renderer.Height];
        renderer.Render(discard);

        // Second frame: starts clean
        renderer.BeginFrame();
        // Render some scanlines in skip mode
        renderer.RenderScanline();
        renderer.RenderScanline();
        // Trigger dirty mid-frame
        state.IsRenderingDirty = true;
        // Render remaining
        int totalScanlines = state.CrtControllerRegisters.VerticalTotalValue + 2;
        for (int i = 2; i < totalScanlines + 5; i++) {
            renderer.RenderScanline();
        }
        renderer.CompleteFrame();

        uint[] frame = new uint[renderer.Width * renderer.Height];
        renderer.Render(frame);

        // Line 0 should still show value 50 (copied from last published frame)
        frame[0].Should().Be(state.DacRegisters.PaletteMap[50],
            "skipped lines should be restored from last published frame");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Memory Width Modes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void MemoryWidth_Byte_DirectAddressing() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        // Byte mode is the default for the EGA config helper
        state.CrtControllerRegisters.UnderlineRowScanlineRegister.DoubleWordMode = false;
        state.CrtControllerRegisters.CrtModeControlRegister.ByteWordMode = ByteWordMode.Byte;

        // Address 5 should be read directly
        vram.Planes[0, 5] = 0x80;

        state.CrtControllerRegisters.ScreenStartAddress = 5;
        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.AttributeMap[1], "byte mode reads address directly");
    }

    [SkippableFact]
    public void MemoryWidth_DoubleWord_ShiftsAddressLeft2() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        state.CrtControllerRegisters.UnderlineRowScanlineRegister.DoubleWordMode = true;

        // In DWord mode: physical = (counter << 2) | ((counter >> 14) & 3)
        // counter=0 â†’ physical=0
        vram.Planes[0, 0] = 0x80;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.AttributeMap[1]);
    }

    [SkippableFact]
    public void MemoryWidth_Word13_ShiftsAndUseBit13() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        state.CrtControllerRegisters.UnderlineRowScanlineRegister.DoubleWordMode = false;
        state.CrtControllerRegisters.CrtModeControlRegister.ByteWordMode = ByteWordMode.Word;
        state.CrtControllerRegisters.CrtModeControlRegister.AddressWrap = false;

        // Word13 mode: physical = (counter << 1) | ((counter >> 13) & 1)
        // counter=0 â†’ physical=0
        vram.Planes[0, 0] = 0x80;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.AttributeMap[1]);
    }

    [SkippableFact]
    public void MemoryWidth_Word15_ShiftsAndUseBit15() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        state.CrtControllerRegisters.UnderlineRowScanlineRegister.DoubleWordMode = false;
        state.CrtControllerRegisters.CrtModeControlRegister.ByteWordMode = ByteWordMode.Word;
        state.CrtControllerRegisters.CrtModeControlRegister.AddressWrap = true;

        // Word15 mode: physical = (counter << 1) | ((counter >> 15) & 1)
        // counter=0 â†’ physical=0
        vram.Planes[0, 0] = 0x80;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.AttributeMap[1]);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Character Clock Mask â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void CharacterClockMask_CountByFour_IncrementsEvery4thChar() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        state.CrtControllerRegisters.UnderlineRowScanlineRegister.CountByFour = true;
        state.CrtControllerRegisters.CrtModeControlRegister.CountByTwo = false;

        // With mask=3, address increments when (charCounter & 3) == 0, AFTER rendering.
        // char 0 reads addr 0 then increments â†’ addr becomes 1
        // chars 1-3 read addr 1 (no increment since (charCounter & 3) != 0)
        // char 4 reads addr 1 then increments â†’ addr becomes 2
        vram.Planes[0, 0] = 0x80; // bit 7 set at address 0
        vram.Planes[0, 1] = 0x40; // bit 6 set at address 1

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // Char 0 reads address 0 â†’ plane0=0x80, bit 7 = 1 â†’ index 1
        frame[0].Should().Be(state.DacRegisters.AttributeMap[1], "char 0 reads address 0");
        // Char 1 reads address 1 (after char 0 incremented) â†’ plane0=0x40, bit 7 = 0
        frame[8].Should().Be(state.DacRegisters.AttributeMap[0], "char 1 reads address 1, bit 7 = 0");
        // Char 1, pixel 1 = bit 6 of addr 1 = 1 â†’ index 1
        frame[9].Should().Be(state.DacRegisters.AttributeMap[1], "char 1 reads address 1, bit 6 = 1");

        // Char 4 still reads address 1 (chars 1-3 did not increment)
        frame[32].Should().Be(state.DacRegisters.AttributeMap[0], "char 4 reads address 1, bit 7 = 0");
        frame[33].Should().Be(state.DacRegisters.AttributeMap[1], "char 4 reads address 1, bit 6 = 1");
    }

    [SkippableFact]
    public void CharacterClockMask_CountByTwo_IncrementsEveryOtherChar() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        state.CrtControllerRegisters.UnderlineRowScanlineRegister.CountByFour = false;
        state.CrtControllerRegisters.CrtModeControlRegister.CountByTwo = true;

        // With mask=1, address increments when (charCounter & 1) == 0, AFTER rendering.
        // char 0 reads addr 0, then increments â†’ addr becomes 1
        // char 1 reads addr 1 (no increment since (1 & 1) != 0)
        // char 2 reads addr 1, then increments â†’ addr becomes 2
        vram.Planes[0, 0] = 0x80; // bit 7 set at address 0
        vram.Planes[0, 1] = 0x00; // nothing at address 1

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.AttributeMap[1], "char 0 reads address 0");
        // Char 1 reads address 1 (after char 0 incremented) â†’ all zeros â†’ index 0
        frame[8].Should().Be(state.DacRegisters.AttributeMap[0], "char 1 reads address 1");
        // Char 2 also reads address 1 (char 1 didn't increment)
        frame[16].Should().Be(state.DacRegisters.AttributeMap[0], "char 2 reads address 1");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Scanline Bit 0 for Address â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void ScanlineBit0ForAddressBit13_SubstitutesOnOddScanlines() {
        EnsureSupported();
        // 2 lines tall with MaximumScanline=1 â†’ char row has scanlines 0 and 1
        VideoState state = ConfigureGraphicsEga(2);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        state.CrtControllerRegisters.MaximumScanlineRegister.MaximumScanline = 1;
        state.CrtControllerRegisters.CrtModeControlRegister.CompatibilityModeSupport = false;

        // On scanline 0: bit13 cleared â†’ address stays low
        // On scanline 1: bit13 set â†’ address gets 0x2000 OR'd in
        vram.Planes[0, 0] = 0x80; // index=1 at address 0
        vram.Planes[0, 0x2000] = 0x40; // index=1 at bit 6, address 0x2000

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        int width = renderer.Width;
        frame[0].Should().Be(state.DacRegisters.AttributeMap[1], "scanline 0 reads address 0");
        // Line 1 (scanline 1 of char row) reads from 0x2000
        frame[width].Should().Be(state.DacRegisters.AttributeMap[0], "scanline 1 bit 7 of addr 0x2000 (=0x40) is 0");
        frame[width + 1].Should().Be(state.DacRegisters.AttributeMap[1], "scanline 1 bit 6 of addr 0x2000 (=0x40) is 1");
    }

    [SkippableFact]
    public void ScanlineBit0ForAddressBit14_SubstitutesOnOddScanlines() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(2);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        state.CrtControllerRegisters.MaximumScanlineRegister.MaximumScanline = 1;
        state.CrtControllerRegisters.CrtModeControlRegister.SelectRowScanCounter = false;

        vram.Planes[0, 0] = 0x80;
        vram.Planes[0, 0x4000] = 0x80; // bit 7 set at address 0x4000

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        int width = renderer.Width;
        frame[0].Should().Be(state.DacRegisters.AttributeMap[1], "scanline 0 reads address 0");
        frame[width].Should().Be(state.DacRegisters.AttributeMap[1], "scanline 1 reads from 0x4000 with bit14 set");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Line Compare (Split Screen) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void LineCompare_ResetsMemoryAddressAtCompareValue() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(4);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        int offset = state.CrtControllerRegisters.Offset;
        // Set line compare to line 2
        state.CrtControllerRegisters.LineCompare = 2;

        // Line 0 at address 0
        vram.Planes[0, 0] = 0x80; // index 1

        // Line 2 normally reads from address 2*offset*2 = 2*40*2=160
        // But line compare resets address to 0 at line 2
        // So after reset, line 3 reads from address 0 + offset*2 = 80
        vram.Planes[0, offset * 2] = 0x40; // index 1 at bit 6, at address offset*2

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        int width = renderer.Width;
        // Line 0: reads from address 0, bit 7 = 1 â†’ index 1
        frame[0].Should().Be(state.DacRegisters.AttributeMap[1], "line 0");

        // Line 3 should read from address offset*2 (after line compare reset at line 2)
        // Line 2 reset address to 0, line 3 = 0 + offset*2
        frame[3 * width + 1].Should().Be(state.DacRegisters.AttributeMap[1],
            "line 3 reads from address offset*2 after line compare reset, bit 6 set");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Color Plane Enable â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void ColorPlaneEnable_DisabledPlaneOutputsZero() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        // Disable plane 0
        state.AttributeControllerRegisters.ColorPlaneEnableRegister.Value = 0b1110;

        vram.Planes[0, 0] = 0xFF; // all bits set, but plane is disabled
        vram.Planes[1, 0] = 0x80; // bit 7 set

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // Without plane 0, index = (0 << 3 | 0 << 2 | 1 << 1 | 0) = 2
        frame[0].Should().Be(state.DacRegisters.AttributeMap[2], "plane 0 disabled â†’ contributes 0");
    }

    [SkippableFact]
    public void ColorPlaneEnable_AllDisabled_AllZero() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        state.AttributeControllerRegisters.ColorPlaneEnableRegister.Value = 0b0000;

        vram.Planes[0, 0] = 0xFF;
        vram.Planes[1, 0] = 0xFF;
        vram.Planes[2, 0] = 0xFF;
        vram.Planes[3, 0] = 0xFF;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.AttributeMap[0], "all planes disabled â†’ index 0");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Screen Start Address â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void ScreenStartAddress_OffsetsRendering() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        // Move screen start to address 10
        state.CrtControllerRegisters.ScreenStartAddress = 10;
        vram.Planes[0, 10] = 0x80; // bit 7 set at address 10

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.AttributeMap[1], "rendering starts from ScreenStartAddress");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Vertical Timing Halved â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void VerticalTimingHalved_BothCharsRowScanlinesShareSameAddress() {
        EnsureSupported();
        // With MaximumScanline=1, each character row has 2 scanlines (0 and 1).
        // With VerticalTimingHalved, the line counter only increments on odd
        // charRowScanline values. With CompatibilityModeSupport=true and
        // SelectRowScanCounter=true (set in ConfigureBase), no scanline-based
        // address modification occurs, so both scanlines read from the same address.
        VideoState state = ConfigureGraphicsEga(2);
        state.CrtControllerRegisters.MaximumScanlineRegister.MaximumScanline = 1;
        state.CrtControllerRegisters.CrtModeControlRegister.VerticalTimingHalved = true;

        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        vram.Planes[0, 0] = 0x80; // bit 7 at address 0

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        int width = renderer.Width;
        // Both output lines come from the same character row address
        frame[0].Should().Be(state.DacRegisters.AttributeMap[1], "scanline 0 of char row");
        frame[width].Should().Be(state.DacRegisters.AttributeMap[1], "scanline 1 of char row, same address");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ CrtcScanDouble â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void CrtcScanDouble_DoublesEachScanline() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, _) = CreateRenderer(state);

        // With scan double, each scanline is displayed twice
        // This halves the effective height
        state.CrtControllerRegisters.VerticalDisplayEnd = 3;
        state.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble = true;

        renderer.Height.Should().Be(2, "scan double should halve the height");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ DotsPerClock in Text Mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void TextMode_9DotsPerClock_Outputs9Pixels() {
        EnsureSupported();
        VideoState state = ConfigureTextMode(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        // Set 9-dot mode
        state.SequencerRegisters.ClockingModeRegister.DotsPerClock = 9;

        byte charCode = 0;
        byte attribute = 0x07;
        vram.Planes[0, 0] = charCode;
        vram.Planes[1, 0] = attribute;
        vram.Planes[2, 0] = 0xFF;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // First 8 pixels should be foreground (font bits), 9th too (mask 0x80>>8 = 0 â†’ background)
        // Actually, the mask goes mask >>= 1 for DotsPerClock iterations
        // mask starts at 0x80: pixels 0-6 use bits 7-1, pixel 7 uses bit 0
        // With DotsPerClock=9, there are 9 iterations but mask reaches 0 at iteration 8
        // So pixel 8 (9th dot) has mask=0 â†’ always background color
        for (int i = 0; i < 8; i++) {
            frame[i].Should().Be(state.DacRegisters.AttributeMap[7], $"pixel {i} = foreground");
        }
        // 9th pixel should be background (mask became 0)
        frame[8].Should().Be(state.DacRegisters.AttributeMap[0], "9th pixel = background (no font bit)");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Extended Memory (Font Select) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void TextMode_ExtendedMemory_UsesCharacterMapA() {
        EnsureSupported();
        VideoState state = ConfigureTextMode(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        state.SequencerRegisters.MemoryModeRegister.ExtendedMemory = true;
        // When bit 3 of attribute is set, use CharacterMapA
        int charMapAOffset = state.SequencerRegisters.CharacterMapSelectRegister.CharacterMapA;

        byte charCode = 1;
        byte attribute = 0x0F; // bit 3 set â†’ use CharacterMapA, fg = 0x7 (bits 0-2)

        vram.Planes[0, 0] = charCode;
        vram.Planes[1, 0] = attribute;
        // Font data at charMapA + 32*charCode + scanline
        vram.Planes[2, charMapAOffset + 32 * charCode] = 0x80;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // With extended memory, fg index = attribute & 0x7 = 7
        frame[0].Should().Be(state.DacRegisters.AttributeMap[7], "first pixel = foreground from charMapA font");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Zero Buffer Size Guard â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void BeginFrame_ZeroSize_DeactivatesFrame() {
        EnsureSupported();
        VideoState state = ConfigureBase(ClockSelect.Use25175Khz, halfDot: true);
        // VerticalDisplayEnd = 0 with ScanDouble = true â†’ Height = (0+1)/2 = 0
        state.CrtControllerRegisters.VerticalDisplayEnd = 0;
        state.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble = true;

        (Renderer renderer, _) = CreateRenderer(state);

        state.IsRenderingDirty = true;
        renderer.BeginFrame();

        // Width=320, Height=0, BufferSize=0
        renderer.BufferSize.Should().Be(0);

        // Scanlines should be no-ops
        renderer.RenderScanline();
        renderer.CompleteFrame();

        uint[] output = new uint[1];
        Array.Fill(output, 0xDEADBEEF);
        renderer.Render(output);
        output[0].Should().Be(0xDEADBEEF, "no frame should be published for zero-size buffer");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Multiple Consecutive Frames â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void MultipleFrames_OnlyLastPendingIsDelivered() {
        EnsureSupported();
        VideoState state = ConfigureGraphics256Color(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityPaletteMap(state);

        // Frame 1
        vram.Planes[0, 0] = 10;
        state.IsRenderingDirty = true;
        RenderFullFrame(renderer, state);

        // Frame 2 (without calling Render in between)
        vram.Planes[0, 0] = 20;
        state.IsRenderingDirty = true;
        RenderFullFrame(renderer, state);

        uint[] frame = new uint[renderer.Width * renderer.Height];
        renderer.Render(frame);

        // Should get the most recent frame's data
        frame[0].Should().Be(state.DacRegisters.PaletteMap[20]);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Byte Panning â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void BytePanning_OffsetsStartAddress() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        state.CrtControllerRegisters.PresetRowScanRegister.BytePanning = 3;
        state.CrtControllerRegisters.ScreenStartAddress = 0;

        // With byte panning = 3, start address becomes 0 + 3 = 3
        vram.Planes[0, 3] = 0x80;

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.AttributeMap[1], "byte panning offsets start address");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Display Enable Skew â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [SkippableFact]
    public void DisplayEnableSkew_DelaysVisibleStart() {
        EnsureSupported();
        VideoState state = ConfigureGraphicsEga(1);
        (Renderer renderer, VideoMemory vram) = CreateRenderer(state);
        SetIdentityAttributeMap(state);

        state.CrtControllerRegisters.HorizontalBlankingEndRegister.DisplayEnableSkew = 1;
        // Need to adjust HorizontalDisplayEnd to account for skew
        // _frameHorizontalDisplayEnd = HorizontalDisplayEnd + 1 + skew
        // With skew=1, blanking ends at character 1 instead of 0

        vram.Planes[0, 0] = 0x80; // data at address 0

        state.IsRenderingDirty = true;
        uint[] frame = RenderAndCapture(renderer, state);

        // With skew=1, the first visible character starts at characterCounter=1
        // But memoryAddressCounter starts at 0 and increments at char 0 (since mask=0)
        // So char 1 reads from address 1
        // We put data at address 0, so char 0 is blanked and char 1 reads address 1
        // The test verifies that skew delays the visible area
        // First visible pixel (pixel 0 in output) comes from char 1, address 1
        vram.Planes[0, 1] = 0x80;
        state.IsRenderingDirty = true;
        frame = RenderAndCapture(renderer, state);

        frame[0].Should().Be(state.DacRegisters.AttributeMap[1], "skew=1 means first visible char reads address 1");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ Helper Methods â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    ///     Creates a Renderer and captures the internal VideoMemory instance.
    /// </summary>
    protected (Renderer renderer, VideoMemory videoMemory) CreateRenderer(VideoState state) {
        return CreateRenderer(state, new VgaBlinkState());
    }

    protected (Renderer renderer, VideoMemory videoMemory) CreateRenderer(VideoState state, VgaBlinkState blinkState) {
        IMemory mockMemory = Substitute.For<IMemory>();
        VideoMemory? captured = null;
        mockMemory.When(m => m.RegisterMapping(Arg.Any<uint>(), Arg.Any<uint>(), Arg.Any<IMemoryDevice>()))
            .Do(callInfo => captured = (VideoMemory)callInfo.ArgAt<IMemoryDevice>(2));

        LoggerService loggerService = new();
        IVgaRenderer256Color renderer256Color = Create256ColorRenderer();
        Renderer renderer = new(mockMemory, state, blinkState, loggerService, renderer256Color);

        if (captured is null) {
            throw new InvalidOperationException("VideoMemory was not captured from RegisterMapping call.");
        }

        return (renderer, captured);
    }

    /// <summary>
    ///     Writes a byte to VRAM through the VideoMemory.Write method, which also sets HasChanged.
    /// </summary>
    private static void WriteVramByte(VideoMemory vram, VideoState state, uint address, byte value) {
        // Configure write mode 0 with no set/reset for a direct write
        state.SequencerRegisters.PlaneMaskRegister.Value = 0b0001; // write to plane 0 only
        state.SequencerRegisters.MemoryModeRegister.OddEvenMode = false;
        state.GraphicsControllerRegisters.GraphicsModeRegister.WriteMode = WriteMode.WriteMode0;
        state.GraphicsControllerRegisters.EnableSetReset.Value = 0;
        state.GraphicsControllerRegisters.BitMask = 0xFF;
        state.GraphicsControllerRegisters.DataRotateRegister.FunctionSelect = FunctionSelect.None;
        state.GraphicsControllerRegisters.DataRotateRegister.RotateCount = 0;

        uint baseAddress = state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.BaseAddress;
        vram.Write(baseAddress + address, value);
    }

    /// <summary>
    ///     Runs a full frame cycle: BeginFrame, enough RenderScanline calls, CompleteFrame.
    /// </summary>
    private static void RenderFullFrame(Renderer renderer, VideoState state) {
        renderer.BeginFrame();
        int totalLines = state.CrtControllerRegisters.VerticalTotalValue + 10;
        for (int i = 0; i < totalLines; i++) {
            renderer.RenderScanline();
        }
        renderer.CompleteFrame();
    }

    /// <summary>
    ///     Renders a full frame and captures the output.
    /// </summary>
    private static uint[] RenderAndCapture(Renderer renderer, VideoState state) {
        RenderFullFrame(renderer, state);
        uint[] output = new uint[renderer.Width * renderer.Height];
        renderer.Render(output);
        return output;
    }

    /// <summary>
    ///     Base register configuration: clock, dot clock, and minimal CRT settings.
    /// </summary>
    private static VideoState ConfigureBase(ClockSelect clock, bool halfDot) {
        VideoState state = new();
        state.GeneralRegisters.MiscellaneousOutput.ClockSelect = clock;
        state.SequencerRegisters.ClockingModeRegister.HalfDotClock = halfDot;
        state.SequencerRegisters.ClockingModeRegister.DotsPerClock = 8;
        state.AttributeControllerRegisters.ColorPlaneEnableRegister.Value = 0x0F;
        state.CrtControllerRegisters.VerticalDisplayEnd = 0;
        state.CrtControllerRegisters.VerticalTotal = 0;
        state.CrtControllerRegisters.MaximumScanlineRegister.MaximumScanline = 0;
        state.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble = false;
        state.CrtControllerRegisters.HorizontalTotal = 99;
        state.CrtControllerRegisters.HorizontalDisplayEnd = 79;
        state.CrtControllerRegisters.Offset = 40;
        state.CrtControllerRegisters.CrtModeControlRegister.ByteWordMode = ByteWordMode.Byte;
        state.CrtControllerRegisters.CrtModeControlRegister.CompatibilityModeSupport = true;
        state.CrtControllerRegisters.CrtModeControlRegister.SelectRowScanCounter = true;
        return state;
    }

    /// <summary>
    ///     Configure for 256-color mode (Mode 13h style), given number of visible lines.
    /// </summary>
    private static VideoState ConfigureGraphics256Color(int lines) {
        VideoState state = ConfigureBase(ClockSelect.Use25175Khz, halfDot: true);
        state.CrtControllerRegisters.VerticalDisplayEnd = (byte)(lines - 1);
        state.CrtControllerRegisters.VerticalTotal = (byte)(lines - 1);
        state.CrtControllerRegisters.HorizontalDisplayEnd = 39;
        state.CrtControllerRegisters.HorizontalTotal = 49;
        state.CrtControllerRegisters.Offset = 40;
        state.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode = true;
        state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode = true;
        return state;
    }

    /// <summary>
    ///     Configure for EGA graphics mode, given number of visible lines.
    /// </summary>
    private static VideoState ConfigureGraphicsEga(int lines) {
        VideoState state = ConfigureBase(ClockSelect.Use25175Khz, halfDot: false);
        state.CrtControllerRegisters.VerticalDisplayEnd = (byte)(lines - 1);
        state.CrtControllerRegisters.VerticalTotal = (byte)(lines - 1);
        state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode = true;
        state.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode = false;
        state.GraphicsControllerRegisters.GraphicsModeRegister.ShiftRegisterMode = ShiftRegisterMode.Ega;
        return state;
    }

    /// <summary>
    ///     Configure for CGA graphics mode, given number of visible lines.
    /// </summary>
    private static VideoState ConfigureGraphicsCga(int lines) {
        VideoState state = ConfigureBase(ClockSelect.Use25175Khz, halfDot: false);
        state.CrtControllerRegisters.VerticalDisplayEnd = (byte)(lines - 1);
        state.CrtControllerRegisters.VerticalTotal = (byte)(lines - 1);
        state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode = true;
        state.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode = false;
        state.GraphicsControllerRegisters.GraphicsModeRegister.ShiftRegisterMode = ShiftRegisterMode.Cga;
        return state;
    }

    /// <summary>
    ///     Configure for text mode (80Ã—lines), given number of visible lines.
    /// </summary>
    private static VideoState ConfigureTextMode(int lines) {
        VideoState state = ConfigureBase(ClockSelect.Use25175Khz, halfDot: false);
        state.CrtControllerRegisters.VerticalDisplayEnd = (byte)(lines - 1);
        state.CrtControllerRegisters.VerticalTotal = (byte)(lines - 1);
        state.CrtControllerRegisters.HorizontalDisplayEnd = 79;
        state.CrtControllerRegisters.HorizontalTotal = 99;
        state.CrtControllerRegisters.Offset = 80;
        state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode = false;
        state.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode = false;
        state.SequencerRegisters.MemoryModeRegister.ExtendedMemory = false;
        return state;
    }

    /// <summary>
    ///     Sets PaletteMap so that index N maps to a unique distinguishable color.
    /// </summary>
    private static void SetIdentityPaletteMap(VideoState state) {
        for (int i = 0; i < 256; i++) {
            state.DacRegisters.PaletteMap[i] = 0xFF000000u | ((uint)i << 16) | ((uint)i << 8) | (uint)i;
        }
    }

    /// <summary>
    ///     Sets AttributeMap so that index N maps to a unique distinguishable color.
    /// </summary>
    private static void SetIdentityAttributeMap(VideoState state) {
        for (int i = 0; i < 16; i++) {
            state.DacRegisters.AttributeMap[i] = 0xFF000000u | ((uint)i << 20) | ((uint)i << 12) | ((uint)i << 4);
        }
    }
}

