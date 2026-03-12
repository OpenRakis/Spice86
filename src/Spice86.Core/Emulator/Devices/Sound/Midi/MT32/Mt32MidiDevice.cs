namespace Spice86.Core.Emulator.Devices.Sound.Midi.MT32;

using Mt32emu;

using Spice86.Audio.Common;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Shared.Interfaces;

using System.IO.Compression;
using System.Linq;

/// <summary>
/// A MIDI device implementation for playing MIDI files on an MT-32 sound module.
/// </summary>
public sealed class Mt32MidiDevice : MidiDevice {
    private readonly Mt32Context _context;
    private readonly SoundChannel _mixerChannel;

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
    /// <param name="loggerService">The logger service to use for logging messages.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="romsPath"/> is <c>null</c> or empty.</exception>
    public Mt32MidiDevice(SoftwareMixer mixer, string romsPath, ILoggerService loggerService) {
        _mixerChannel = mixer.AddChannel(RenderCallback, 48000, nameof(Mt32MidiDevice), new HashSet<ChannelFeature> {
            ChannelFeature.Sleep,
            ChannelFeature.ReverbSend,
            ChannelFeature.ChorusSend,
            ChannelFeature.Stereo,
            ChannelFeature.Synthesizer
        });
        _context = new();
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
    }

    /// <inheritdoc/>
    protected override void PlayShortMessage(uint message) {
        if (!_disposed) {
            _mixerChannel?.WakeUp();
            _context.PlayMessage(message);
        }
    }

    /// <inheritdoc/>
    protected override void PlaySysex(ReadOnlySpan<byte> data) {
        if (!_disposed) {
            _mixerChannel?.WakeUp();
            _context.PlaySysex(data);
        }
    }

    private void RenderCallback(int framesRequested) {
        int framesRemaining = framesRequested;
        while (framesRemaining > 0) {
            int framesToRender = Math.Min(framesRemaining, _buffer.Length / 2);
            Span<float> renderSpan = _buffer.AsSpan(0, framesToRender * 2);
            renderSpan.Clear();
            _context.Render(renderSpan);

            // MUNT renders normalized floats (-1.0..1.0), but the mixer
            // pipeline expects int16-scale values because the master output
            // divides by 32768.  Scale up to match.
            int sampleCount = framesToRender * 2;
            for (int i = 0; i < sampleCount; i++) {
                renderSpan[i] *= short.MaxValue;
            }

            _mixerChannel.AddSamplesFloat(framesToRender, renderSpan);
            framesRemaining -= framesToRender;
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
                _context.Dispose();
            }
            _disposed = true;
        }
    }
}