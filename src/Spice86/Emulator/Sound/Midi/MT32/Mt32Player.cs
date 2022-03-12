namespace Spice86.Emulator.Sound.Midi.MT32;

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

using Mt32emu;

using TinyAudio;

internal sealed class Mt32Player : IDisposable {
    private readonly Mt32Context context = new Mt32Context();
    private readonly AudioPlayer? audioPlayer = Audio.CreatePlayer(true);
    private bool disposed;

    public Mt32Player(string romsPath) {
        if (string.IsNullOrWhiteSpace(romsPath))
            throw new ArgumentNullException(nameof(romsPath));
        if(audioPlayer is null) {
            return;
        }
        if(!OperatingSystem.IsWindows()) {
            return;
        }
        LoadRoms(romsPath);

        AnalogOutputMode analogMode = Mt32GlobalState.GetBestAnalogOutputMode(audioPlayer.Format.SampleRate);
        context.AnalogOutputMode = analogMode;
        context.SetSampleRate(audioPlayer.Format.SampleRate);

        context.OpenSynth();
        audioPlayer.BeginPlayback(this.FillBufferDelegate);
    }

    private void FillBufferDelegate(Span<short> buffer, out int samplesWritten) {
        this.FillBuffer(buffer.ToArray().Select(x => (float)x).ToArray().AsSpan(), out var samples);
        samplesWritten = (int)samples;
    }

    public void PlayShortMessage(uint message) => context.PlayMessage(message);
    public void PlaySysex(ReadOnlySpan<byte> data) => context.PlaySysex(data);
    public void Pause() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }
        audioPlayer?.StopPlayback();
    }

    public void Resume() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }
        audioPlayer?.BeginPlayback(this.FillBufferDelegate);
    }

    public void Dispose() {
        if (!disposed) {
            context.Dispose();
            if(OperatingSystem.IsWindows()) {
                audioPlayer?.Dispose();
            }
            disposed = true;
        }
    }

    private void FillBuffer(Span<float> buffer, out uint samplesWritten) {
        try {
            context.Render(buffer);
            samplesWritten = (uint)buffer.Length;
        } catch (ObjectDisposedException) {
            buffer.Clear();
            samplesWritten = (uint)buffer.Length;
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
            foreach (var fileName in Directory.EnumerateFiles(path, "*.ROM"))
                context.AddRom(fileName);
        }
    }
}
