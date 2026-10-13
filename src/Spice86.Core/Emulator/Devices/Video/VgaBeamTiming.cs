namespace Spice86.Core.Emulator.Devices.Video;

using System.Threading;

/// <summary>
/// Shared programmed beam timing and frame epoch for rendering and status reads.
/// </summary>
public sealed class VgaBeamTiming {
    private readonly IVideoState _state;
    private readonly object _lock = new();
    private Snapshot _snapshot;

    /// <summary>Creates a beam model from the initial register state.</summary>
    public VgaBeamTiming(IVideoState state) {
        _state = state;
        VgaTimingRegisters registers = VgaTimingRegisters.Read(state);
        _snapshot = new(registers, VgaTimingParameters.Calculate(registers), 0);
    }

    /// <summary>Refreshes changed timing registers and starts their new frame epoch.</summary>
    public VgaTimingParameters Refresh(double elapsedTimeMs) {
        VgaTimingRegisters registers = VgaTimingRegisters.Read(_state);
        Snapshot snapshot = Volatile.Read(ref _snapshot);
        if (registers == snapshot.Registers) {
            return snapshot.Parameters;
        }
        lock (_lock) {
            snapshot = _snapshot;
            registers = VgaTimingRegisters.Read(_state);
            if (registers != snapshot.Registers) {
                VgaTimingParameters parameters = VgaTimingParameters.Calculate(registers);
                double frameStartMs = elapsedTimeMs;
                if (parameters == snapshot.Parameters) {
                    parameters = snapshot.Parameters;
                    frameStartMs = snapshot.FrameStartMs;
                }
                snapshot = new(registers, parameters, frameStartMs);
                Volatile.Write(ref _snapshot, snapshot);
            }
            return snapshot.Parameters;
        }
    }

    /// <summary>Sets the frame epoch at a scheduled frame boundary.</summary>
    public VgaTimingParameters StartFrame(double frameStartMs) {
        Refresh(frameStartMs);
        lock (_lock) {
            Snapshot snapshot = _snapshot;
            Volatile.Write(ref _snapshot, snapshot with { FrameStartMs = frameStartMs });
            return snapshot.Parameters;
        }
    }

    /// <summary>Reads input status without depending on renderer progress or modifying registers.</summary>
    public byte ReadStatus(double elapsedTimeMs) {
        Snapshot snapshot = Volatile.Read(ref _snapshot);
        VgaTimingParameters timing = snapshot.Parameters;
        double framePosition = PositiveRemainder(elapsedTimeMs - snapshot.FrameStartMs, timing.FrameDurationMs);
        double linePosition = framePosition % timing.LineDurationMs;
        const byte DisplayDisabledBit = 1;
        const byte VerticalRetraceBit = 8;
        byte status = (byte)(_state.GeneralRegisters.InputStatusRegister1.Value & ~(DisplayDisabledBit | VerticalRetraceBit));
        if (IsInInterval(framePosition, timing.VerticalRetraceStartMs, timing.VerticalRetraceEndMs, timing.FrameDurationMs)) {
            status |= VerticalRetraceBit;
        }
        if (framePosition >= timing.DisplayDurationMs
            || IsInInterval(framePosition, timing.VerticalBlankingStartMs, timing.VerticalBlankingEndMs, timing.FrameDurationMs)
            || IsInInterval(linePosition, timing.HorizontalBlankingStartMs, timing.HorizontalBlankingEndMs, timing.LineDurationMs)) {
            status |= DisplayDisabledBit;
        }
        return status;
    }

    private static double PositiveRemainder(double value, double period) {
        double remainder = value % period;
        if (remainder < 0) {
            remainder += period;
        }
        return remainder;
    }

    private static bool IsInInterval(double position, double start, double end, double period) {
        if (start >= period || end <= start) {
            return false;
        }
        return (position >= start && position < end) || (end > period && position < end - period);
    }

    private sealed record Snapshot(VgaTimingRegisters Registers, VgaTimingParameters Parameters, double FrameStartMs);
}
