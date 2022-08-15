using Bufdio;
using Bufdio.Engines;

namespace Spice86.Core.Backend.Audio.PortAudio; 

public class PortAudioPlayer : AudioPlayer {
    private readonly IAudioEngine _engine;
    private bool _disposed;
    private PortAudioPlayer(AudioFormat format) : base(format) {
        BufdioLib.InitializePortAudio();
        AudioEngineOptions options = new AudioEngineOptions(2, format.SampleRate);
        _engine = new PortAudioEngine(options);
    }

    public static PortAudioPlayer Create() {
        return new PortAudioPlayer(new AudioFormat(SampleRate: 48000, Channels: 2,
            SampleFormat: SampleFormat.IeeeFloat32));
    }

    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _engine.Dispose();
                _disposed = true;
            }
        }
    }

    protected override void Start(bool useCallback) {
        //NOP
    }

    protected override void Stop() {
        //NOP
    }

    protected override int WriteDataInternal(Span<byte> data) {
        Span<float> samples = data.Cast<byte, float>();
        _engine.Send(samples);
        return data.Length;
    }
}