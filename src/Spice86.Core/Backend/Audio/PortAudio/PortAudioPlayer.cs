namespace Spice86.Core.Backend.Audio.PortAudio;
using Bufdio.Spice86;
using Bufdio.Spice86.Engines;

public sealed class PortAudioPlayer : AudioPlayer {
    private readonly IAudioEngine _engine;
    private static bool _loadedNativeLib;

    private PortAudioPlayer(int framesPerBuffer, AudioFormat format, double? suggestedLatency = null) : base(format) {
        AudioEngineOptions options = new AudioEngineOptions(2, format.SampleRate);
        if (suggestedLatency is not null) {
            options = new AudioEngineOptions(2, format.SampleRate, suggestedLatency.Value);
        }
        _engine = new PortAudioEngine(framesPerBuffer, options);
    }

    private static readonly object _lock = new();

    private static void LoadNativeLib() {
        lock(_lock) {
            if(_loadedNativeLib) {
                return;
            }
            if (OperatingSystem.IsWindows()) {
                const string path = "libportaudio.dll";
                _loadedNativeLib = BufdioLib.InitializePortAudio(path);
            } else {
                //rely on system-provided libportaudio.
                _loadedNativeLib = BufdioLib.InitializePortAudio();
            }
        }
    }

    public static PortAudioPlayer? Create(int sampleRate, int framesPerBuffer, double? suggestedLatency = null) {
        if (!_loadedNativeLib && !BufdioLib.IsPortAudioInitialized) {
            LoadNativeLib();
        }
        if(!_loadedNativeLib) {
            return null;
        }
        return new PortAudioPlayer(framesPerBuffer, new AudioFormat(SampleRate: sampleRate, Channels: 2,
            SampleFormat: SampleFormat.IeeeFloat32), suggestedLatency);
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