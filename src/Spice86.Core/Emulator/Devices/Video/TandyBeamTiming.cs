namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>Reads beam periods from the native Tandy drawing-setup latch.</summary>
public sealed class TandyBeamTiming : VgaBeamTiming {
    private readonly TandyVideoState _tandy;

    /// <summary>Creates a beam model from the current native scanout timing.</summary>
    public TandyBeamTiming(IVideoState state, TandyVideoState tandy) : base(state, tandy.CalculateTiming()) {
        _tandy = tandy;
    }

    /// <inheritdoc />
    public override VgaTimingParameters Refresh(double elapsedTimeMs) {
        return UpdateTiming(_tandy.CalculateTiming(), elapsedTimeMs);
    }

    /// <inheritdoc />
    public override VgaTimingParameters CalculateTiming() {
        return _tandy.CalculateTiming();
    }
}