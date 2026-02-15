namespace Spice86.Core.Emulator.Devices.Sound.Midi;

using MeltySynth;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM;
using Spice86.Libs.Sound.Common;
using Spice86.Shared.Interfaces;

using System.Linq;

using Windows;

using OperatingSystem = System.OperatingSystem;

/// <summary>
/// Represents an external General MIDI device. <br/>
/// http://midi.teragonaudio.com/tech/lowmidi.htm
/// <remarks>On non-Windows: Uses a soundfont, not the host OS APIs. This is not a MIDI passthrough.</remarks>
/// </summary>
public sealed class GeneralMidiDevice : MidiDevice {
    private readonly Configuration _configuration;
    private readonly MixerChannel? _mixerChannel;
    private readonly Synthesizer? _synthesizer;

    private bool _disposed;

    private volatile uint _message;

    // General MIDI needs a large buffer to store preset PCM data of musical instruments.
    // Too small and it's garbled.
    // Too large and we can't render in time, therefore there is only silence.
    private readonly float[] _buffer = new float[16384];

    /// <summary>
    /// The file name of the soundfont we load and use for all General MIDI preset sounds.
    /// </summary>
    private const string SoundFontResourceName = "Spice86.Core.2MGM.sf2";

    private IntPtr _midiOutHandle;

    /// <summary>
    /// Initializes a new instance of <see cref="GeneralMidiDevice"/>.
    /// </summary>
    /// <param name="configuration">The class that tells us what to run and how.</param>
    /// <param name="mixer">The software mixer for sound channels.</param>
    /// <param name="pauseHandler">The service for handling pause/resume of emulation.</param>
    /// <param name="loggerService">The service used to log messages.</param>
    public GeneralMidiDevice(Configuration configuration, Mixer mixer, IPauseHandler pauseHandler, ILoggerService loggerService) {
        _configuration = configuration;
        if (GetType().Assembly.GetManifestResourceNames().Any(x => x == SoundFontResourceName)) {
            Stream? resource = GetType().Assembly.GetManifestResourceStream(SoundFontResourceName);
            if (resource is not null && configuration.AudioEngine != AudioEngine.Dummy) {
                _synthesizer = new Synthesizer(new SoundFont(resource), 48000);
            }
        }
        if (!OperatingSystem.IsWindows() && configuration.AudioEngine != AudioEngine.Dummy) {
            _mixerChannel = mixer.AddChannel(RenderCallback, 48000, nameof(GeneralMidiDevice), new HashSet<ChannelFeature> { ChannelFeature.Stereo, ChannelFeature.Synthesizer });
            // DON'T enable the channel here - it starts disabled and wakes up on first MIDI message
            // The channel will be enabled when MIDI messages are played (via WakeUp call)
        }
        
        if (OperatingSystem.IsWindows() && configuration.AudioEngine != AudioEngine.Dummy) {
            NativeMethods.midiOutOpen(out _midiOutHandle, NativeMethods.MIDI_MAPPER, IntPtr.Zero, IntPtr.Zero, 0);
        }
    }

    ~GeneralMidiDevice() => Dispose(false);

    private void RenderCallback(int framesRequested) {
        if (_mixerChannel is null) {
            return;
        }

        ((Span<float>)_buffer).Clear();
        FillBuffer(_synthesizer, _buffer);

        _mixerChannel.AudioFrames.Clear();
        for (int i = 0; i < _buffer.Length && i < framesRequested * 2; i += 2) {
            _mixerChannel.AudioFrames.Add(new AudioFrame(_buffer[i], _buffer[i + 1]));
        }
    }

    private void FillBuffer(Synthesizer? synthesizer, Span<float> data) {
        ExtractAndProcessMidiMessage(_message, synthesizer);
        synthesizer?.RenderInterleaved(data);
    }

    protected override void PlayShortMessage(uint message) {
        if (OperatingSystem.IsWindows() && _configuration.AudioEngine != AudioEngine.Dummy) {
            NativeMethods.midiOutShortMsg(_midiOutHandle, message);
        } else {
            _mixerChannel?.WakeUp();
            _message = message;
        }
    }

    private static void ExtractAndProcessMidiMessage(uint packedMessage, Synthesizer? synthesizer) {
        byte[] bytes = BitConverter.GetBytes(packedMessage);

        // Extract MIDI status from the low word, low-order byte
        byte midiStatus = bytes[0];

        // Extract first data byte from the low word, high-order byte
        byte data1 = bytes[1];

        // Extract second data byte from the high word, low-order byte
        byte data2 = bytes[2];

        byte command = (byte)(midiStatus & 0xF0);

        if (command is < 0x80 or > 0xE0) {
            return;
        }

        // it's a voice message
        // find the channel by masking off all but the low 4 bits
        byte channel = (byte)(midiStatus & 0x0F);

        synthesizer?.ProcessMidiMessage(channel, command, data1, data2);
    }

    /// <summary>
    /// Do nothing for General MIDI, as it does not support SysEx messages.
    /// </summary>
    /// <param name="data">The system exclusive data</param>
    protected override void PlaySysex(ReadOnlySpan<byte> data) {
        // NOP
    }

    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                if (OperatingSystem.IsWindows() &&
                    _configuration.AudioEngine != AudioEngine.Dummy &&
                    _midiOutHandle != IntPtr.Zero) {
                    NativeMethods.midiOutClose(_midiOutHandle);
                    _midiOutHandle = IntPtr.Zero;
                }
            }

            _disposed = true;
        }
    }
}