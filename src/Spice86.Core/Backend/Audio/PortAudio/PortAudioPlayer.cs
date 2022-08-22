using Bufdio;
using Bufdio.Engines;

namespace Spice86.Core.Backend.Audio.PortAudio; 

public class PortAudioPlayer : AudioPlayer {
    private readonly IAudioEngine _engine;
    private bool _disposed;
    private PortAudioPlayer(int framesPerBuffer, AudioFormat format, double? suggestedLatency = null) : base(format) {
        if(OperatingSystem.IsWindows()) {
            string path = "libportaudio.dll";
            BufdioLib.InitializePortAudio(path);
        }
        else {
            //rely on system-provided libportaudio.
            BufdioLib.InitializePortAudio();
        }
        AudioEngineOptions options = new AudioEngineOptions(2, format.SampleRate);
        if(suggestedLatency is not null) {
            options = new AudioEngineOptions(2, format.SampleRate, suggestedLatency.Value);
        }
        _engine = new PortAudioEngine(framesPerBuffer, options);
    }

    public static PortAudioPlayer Create(int sampleRate, int framesPerBuffer, double? suggestedLatency = null) {
        return new PortAudioPlayer(framesPerBuffer, new AudioFormat(SampleRate: sampleRate, Channels: 2,
            SampleFormat: SampleFormat.IeeeFloat32), suggestedLatency);
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