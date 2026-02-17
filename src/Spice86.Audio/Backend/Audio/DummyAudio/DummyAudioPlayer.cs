namespace Spice86.Audio.Backend.Audio.DummyAudio;

using System.Threading;

/// <summary>
/// Dummy audio player with no backend.
/// Simulates real audio device backpressure by sleeping for the duration
/// the samples would have taken to play back, preventing the mixer thread
/// from spinning in a tight loop.
/// </summary>
sealed class DummyAudioPlayer : AudioPlayer {
    /// <summary>
    /// Initializes a new instance of the <see cref="DummyAudioPlayer"/> class.
    /// </summary>
    /// <param name="format">The audio playback format.</param>
    public DummyAudioPlayer(AudioFormat format) : base(format) {
    }

    /// <summary>
    /// Fakes writing data to the audio device.
    /// Sleeps for the expected playback duration to simulate real audio
    /// device backpressure (a real backend blocks in BulkEnqueue until
    /// the callback drains the queue).
    /// </summary>
    /// <param name="data">The input audio data</param>
    /// <returns>The data parameter length</returns>
    public override int WriteData(Span<float> data) {
        int sampleRate = Format.SampleRate;
        int channels = Format.Channels;
        if (sampleRate > 0 && channels > 0) {
            int frames = data.Length / channels;
            int sleepMs = (int)((long)frames * 1000 / sampleRate);
            if (sleepMs > 0) {
                Thread.Sleep(sleepMs);
            }
        }
        return data.Length;
    }

    /// <inheritdoc/>
    public override void Start() {
        // No-op for dummy player
    }

    /// <inheritdoc/>
    public override void ClearQueuedData() {
        // No-op for dummy player
    }

    /// <inheritdoc/>
    public override void MuteOutput() {
        // No-op for dummy player
    }

    /// <inheritdoc/>
    public override void UnmuteOutput() {
        // No-op for dummy player
    }
}