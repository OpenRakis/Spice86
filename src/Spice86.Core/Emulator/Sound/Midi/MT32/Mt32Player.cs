namespace Spice86.Core.Emulator.Sound.Midi.MT32;

using System.IO;
using System.IO.Compression;
using System.Linq;

using Mt32emu;

using Spice86.Shared.Interfaces;
using Spice86.Core.Backend.Audio;
using Spice86.Core.Emulator.Sound;

internal sealed class Mt32Player : IDisposable {
    private readonly Mt32Context _context = new();
    private readonly AudioPlayer? _audioPlayer;
    private bool _disposed;
    private bool _threadStarted;

    private readonly Thread? _renderThread;

    private bool _exitRenderThread = false;

    private readonly ManualResetEvent _fillBufferEvent = new(false);

    private readonly ILoggerService _loggerService;

    public Mt32Player(string romsPath, ILoggerService loggerService) {
        _loggerService = loggerService;
        if (string.IsNullOrWhiteSpace(romsPath)) {
            throw new ArgumentNullException(nameof(romsPath));
        }

        _audioPlayer = Audio.CreatePlayer();
        if (_audioPlayer is null) {
            return;
        }
        if(!LoadRoms(romsPath)) {
            if(_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _loggerService.Error("{MethodName} could not find roms in {RomsPath}, {ClassName} was not created", nameof(LoadRoms), romsPath, nameof(Mt32Player));
            }
            return;
        }

        _context.AnalogOutputMode = Mt32GlobalState.GetBestAnalogOutputMode(_audioPlayer.Format.SampleRate);
        _context.SetSampleRate(_audioPlayer.Format.SampleRate);

        _context.OpenSynth();

        _renderThread = new Thread(RenderThreadMethod) {
            Name = "MT32Audio"
        };
    }

    private void StartThreadIfNeeded() {
        if(!_disposed && !_exitRenderThread && !_threadStarted) {
            _threadStarted = true;
            _renderThread?.Start();
        }
    }

    private void RenderThreadMethod() {
        Span<float> buffer = stackalloc float[128];
        while(!_exitRenderThread) {
            if(!_exitRenderThread) {
                _fillBufferEvent.WaitOne(1);
            }
            buffer.Clear();
            _context.Render(buffer);
            _audioPlayer?.WriteData(buffer);
        }
    }

    public void PlayShortMessage(uint message) {
        StartThreadIfNeeded();
        if(!_disposed && !_exitRenderThread) {
            _context.PlayMessage(message);
            RaiseFillBufferEvent();
        }
    }

    public void PlaySysex(ReadOnlySpan<byte> data) {
        StartThreadIfNeeded();
        if (!_disposed && !_exitRenderThread) {
            _context.PlaySysex(data);
            RaiseFillBufferEvent();
        }
    }

    private void RaiseFillBufferEvent() {
        if(!_disposed && !_exitRenderThread) {
            _fillBufferEvent.Set();
        }
    }

    /// <inheritdoc/>
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if(disposing) {
                _exitRenderThread = true;
                _fillBufferEvent.Set();
                if(_renderThread?.IsAlive == true) {
                    _renderThread.Join();
                }
                _context.Dispose();
                _fillBufferEvent.Dispose();
                _audioPlayer?.Dispose();
            }
            _disposed = true;
        }
    }

    private bool LoadRoms(string path) {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
            using ZipArchive zip = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read);
            bool foundRom = false;
            for (int i = 0; i < zip.Entries.Count; i++) {
                ZipArchiveEntry entry = zip.Entries[i];
                if (entry.FullName.EndsWith(".ROM", StringComparison.OrdinalIgnoreCase)) {
                    using Stream stream = entry.Open();
                    _context.AddRom(stream);
                    foundRom = true;
                }
            }
            return foundRom;
        } else if (Directory.Exists(path)) {
            IEnumerable<string> fileNames = Directory.EnumerateFiles(path, "*.ROM");
            foreach (string? fileName in fileNames) {
                _context.AddRom(fileName);
            }
            return fileNames.Any();
        }
        return false;
    }
}
