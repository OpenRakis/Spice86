namespace Spice86.Emulator.Sound.Midi.MT32;

using Backend.Audio.OpenAl;

using Mt32emu;

using System;
using System.IO;
using System.IO.Compression;

using Spice86.Backend.Audio.OpenAl;

internal sealed class Mt32Player : IDisposable {
    private readonly Mt32Context _context = new();
    private readonly AudioPlayer? _audioPlayer;
    private bool _disposed;

    public Mt32Player(string romsPath, Configuration configuration) {
        if (string.IsNullOrWhiteSpace(romsPath)) {
            throw new ArgumentNullException(nameof(romsPath));
        }

        if (!configuration.CreateAudioBackend) {
            return;
        }
        _audioPlayer = Audio.CreatePlayer();
        if (_audioPlayer is null) {
            return;
        }
        LoadRoms(romsPath);

        _context.AnalogOutputMode = Mt32GlobalState.GetBestAnalogOutputMode(_audioPlayer.Format.SampleRate);
        _context.SetSampleRate(_audioPlayer.Format.SampleRate);

        _context.OpenSynth();
        _audioPlayer.BeginPlayback(this.FillBuffer);
    }

    public void PlayShortMessage(uint message) => _context.PlayMessage(message);
    public void PlaySysex(ReadOnlySpan<byte> data) => _context.PlaySysex(data);
    public void Pause() {
        //... Do not pause ...
        //audioPlayer?.StopPlayback();
    }

    public void Resume() {
        // ... and restart, this produces an InvalidOperationException
        //audioPlayer?.BeginPlayback(this.FillBuffer);
    }

    public void Dispose() {
        if (!_disposed) {
            _context.Dispose();
            _audioPlayer?.Dispose();
            _disposed = true;
        }
    }

    private void FillBuffer(Span<float> buffer, out int samplesWritten) {
        try {
            _context.Render(buffer);
            samplesWritten = buffer.Length;
        } catch (ObjectDisposedException) {
            buffer.Clear();
            samplesWritten = buffer.Length;
        }
    }
    private void LoadRoms(string path) {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
            using var zip = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read);
            for (int i = 0; i < zip.Entries.Count; i++) {
                ZipArchiveEntry? entry = zip.Entries[i];
                if (entry.FullName.EndsWith(".ROM", StringComparison.OrdinalIgnoreCase)) {
                    using Stream? stream = entry.Open();
                    _context.AddRom(stream);
                }
            }
        } else if (Directory.Exists(path)) {
            foreach (string? fileName in Directory.EnumerateFiles(path, "*.ROM")) {
                _context.AddRom(fileName);
            }
        }
    }
}
