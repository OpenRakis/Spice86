namespace Spice86.Emulator.Sound.Midi.MT32;

using Mt32emu;

using System;
using System.IO;
using System.IO.Compression;

using TinyAudio;

internal sealed class Mt32Player : IDisposable {
    private readonly Mt32Context context = new();
    private readonly AudioPlayer? audioPlayer;
    private bool disposed;

    public Mt32Player(string romsPath, Configuration configuration) {
        if (string.IsNullOrWhiteSpace(romsPath)) {
            throw new ArgumentNullException(nameof(romsPath));
        }

        if (configuration.CreateAudioBackend == false) {
            return;
        }
        audioPlayer = Audio.CreatePlayer(true);
        if (audioPlayer is null) {
            return;
        }
        if (!OperatingSystem.IsWindows()) {
            return;
        }
        LoadRoms(romsPath);

        AnalogOutputMode analogMode = Mt32GlobalState.GetBestAnalogOutputMode(audioPlayer.Format.SampleRate);
        context.AnalogOutputMode = analogMode;
        context.SetSampleRate(audioPlayer.Format.SampleRate);

        context.OpenSynth();
        audioPlayer.BeginPlayback(this.FillBuffer);
    }

    public void PlayShortMessage(uint message) => context.PlayMessage(message);
    public void PlaySysex(ReadOnlySpan<byte> data) => context.PlaySysex(data);
    public void Pause() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }
        //... Do not pause ...
        //audioPlayer?.StopPlayback();
    }

    public void Resume() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }
        // ... and restart, this produces an InvalidOperationException
        //audioPlayer?.BeginPlayback(this.FillBuffer);
    }

    public void Dispose() {
        if (!disposed) {
            context.Dispose();
            if (OperatingSystem.IsWindows()) {
                audioPlayer?.Dispose();
            }
            disposed = true;
        }
    }

    private void FillBuffer(Span<float> buffer, out int samplesWritten) {
        try {
            context.Render(buffer);
            samplesWritten = buffer.Length;
        } catch (ObjectDisposedException) {
            buffer.Clear();
            samplesWritten = buffer.Length;
        }
    }
    private void LoadRoms(string path) {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
            using var zip = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read);
            foreach (ZipArchiveEntry? entry in zip.Entries) {
                if (entry.FullName.EndsWith(".ROM", StringComparison.OrdinalIgnoreCase)) {
                    using Stream? stream = entry.Open();
                    context.AddRom(stream);
                }
            }
        } else if (Directory.Exists(path)) {
            foreach (string? fileName in Directory.EnumerateFiles(path, "*.ROM")) {
                context.AddRom(fileName);
            }
        }
    }
}
