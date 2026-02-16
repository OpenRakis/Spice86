namespace Spice86.Audio.Backend.Audio.DummyAudio;

/// <summary>
/// Dummy audio player with no backend
/// </summary>
sealed class DummyAudioPlayer : AudioPlayer {
    /// <summary>
    /// Initializes a new instance of the <see cref="DummyAudioPlayer"/> class.
    /// </summary>
    /// <param name="format">The audio playback format.</param>
    public DummyAudioPlayer(AudioFormat format) : base(format) {
    }

    /// <summary>
    /// Fakes writing data to the audio device
    /// </summary>
    /// <param name="data">The input audio data</param>
    /// <returns>The data parameter length</returns>
    public override int WriteData(Span<float> data) {
        // Tell we wrote it all, it's all fake anyway
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