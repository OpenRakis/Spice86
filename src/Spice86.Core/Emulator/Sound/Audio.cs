namespace Spice86.Core.Emulator.Sound;

using Spice86.Core.Backend.Audio.PortAudio;
using Spice86.Core.Backend.Audio;

/// <summary>
/// Provides static methods to create an audio player and write full buffers of audio data to it.
/// </summary>
internal static class Audio {
    /// <summary>
    /// Creates an instance of an <see cref="AudioPlayer"/> with the specified sample rate, frames per buffer, and suggested latency.
    /// </summary>
    /// <param name="sampleRate">The sample rate of the audio player, in Hz.</param>
    /// <param name="framesPerBuffer">The number of frames per buffer, or 0 for the default value.</param>
    /// <param name="suggestedLatency">The suggested latency of the audio player, or null for the default value.</param>
    /// <returns>An instance of an <see cref="AudioPlayer"/> or null if one could not be created.</returns>
    public static AudioPlayer? CreatePlayer(int sampleRate = 48000, int framesPerBuffer = 0, double? suggestedLatency = null) {
        return PortAudioPlayer.Create(sampleRate, framesPerBuffer, suggestedLatency);
    }

    /// <summary>
    /// Writes the full buffer of audio data to the specified <paramref name="player"/>.
    /// </summary>
    /// <param name="player">The <see cref="AudioPlayer"/> to write the data to.</param>
    /// <param name="buffer">The buffer of audio data to write.</param>
    /// <remarks>
    /// The buffer must contain data in the range [-1.0, 1.0]. The method will block until the entire buffer has been written to the <paramref name="player"/>.
    /// </remarks>
    public static void WriteFullBuffer(AudioPlayer player, Span<float> buffer) {
        Span<float> writeBuffer = buffer;

        while (true) {
            int count = player.WriteData(writeBuffer);
            writeBuffer = writeBuffer[count..];
            if (writeBuffer.IsEmpty) {
                return;
            }
        }
    }
    
    /// <summary>
    /// Writes the full buffer of audio data to the specified <paramref name="player"/>.
    /// </summary>
    /// <param name="player">The <see cref="AudioPlayer"/> to write the data to.</param>
    /// <param name="buffer">The buffer of audio data to write.</param>
    /// <remarks>
    /// The buffer must contain 16-bit signed integer data. The method will block until the entire buffer has been written to the <paramref name="player"/>.
    /// </remarks>
    public static void WriteFullBuffer(AudioPlayer player, Span<short> buffer) {
        Span<short> writeBuffer = buffer;

        while (true) {
            int count = player.WriteData(writeBuffer);
            writeBuffer = writeBuffer[count..];
            if (writeBuffer.IsEmpty) {
                return;
            }
        }
    }
    
    
    /// <summary>
    /// Writes the full buffer of audio data to the specified <paramref name="player"/>.
    /// </summary>
    /// <param name="player">The <see cref="AudioPlayer"/> to write the data to.</param>
    /// <param name="buffer">The buffer of audio data to write.</param>
    /// <remarks>
    /// The buffer must contain 8-bit unsigned integer data, which will be converted to floats in the range [-1.0, 1.0]. The method will block until the entire buffer has been written to the <paramref name="player"/>.
    /// </remarks>
    public static void WriteFullBuffer(AudioPlayer player, Span<byte> buffer) {
        Span<byte> writeBuffer = buffer;

        float[] floatArray = new float[writeBuffer.Length];

        for (int i = 0; i < writeBuffer.Length; i++) {
            floatArray[i] = writeBuffer[i];
        }

        Span<float> span = new Span<float>(floatArray);

        while (true) {
            int count = player.WriteData(span);
            writeBuffer = writeBuffer[count..];
            if (writeBuffer.IsEmpty) {
                return;
            }
        }
    }
}
