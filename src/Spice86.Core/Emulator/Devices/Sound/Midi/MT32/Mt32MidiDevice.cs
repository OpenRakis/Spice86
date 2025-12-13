namespace Spice86.Core.Emulator.Devices.Sound.Midi.MT32;

using Mt32emu;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM;
using Spice86.Libs.Sound.Common;
using Spice86.Shared.Interfaces;

using System.IO.Compression;
using System.Linq;

/// <summary>
/// A MIDI device implementation for playing MIDI files on an MT-32 sound module.
/// </summary>
public sealed class Mt32MidiDevice : MidiDevice {
    private readonly Mt32Context _context;
    private readonly MixerChannel _mixerChannel;
    private readonly DeviceThread _deviceThread;

    /// <summary>
    /// Indicates whether this object has been disposed.
    /// </summary>
    private bool _disposed;

    private readonly float[] _buffer = new float[128];

    /// <summary>
    /// Constructs an instance of <see cref="Mt32MidiDevice"/>.
    /// </summary>
    /// <param name="mixer">The software mixer for sound channels.</param>
    /// <param name="romsPath">The path to the MT-32 ROM files.</param>
    /// <param name="pauseHandler">The service for handling pause/resume of emulation.</param>
    /// <param name="loggerService">The logger service to use for logging messages.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="romsPath"/> is <c>null</c> or empty.</exception>
    public Mt32MidiDevice(Mixer mixer, string romsPath, IPauseHandler pauseHandler, ILoggerService loggerService) {
        _mixerChannel = mixer.AddChannel(RenderCallback, 48000, nameof(Mt32MidiDevice), new HashSet<ChannelFeature> { ChannelFeature.Stereo, ChannelFeature.Synthesizer });
        _context = new();
        _deviceThread = new DeviceThread(nameof(Mt32MidiDevice), PlaybackLoopBody, pauseHandler, loggerService);
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
        _mixerChannel.Enable(true);
    }

    /// <inheritdoc/>
    protected override void PlayShortMessage(uint message) {
        if (!_disposed) {
            _deviceThread.StartThreadIfNeeded();
            _context.PlayMessage(message);
        }
    }

    /// <inheritdoc/>
    protected override void PlaySysex(ReadOnlySpan<byte> data) {
        if (!_disposed) {
            _deviceThread.StartThreadIfNeeded();
            _context.PlaySysex(data);
        }
    }

    private void PlaybackLoopBody() {
        ((Span<float>)_buffer).Clear();
        _context.Render(_buffer);
    }

    private void RenderCallback(int framesRequested) {
        ((Span<float>)_buffer).Clear();
        _context.Render(_buffer);

        _mixerChannel.AudioFrames.Clear();
        for (int i = 0; i < _buffer.Length && i < framesRequested * 2; i += 2) {
            _mixerChannel.AudioFrames.Add(new AudioFrame(_buffer[i], _buffer[i + 1]));
        }
    }

    private bool LoadRoms(string path) {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
            if (!Directory.Exists(path)) {
                return false;
            }

            EnumerationOptions enumerationOptions = new();
            enumerationOptions.MatchCasing = MatchCasing.CaseInsensitive;
            IEnumerable<string> fileNames = Directory.EnumerateFiles(path, "*.ROM", enumerationOptions);
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
                _deviceThread.Dispose();
                _context.Dispose();
            }
            _disposed = true;
        }
    }
}