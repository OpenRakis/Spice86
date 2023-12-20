namespace Spice86.Core.Backend.Audio.PortAudio;

using Bufdio.Spice86;
using Bufdio.Spice86.Engines;

/// <summary>
/// The audio rendering backend
/// </summary>
public sealed class PortAudioPlayer : AudioPlayer {
    private readonly PortAudioEngine _engine;
    private readonly PortAudioLib _portAudioLib;

    public PortAudioPlayer(PortAudioLib portAudioLib, int framesPerBuffer, AudioFormat format, double? suggestedLatency = null) : base(format) {
        _portAudioLib = portAudioLib;
        AudioEngineOptions options = new AudioEngineOptions(_portAudioLib.DefaultOutputDevice, 2, format.SampleRate);
        if (suggestedLatency is not null) {
            options = new AudioEngineOptions(_portAudioLib.DefaultOutputDevice, 2, format.SampleRate, suggestedLatency.Value);
        }
        _engine = new PortAudioEngine(framesPerBuffer, options);
    }

    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _engine.Dispose();
                _portAudioLib.Dispose();
            }
            _disposed = true;
        }
    }

    protected override int WriteDataInternal(Span<byte> data) {
        Span<float> samples = data.Cast<byte, float>();
        _engine.Send(samples);
        return data.Length;
    }
}