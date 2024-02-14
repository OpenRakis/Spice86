namespace Spice86.Core.Emulator.Devices.Sound;

using System.Numerics;

public interface IAudioFrame {
    public Span<float> Frame { get; }
}