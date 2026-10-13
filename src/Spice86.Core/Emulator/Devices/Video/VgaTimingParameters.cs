namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Devices.Video.Registers.General;

/// <summary>
/// Programmed VGA beam intervals, derived from the dot clock and CRTC counters.
/// </summary>
public sealed record VgaTimingParameters {
    /// <summary>Selected oscillator frequency in hertz.</summary>
    public required int PixelClockHz { get; init; }
    /// <summary>Character clocks per physical horizontal period.</summary>
    public required int HorizontalTotal { get; init; }
    /// <summary>Duration of one physical scanline in milliseconds.</summary>
    public required double LineDurationMs { get; init; }
    /// <summary>Horizontal display end in milliseconds from line start.</summary>
    public required double HorizontalDisplayEndMs { get; init; }
    /// <summary>Horizontal blanking start in milliseconds from line start.</summary>
    public required double HorizontalBlankingStartMs { get; init; }
    /// <summary>Horizontal blanking end, including counter wrap, in milliseconds.</summary>
    public required double HorizontalBlankingEndMs { get; init; }
    /// <summary>Horizontal retrace start in milliseconds from line start.</summary>
    public required double HorizontalRetraceStartMs { get; init; }
    /// <summary>Horizontal retrace end, including counter wrap, in milliseconds.</summary>
    public required double HorizontalRetraceEndMs { get; init; }
    /// <summary>Physical scanlines per frame.</summary>
    public required int TotalScanlines { get; init; }
    /// <summary>Physical scanlines in the display interval.</summary>
    public required int VisibleScanlines { get; init; }
    /// <summary>Vertical blanking start in milliseconds from frame start.</summary>
    public required double VerticalBlankingStartMs { get; init; }
    /// <summary>Vertical blanking end, including counter wrap, in milliseconds.</summary>
    public required double VerticalBlankingEndMs { get; init; }
    /// <summary>Vertical retrace start in milliseconds from frame start.</summary>
    public required double VerticalRetraceStartMs { get; init; }
    /// <summary>Vertical retrace end, including counter wrap, in milliseconds.</summary>
    public required double VerticalRetraceEndMs { get; init; }
    /// <summary>Programmed frame period in milliseconds.</summary>
    public double FrameDurationMs => TotalScanlines * LineDurationMs;
    /// <summary>End of the physical display interval in milliseconds.</summary>
    public double DisplayDurationMs => VisibleScanlines * LineDurationMs;
    /// <summary>Programmed refresh frequency in hertz.</summary>
    public double RefreshRateHz => 1000.0 / FrameDurationMs;

    /// <summary>Calculates the timing model without modifying registers.</summary>
    public static VgaTimingParameters Calculate(IVideoState state) {
        return Calculate(VgaTimingRegisters.Read(state));
    }

    internal static VgaTimingParameters Calculate(VgaTimingRegisters registers) {
        const int PixelClock640Hz = 25175000;
        const int PixelClock720Hz = 28322000;
        int pixelClock = PixelClock720Hz;
        if (registers.ClockSelect == MiscellaneousOutput.ClockSelectValue.Use25175Khz) {
            pixelClock = PixelClock640Hz;
        }
        int characterClock = pixelClock / registers.DotsPerClock;
        double characterDurationMs = 1000.0 / characterClock;
        int horizontalTotal = registers.HorizontalTotal + 5;
        if (registers.HalfDotClock) {
            horizontalTotal *= 2;
        }
        int verticalScale = registers.VerticalTimingHalved ? 2 : 1;
        double lineDurationMs = horizontalTotal * characterDurationMs;
        double verticalCounterDurationMs = verticalScale * lineDurationMs;
        int blankingStart = registers.VerticalBlankingStart;
        int blankingEnd = registers.VerticalBlankingEnd & 0x7F;
        if (blankingStart != 0) {
            blankingStart++;
            blankingEnd = WrappedEnd(blankingStart, blankingEnd, 0x7F);
        }
        blankingEnd++;

        return new VgaTimingParameters {
            PixelClockHz = pixelClock,
            HorizontalTotal = horizontalTotal,
            LineDurationMs = lineDurationMs,
            HorizontalDisplayEndMs = Math.Min(registers.HorizontalDisplayEnd + 1, horizontalTotal) * characterDurationMs,
            HorizontalBlankingStartMs = registers.HorizontalBlankingStart * characterDurationMs,
            HorizontalBlankingEndMs = (registers.HorizontalBlankingStart
                + ((registers.HorizontalBlankingEnd - registers.HorizontalBlankingStart) & 0x3F)) * characterDurationMs,
            HorizontalRetraceStartMs = registers.HorizontalSyncStart * characterDurationMs,
            HorizontalRetraceEndMs = WrappedEnd(registers.HorizontalSyncStart, registers.HorizontalSyncEnd, 0x1F) * characterDurationMs,
            TotalScanlines = (registers.VerticalTotal + 2) * verticalScale,
            VisibleScanlines = (registers.VerticalDisplayEnd + 1) * verticalScale,
            VerticalBlankingStartMs = blankingStart * verticalCounterDurationMs,
            VerticalBlankingEndMs = blankingEnd * verticalCounterDurationMs,
            VerticalRetraceStartMs = registers.VerticalSyncStart * verticalCounterDurationMs,
            VerticalRetraceEndMs = WrappedEnd(registers.VerticalSyncStart, registers.VerticalSyncEnd, 0x0F)
                * verticalCounterDurationMs
        };
    }

    private static int WrappedEnd(int start, int lowEnd, int mask) {
        int length = (lowEnd - start) & mask;
        if (length == 0) {
            length = mask + 1;
        }
        return start + length;
    }
}
