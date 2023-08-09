namespace Spice86.Core.Backend.Audio.DummyAudio; 

/// <summary>
/// Dummy audio player with no backend
/// </summary>
sealed class DummyAudioPlayer : AudioPlayer {
    public DummyAudioPlayer(AudioFormat format) : base(format) {
    }

    protected override int WriteDataInternal(Span<byte> data) {
        // Tell we wrote it all, it's all fake anyway
        return data.Length;
    }
}