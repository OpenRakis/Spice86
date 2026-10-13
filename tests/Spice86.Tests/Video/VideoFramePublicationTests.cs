namespace Spice86.Tests.Video;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.DeviceScheduler;
using Spice86.Shared.Emulator.Video;
using Spice86.Shared.Interfaces;
using Spice86.Tests.AsmFixtures;

using Xunit;

public sealed class VideoFramePublicationTests {
    [Fact]
    public void LatePumpsShareFrameEpochAndPreserveTheFinalRenderedRow() {
        // Arrange
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.FlashbackVideo(true), JitMode.InterpretedOnly);
        uint[] expected = run.Result.CaptureVideoFrame();
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        clock.ElapsedTimeMs.Returns(100.0);
        DeviceScheduler scheduler = new(clock, Substitute.For<ILogger>(), "Shared video beam test");
        _ = new VgaTimingEngine(run.Result.VideoState, scheduler, clock, run.Result.VideoRenderer, new VgaBlinkState());
        run.Result.VideoState.IsRenderingDirty = true;
        VgaTimingParameters parameters = VgaTimingParameters.Calculate(run.Result.VideoState);
        scheduler.ProcessEvents();

        // Act
        for (int millisecond = 101; millisecond < 100 + parameters.DisplayDurationMs; millisecond++) {
            clock.ElapsedTimeMs.Returns((double)millisecond);
            scheduler.ProcessEvents();
        }
        clock.ElapsedTimeMs.Returns(100 + parameters.DisplayDurationMs);
        scheduler.ProcessEvents();
        uint[] pixels = new uint[expected.Length];
        run.Result.VideoRenderer.CopyLastFrame(pixels);
        clock.ElapsedTimeMs.Returns(100 + parameters.VerticalRetraceStartMs + parameters.LineDurationMs / 2);
        scheduler.ProcessEvents();

        // Assert
        pixels.Should().Equal(expected);
        (run.Result.VideoState.BeamTiming.ReadStatus(clock.ElapsedTimeMs) & 8).Should().Be(8);
    }

    [Fact]
    public void MidFrameTimingChangeCancelsTheOldFrameChain() {
        // Arrange
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.FlashbackVideo(true), JitMode.InterpretedOnly);
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        DeviceScheduler scheduler = new(clock, Substitute.For<ILogger>(), "Video reconfiguration test");
        VgaTimingEngine engine = new(run.Result.VideoState, scheduler, clock, run.Result.VideoRenderer, new VgaBlinkState());
        scheduler.ProcessEvents();
        VgaTimingParameters before = VgaTimingParameters.Calculate(run.Result.VideoState);
        run.Result.VideoState.SequencerRegisters.ClockingModeRegister.HalfDotClock = true;
        clock.ElapsedTimeMs.Returns(1.0);

        // Act
        scheduler.ProcessEvents();
        VgaTimingParameters after = VgaTimingParameters.Calculate(run.Result.VideoState);
        for (int millisecond = 2; millisecond <= Math.Ceiling(before.DisplayDurationMs) + 1; millisecond++) {
            clock.ElapsedTimeMs.Returns((double)millisecond);
            scheduler.ProcessEvents();
        }
        TimeSpan durationAtOldDisplayEnd = engine.LastFrameDuration;
        for (int millisecond = (int)Math.Ceiling(before.DisplayDurationMs) + 2; millisecond <= 40; millisecond++) {
            clock.ElapsedTimeMs.Returns((double)millisecond);
            scheduler.ProcessEvents();
        }

        // Assert
        after.FrameDurationMs.Should().BeApproximately(before.FrameDurationMs * 2, 1e-12);
        durationAtOldDisplayEnd.Should().Be(TimeSpan.Zero);
        engine.LastFrameDuration.TotalMilliseconds.Should().BeApproximately(after.DisplayDurationMs, 0.0001);
    }

    [Fact]
    public void TimingChangeDuringBlankingDoesNotReuseAnOverdueFrameEpoch() {
        // Arrange
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.FlashbackVideo(true), JitMode.InterpretedOnly);
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        DeviceScheduler scheduler = new(clock, Substitute.For<ILogger>(), "Blanking reconfiguration test");
        _ = new VgaTimingEngine(run.Result.VideoState, scheduler, clock, run.Result.VideoRenderer, new VgaBlinkState());
        scheduler.ProcessEvents();
        VgaTimingParameters before = VgaTimingParameters.Calculate(run.Result.VideoState);
        clock.ElapsedTimeMs.Returns(before.DisplayDurationMs);
        scheduler.ProcessEvents();
        run.Result.VideoState.SequencerRegisters.ClockingModeRegister.HalfDotClock = true;
        clock.ElapsedTimeMs.Returns(100.0);

        // Act
        scheduler.ProcessEvents();
        VgaTimingParameters after = VgaTimingParameters.Calculate(run.Result.VideoState);
        clock.ElapsedTimeMs.Returns(100 + after.VerticalRetraceStartMs + after.LineDurationMs / 2);
        scheduler.ProcessEvents();
        byte status = run.Result.VideoState.BeamTiming.ReadStatus(clock.ElapsedTimeMs);

        // Assert
        (status & 8).Should().Be(8);
    }

    [Fact]
    public void MillisecondSchedulerPumpsPreserveScanlineTiming() {
        // Arrange
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.FlashbackVideo(true), JitMode.InterpretedOnly);
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        DeviceScheduler scheduler = new(clock, Substitute.For<ILogger>(), "Late video timing test");
        VgaTimingEngine timing = new(run.Result.VideoState, scheduler, clock, run.Result.VideoRenderer, new VgaBlinkState());
        int visibleScanlines = run.Result.VideoState.CrtControllerRegisters.VerticalDisplayEndValue + 1;

        // Act
        for (int millisecond = 0; millisecond <= 100; millisecond++) {
            clock.ElapsedTimeMs.Returns((double)millisecond);
            scheduler.ProcessEvents();
        }

        // Assert
        timing.LastFrameDuration.TotalMilliseconds.Should().BeGreaterThan(0);
        timing.LastFrameDuration.TotalMilliseconds.Should().BeApproximately(
            visibleScanlines * VgaTimingParameters.Calculate(run.Result.VideoState).LineDurationMs, 0.0001);
    }

    [Fact]
    public void DisabledOutputUsesNewFrameWidthAfterResolutionReduction() {
        // Arrange
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.FlashbackVideo(false), JitMode.InterpretedOnly);
        run.Result.VideoRenderer.Width.Should().Be(320);
        run.Result.VideoState.CrtControllerRegisters.HorizontalDisplayEnd = 39;
        run.Result.VideoState.GeneralRegisters.CgaBitsPerPixel = 2;
        run.Result.VideoState.GeneralRegisters.CgaModeControl = 0;

        // Act
        uint[] pixels = run.Result.CaptureVideoFrame();

        // Assert
        run.Result.VideoRenderer.Width.Should().Be(160);
        pixels.Length.Should().Be(160 * 200);
        pixels.Should().OnlyContain(pixel => pixel == 0xFF000000);
    }

    [Theory]
    [InlineData(0, 1, false, false, 1, 2)]
    [InlineData(448, 1, false, false, 225, 226)]
    [InlineData(447, 1, false, false, 224, 225)]
    [InlineData(0, 0, true, false, 2, 3)]
    [InlineData(1, 0, true, false, 4, 5)]
    [InlineData(1, 1, true, false, 2, 3)]
    [InlineData(448, 0, false, true, 225, 226)]
    [InlineData(0, 0, false, true, 1, 2)]
    public void SchedulerPublishesFinalVisiblePhysicalScanline(
        int verticalDisplayEnd, byte maximumScanline, bool verticalTimingHalved, bool scanDouble,
        int expectedHeight, int lastPaletteIndex) {
        // Arrange
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.FlashbackVideo(true), JitMode.InterpretedOnly);
        IVideoState state = run.Result.VideoState;
        state.CrtControllerRegisters.VerticalDisplayEnd = (byte)verticalDisplayEnd;
        const byte verticalTotalBit9 = 0x20;
        state.CrtControllerRegisters.OverflowRegister.Value =
            (byte)(verticalTotalBit9 | ((verticalDisplayEnd >> 8) << 1));
        state.CrtControllerRegisters.MaximumScanlineRegister.MaximumScanline = maximumScanline;
        state.CrtControllerRegisters.MaximumScanlineRegister.CrtcScanDouble = scanDouble;
        state.CrtControllerRegisters.CrtModeControlRegister.VerticalTimingHalved = verticalTimingHalved;
        state.IsRenderingDirty = true;
        IEmulatedClock clock = Substitute.For<IEmulatedClock>();
        DeviceScheduler scheduler = new(clock, Substitute.For<ILogger>(), "Video timing test");
        VgaTimingEngine timing = new(state, scheduler, clock, run.Result.VideoRenderer, new VgaBlinkState());
        int visibleScanlines = verticalDisplayEnd + 1;
        if (verticalTimingHalved) {
            visibleScanlines *= 2;
        }

        // Act
        for (int eventIndex = 0; eventIndex < visibleScanlines * 2 + 8; eventIndex++) {
            double nextTime = scheduler.NextEventTime ?? throw new InvalidOperationException("Missing VGA timing event.");
            clock.ElapsedTimeMs.Returns(nextTime);
            scheduler.ProcessEvents();
            if (timing.LastFrameDuration > TimeSpan.Zero) {
                break;
            }
        }
        uint[] pixels = new uint[run.Result.VideoRenderer.BufferSize];
        run.Result.VideoRenderer.CopyLastFrame(pixels);

        // Assert
        timing.LastFrameDuration.Should().BeGreaterThan(TimeSpan.Zero);
        run.Result.VideoRenderer.Height.Should().Be(expectedHeight);
        pixels[(expectedHeight - 1) * 256].Should().Be(state.DacRegisters.PaletteMap[lastPaletteIndex]);
        VgaTimingParameters parameters = VgaTimingParameters.Calculate(state);
        timing.LastFrameDuration.TotalMilliseconds.Should().BeApproximately(visibleScanlines * parameters.LineDurationMs, 0.0001);
        int totalScanlines = state.CrtControllerRegisters.VerticalTotalValue + 2;
        if (verticalTimingHalved) {
            totalScanlines *= 2;
        }
        scheduler.NextEventTime.Should().NotBeNull();
        parameters.FrameDurationMs.Should().BeApproximately(totalScanlines * parameters.LineDurationMs, 0.0001);
    }

    [Fact]
    public void PitchedDestinationPreservesPaddingAndCopiesEveryRow() {
        // Arrange
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.FlashbackVideo(true), JitMode.InterpretedOnly);
        run.DependencyInjection.HeadlessGui?.Dispose();
        Renderer renderer = run.Result.VideoRenderer;
        uint[] expected = run.Result.CaptureVideoFrame();
        VideoFrameInfo info = renderer.FrameInfo;
        int stride = info.Width + 3;
        const uint padding = 0xDEADBEEF;
        uint[] destination = Enumerable.Repeat(padding, stride * info.Height).ToArray();

        // Act
        renderer.Render(destination, info.Width, info.Height, stride);

        // Assert
        for (int row = 0; row < info.Height; row++) {
            destination.AsSpan(row * stride, info.Width).ToArray()
                .Should().Equal(expected.AsSpan(row * info.Width, info.Width).ToArray());
            destination.AsSpan(row * stride + info.Width, 3).ToArray().Should().OnlyContain(pixel => pixel == padding);
        }
    }

    [Fact]
    public void GeometryChangeDoesNotRelabelPublishedPixelsOrConsumeFrameOnResizeMismatch() {
        // Arrange
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.FlashbackVideo(true), JitMode.InterpretedOnly);
        run.DependencyInjection.HeadlessGui?.Dispose();
        Renderer renderer = run.Result.VideoRenderer;
        VideoFrameInfo oldInfo = renderer.FrameInfo;
        run.Result.VideoState.CrtControllerRegisters.HorizontalDisplayEnd = 31;
        run.Result.VideoState.IsRenderingDirty = true;

        // Act
        renderer.BeginFrame();
        renderer.FrameInfo.Should().Be(oldInfo);
        for (int row = 0; row < 448; row++) {
            renderer.RenderScanline();
        }
        renderer.CompleteFrame();
        uint[] oldDestination = new uint[oldInfo.Width * oldInfo.Height];
        renderer.Render(oldDestination, oldInfo.Width, oldInfo.Height, oldInfo.Width);
        uint[] newDestination = new uint[renderer.BufferSize];
        renderer.Render(newDestination);

        // Assert
        renderer.Width.Should().Be(128);
        renderer.Height.Should().Be(224);
        oldDestination.Should().OnlyContain(pixel => pixel == 0);
        newDestination[0].Should().Be(run.Result.VideoState.DacRegisters.PaletteMap[2]);
        renderer.FrameInfo.AspectRatioCorrectionFactor.Should().Be(3.0 * 128 / (4.0 * 224));
    }

    [Fact]
    public void GuiReceivesHardwareGeometryAndAspectWithoutBiosModeChange() {
        // Arrange
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.FlashbackVideo(true), JitMode.InterpretedOnly);
        IGuiVideoPresentation gui = Substitute.For<IGuiVideoPresentation>();
        VgaCard card = new(gui, run.Result.VideoRenderer, Substitute.For<ILogger>());

        // Act
        card.SubscribeToEvents();
        run.Result.VideoState.CrtControllerRegisters.HorizontalDisplayEnd = 31;
        run.Result.CaptureVideoFrame();
        gui.RenderScreen += Raise.Event<EventHandler<UIRenderEventArgs>>(gui,
            new UIRenderEventArgs(IntPtr.Zero, 0, 256, 224, 256));

        // Assert
        gui.Received().UpdateResolution(256, 224, 3.0 * 256 / (4.0 * 224));
        gui.Received().UpdateResolution(128, 224, 3.0 * 128 / (4.0 * 224));
    }

    [Fact]
    public void PartialLastScanlinePairIsNotLost() {
        // Arrange
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.FlashbackVideo(true), JitMode.InterpretedOnly);
        run.Result.VideoState.CrtControllerRegisters.VerticalDisplayEnd = 0xC0;

        // Act
        uint[] pixels = run.Result.CaptureVideoFrame();

        // Assert
        run.Result.VideoRenderer.Height.Should().Be(225);
        pixels[224 * 256].Should().Be(run.Result.VideoState.DacRegisters.PaletteMap[226]);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 2)]
    public void HalvedVerticalTimingRendersBothPhysicalLinesOfLastVisibleCounter(byte maximumScanline, int expectedHeight) {
        // Arrange
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.FlashbackVideo(true), JitMode.InterpretedOnly);
        run.Result.VideoState.CrtControllerRegisters.MaximumScanlineRegister.MaximumScanline = maximumScanline;
        run.Result.VideoState.CrtControllerRegisters.CrtModeControlRegister.VerticalTimingHalved = true;
        run.Result.VideoState.CrtControllerRegisters.VerticalDisplayEnd = 1;
        run.Result.VideoState.CrtControllerRegisters.OverflowRegister.Value = 0;

        // Act
        uint[] pixels = run.Result.CaptureVideoFrame();

        // Assert
        run.Result.VideoRenderer.Height.Should().Be(expectedHeight);
        for (int row = 0; row < expectedHeight; row++) {
            pixels[row * 256].Should().Be(run.Result.VideoState.DacRegisters.PaletteMap[row + 2]);
        }
    }

    [Fact]
    public void MidFrameWidthReductionDoesNotRetainPixelsFromReusedBackBuffer() {
        // Arrange
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(AsmFixtureCatalog.FlashbackVideo(true), JitMode.InterpretedOnly);
        run.Result.CaptureVideoFrame();
        run.Result.CaptureVideoFrame();
        Renderer renderer = run.Result.VideoRenderer;
        run.Result.VideoState.IsRenderingDirty = true;
        renderer.BeginFrame();
        run.Result.VideoState.CrtControllerRegisters.HorizontalDisplayEnd = 0;

        // Act
        for (int scanline = 0; scanline < 448; scanline++) {
            renderer.RenderScanline();
        }
        renderer.CompleteFrame();
        uint[] pixels = new uint[renderer.BufferSize];
        renderer.CopyLastFrame(pixels);

        // Assert
        renderer.Width.Should().Be(256);
        pixels[256].Should().Be(run.Result.VideoState.DacRegisters.PaletteMap[3]);
        pixels.AsSpan(260, 252).ToArray().Should().OnlyContain(pixel => pixel == 0);
    }
}
