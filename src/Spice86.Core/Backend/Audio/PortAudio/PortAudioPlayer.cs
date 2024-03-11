namespace Spice86.Core.Backend.Audio.PortAudio;

using Bufdio.Spice86;
using Bufdio.Spice86.Engines;

using Spice86.Shared.Emulator.Audio;

using System.Runtime.InteropServices;

/// <summary>
/// The audio rendering backend
/// </summary>
public sealed class PortAudioPlayer : AudioPlayer {
    private readonly PortAudioEngine _engine;
    private readonly PortAudioLib _portAudioLib;

    /// <summary>
    /// Initializes a new instance of <see cref="PortAudioPlayer"/> class.
    /// </summary>
    /// <param name="portAudioLib">The class that represents the native PortAudio library</param>
    /// <param name="framesPerBuffer">The number of frames in the audio buffer</param>
    /// <param name="format">The audio playback format</param>
    /// <param name="suggestedLatency">Desired output latency</param>
    public PortAudioPlayer(PortAudioLib portAudioLib, int framesPerBuffer, AudioFormat format, double? suggestedLatency = null) : base(format) {
        _portAudioLib = portAudioLib;
        AudioEngineOptions options = new AudioEngineOptions(_portAudioLib.DefaultOutputDevice, 2, format.SampleRate);
        if (suggestedLatency is not null) {
            options = new AudioEngineOptions(_portAudioLib.DefaultOutputDevice, 2, format.SampleRate, suggestedLatency.Value);
        }
        _engine = new PortAudioEngine(framesPerBuffer, options);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _engine.Dispose();
                _portAudioLib.Dispose();
            }
            _disposed = true;
        }
    }

    /// <inheritdoc/>
    protected override int WriteDataInternal(AudioFrame<float> frames)
    {
        _engine.Send(frames);
        return 2;
    }
}