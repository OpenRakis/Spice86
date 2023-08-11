namespace Spice86.Core.Backend.Audio.PortAudio;
using Bufdio.Spice86.Engines;

public sealed class PortAudioPlayer : AudioPlayer {
    private readonly IAudioEngine _engine;

    public PortAudioPlayer(int framesPerBuffer, AudioFormat format, double? suggestedLatency = null) : base(format) {
        AudioEngineOptions options = new AudioEngineOptions(2, format.SampleRate);
        if (suggestedLatency is not null) {
            options = new AudioEngineOptions(2, format.SampleRate, suggestedLatency.Value);
        }
        _engine = new PortAudioEngine(framesPerBuffer, options);
    }

    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _engine.Dispose();
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