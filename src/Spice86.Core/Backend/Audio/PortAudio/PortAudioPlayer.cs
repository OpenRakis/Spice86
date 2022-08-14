using Bufdio;
using Bufdio.Engines;

namespace Spice86.Core.Backend.Audio.PortAudio; 

public class PortAudioPlayer : AudioPlayer {
    private static IAudioEngine? _engine;
    private bool _disposed;
    private static int _numberOfPortAudioPlayerInstances = 0;
    private PortAudioPlayer(AudioFormat format) : base(format) {
        BufdioLib.InitializePortAudio();
        AudioEngineOptions options = new AudioEngineOptions(2, format.SampleRate);
        _engine ??= new PortAudioEngine(options);
        _numberOfPortAudioPlayerInstances++;
    }

    public static PortAudioPlayer Create() {
        return new PortAudioPlayer(new AudioFormat(SampleRate: 48000, Channels: 2,
            SampleFormat: SampleFormat.SignedPcm16));
    }

    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                if (_numberOfPortAudioPlayerInstances == 1) {
                    _engine?.Dispose();
                }
                _numberOfPortAudioPlayerInstances--;
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

    protected override int WriteDataInternal(ReadOnlySpan<byte> data) {
        Span<byte> destination = new Span<byte>(data.ToArray());
        Span<float> samples = SpanExtensions.Cast<byte, float>(destination);
        _engine?.Send(samples);
        return data.Length;
    }
}