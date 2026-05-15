namespace Spice86.Shared.Emulator.Storage.CdRom.Audio;

using System.Collections.Generic;
using System.IO;

/// <summary>
/// <see cref="IAudioCodec"/> that decodes RIFF/WAVE files holding CDDA-compliant PCM
/// (44.1 kHz, 16-bit, stereo). Returns a <see cref="WindowedDataSource"/> pointing at
/// the raw <c>data</c> chunk of the WAV file.
/// </summary>
public sealed class WavAudioCodec : IAudioCodec, IDisposable {
    private readonly List<IDisposable> _owned = new List<IDisposable>();

    /// <inheritdoc/>
    public IDataSource OpenAsCdda(string filePath) {
        WavAudioFile wav = new WavAudioFile(filePath);
        FileBackedDataSource backing = new FileBackedDataSource(filePath);
        _owned.Add(backing);
        return new WindowedDataSource(backing, wav.PcmDataOffset, wav.PcmDataLength);
    }

    /// <inheritdoc/>
    public void Dispose() {
        foreach (IDisposable d in _owned) {
            d.Dispose();
        }
        _owned.Clear();
    }
}
