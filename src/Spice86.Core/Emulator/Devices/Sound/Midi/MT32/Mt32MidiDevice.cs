namespace Spice86.Core.Emulator.Devices.Sound.Midi.MT32;

using Mt32emu;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Shared.Interfaces;

using System.IO.Compression;
using System.Linq;

/// <summary>
/// A MIDI device implementation for playing MIDI files on an MT-32 sound module.
/// </summary>
public sealed class Mt32MidiDevice : MidiDevice {
    private readonly Mt32Context _context;
    private readonly SoundChannel _soundChannel;
    private readonly Thread? _renderThread;
    private readonly ManualResetEvent _fillBufferEvent = new(false);

    private bool _threadStarted;
    private bool _exitRenderThread;

    /// <summary>
    /// Indicates whether this object has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Constructs an instance of <see cref="Mt32MidiDevice"/>.
    /// </summary>
    /// <param name="mt32SoundChannel">The software mixer's sound channel for the MT-32.</param>
    /// <param name="romsPath">The path to the MT-32 ROM files.</param>
    /// <param name="loggerService">The logger service to use for logging messages.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="romsPath"/> is <c>null</c> or empty.</exception>
    public Mt32MidiDevice(SoundChannel mt32SoundChannel, string romsPath, ILoggerService loggerService) {
        _context = new();
        _soundChannel = mt32SoundChannel;
        if (string.IsNullOrWhiteSpace(romsPath)) {
            throw new ArgumentNullException(nameof(romsPath));
        }
        
        if (!LoadRoms(romsPath)) {
            if (loggerService.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                loggerService.Error("{MethodName} could not find roms in {RomsPath}, {ClassName} was not created",
                    nameof(LoadRoms), romsPath, nameof(Mt32MidiDevice));
            }
            return;
        }
        _context.AnalogOutputMode = Mt32GlobalState.GetBestAnalogOutputMode(48000);
        _context.SetSampleRate(48000);
        _context.OpenSynth();

        _renderThread = new Thread(RenderThreadMethod) {
            Name = nameof(Mt32MidiDevice)
        };
    }

    /// <inheritdoc/>
    protected override void PlayShortMessage(uint message) {
        StartThreadIfNeeded();
        if (!_disposed && !_exitRenderThread) {
            _context.PlayMessage(message);
            RaiseFillBufferEvent();
        }
    }

    /// <inheritdoc/>
    protected override void PlaySysex(ReadOnlySpan<byte> data) {
        StartThreadIfNeeded();
        if (!_disposed && !_exitRenderThread) {
            _context.PlaySysex(data);
            RaiseFillBufferEvent();
        }
    }


    private void StartThreadIfNeeded() {
        if (!_disposed && !_exitRenderThread && !_threadStarted) {
            _threadStarted = true;
            _renderThread?.Start();
        }
    }

    private void RenderThreadMethod() {
        Span<float> buffer = stackalloc float[128];
        while (!_exitRenderThread) {
            if (!_exitRenderThread) {
                _fillBufferEvent.WaitOne(1);
            }

            buffer.Clear();
            _context.Render(buffer);
            _soundChannel.Render(buffer);
        }
    }

    private void RaiseFillBufferEvent() {
        if (!_disposed && !_exitRenderThread) {
            _fillBufferEvent.Set();
        }
    }

    private bool LoadRoms(string path) {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
            if (!Directory.Exists(path)) {
                return false;
            }
            IEnumerable<string> fileNames = Directory.EnumerateFiles(path, "*.ROM");
            IEnumerable<string> enumerable = fileNames as string[] ?? fileNames.ToArray();
            foreach (string? fileName in enumerable) {
                _context.AddRom(fileName);
            }
            return enumerable.Any();
        }
        using ZipArchive zip = new(File.OpenRead(path), ZipArchiveMode.Read);
        bool foundRom = false;
        foreach (ZipArchiveEntry entry in zip.Entries) {
            if (!entry.FullName.EndsWith(".ROM", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            using Stream stream = entry.Open();
            _context.AddRom(stream);
            foundRom = true;
        }
        return foundRom;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _exitRenderThread = true;
                _fillBufferEvent.Set();
                if (_renderThread?.IsAlive == true) {
                    _renderThread.Join();
                }
                _context.Dispose();
                _fillBufferEvent.Dispose();
            }
            _disposed = true;
        }
    }
}