namespace Spice86.Shared.Emulator.Storage.CdRom.Audio;

using System.Collections.Generic;
using System.IO;
using System.Threading;

using LibVLCSharp.Shared;

/// <summary>
/// <see cref="IAudioCodec"/> that delegates decoding to LibVLC. Supports any audio
/// format LibVLC can demux (MP3, FLAC, OGG, OPUS, AIFF, ...). The decoded stream is
/// transcoded to raw CDDA-compatible PCM (44.1 kHz, 16-bit signed little-endian,
/// stereo, interleaved) and written to a temporary file, which is then exposed via
/// <see cref="FileBackedDataSource"/>.
/// </summary>
/// <remarks>
/// Requires the LibVLC native binaries to be loadable in the current process. On
/// Windows the <c>VideoLAN.LibVLC.Windows</c> package bundles them. On Linux/macOS
/// the system <c>libvlc</c> must be installed and discoverable.
/// </remarks>
public sealed class LibVlcAudioCodec : IAudioCodec, IDisposable {
    private static int s_coreInitialized;

    private readonly LibVLC _libVlc;
    private readonly List<string> _tempFiles = new List<string>();
    private readonly List<IDisposable> _owned = new List<IDisposable>();
    private bool _disposed;

    /// <summary>Creates the codec, initializing the LibVLC native runtime if needed.</summary>
    /// <exception cref="InvalidOperationException">LibVLC native binaries cannot be loaded.</exception>
    public LibVlcAudioCodec() {
        EnsureCoreInitialized();
        _libVlc = new LibVLC("--no-video", "--quiet", "--no-sout-display");
    }

    /// <inheritdoc/>
    public IDataSource OpenAsCdda(string filePath) {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(LibVlcAudioCodec));
        }
        if (!File.Exists(filePath)) {
            throw new FileNotFoundException("Audio file not found.", filePath);
        }
        string tempPath = Path.Combine(Path.GetTempPath(), $"spice86-cdda-{Guid.NewGuid():N}.pcm");
        TranscodeToPcm(filePath, tempPath);
        _tempFiles.Add(tempPath);
        FileBackedDataSource source = new FileBackedDataSource(tempPath);
        _owned.Add(source);
        return source;
    }

    private void TranscodeToPcm(string filePath, string tempPath) {
        string escapedDst = tempPath.Replace("\\", "/");
        string sout = $":sout=#transcode{{acodec=s16l,channels=2,samplerate=44100}}:standard{{access=file,mux=raw,dst='{escapedDst}'}}";

        using Media media = new Media(_libVlc, filePath, FromType.FromPath);
        media.AddOption(sout);
        media.AddOption(":sout-keep");
        media.AddOption(":no-sout-display");

        using MediaPlayer player = new MediaPlayer(media);
        using ManualResetEventSlim done = new ManualResetEventSlim(false);
        Exception? failure = null;

        void OnEndReached(object? sender, EventArgs e) {
            done.Set();
        }
        void OnError(object? sender, EventArgs e) {
            failure = new InvalidDataException($"LibVLC failed to decode '{filePath}'.");
            done.Set();
        }

        player.EndReached += OnEndReached;
        player.EncounteredError += OnError;

        if (!player.Play()) {
            throw new InvalidDataException($"LibVLC refused to start playback for '{filePath}'.");
        }
        if (!done.Wait(TimeSpan.FromMinutes(5))) {
            player.Stop();
            throw new TimeoutException($"LibVLC transcode of '{filePath}' did not complete within 5 minutes.");
        }
        player.Stop();
        player.EndReached -= OnEndReached;
        player.EncounteredError -= OnError;

        if (failure != null) {
            throw failure;
        }
        if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0) {
            throw new InvalidDataException($"LibVLC produced no PCM output for '{filePath}'.");
        }
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        foreach (IDisposable d in _owned) {
            d.Dispose();
        }
        _owned.Clear();
        _libVlc.Dispose();
        foreach (string tempFile in _tempFiles) {
            try {
                if (File.Exists(tempFile)) {
                    File.Delete(tempFile);
                }
            } catch (IOException) {
                // Best-effort cleanup; another handle may still be open.
            } catch (UnauthorizedAccessException) {
                // Best-effort cleanup.
            }
        }
        _tempFiles.Clear();
    }

    private static void EnsureCoreInitialized() {
        if (Interlocked.CompareExchange(ref s_coreInitialized, 1, 0) != 0) {
            return;
        }
        try {
            Core.Initialize();
        } catch (VLCException ex) {
            Interlocked.Exchange(ref s_coreInitialized, 0);
            throw new InvalidOperationException(
                "LibVLC native binaries could not be loaded. Install VideoLAN.LibVLC.Windows on Windows or the system libvlc package on Linux/macOS.", ex);
        } catch (DllNotFoundException ex) {
            Interlocked.Exchange(ref s_coreInitialized, 0);
            throw new InvalidOperationException(
                "LibVLC native binaries could not be loaded.", ex);
        }
    }
}
