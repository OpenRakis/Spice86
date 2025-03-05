namespace Spice86.Core.Emulator.Devices.Sound.Midi;

using MeltySynth;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM;
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
    private readonly SoundChannel? _soundChannel;
    private readonly Synthesizer? _synthesizer;

    private bool _disposed;

    private readonly DeviceThread _deviceThread;
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
    /// <param name="softwareMixer">The software mixer for sound channels.</param>
    /// <param name="pauseHandler">The service for handling pause/resume of emulation.</param>
    /// <param name="loggerService">The service used to log messages.</param>
    public GeneralMidiDevice(SoftwareMixer softwareMixer, IPauseHandler pauseHandler, ILoggerService loggerService) {
        if (GetType().Assembly.GetManifestResourceNames().Any(x => x == SoundFontResourceName)) {
            Stream? resource = GetType().Assembly.GetManifestResourceStream(SoundFontResourceName);
            if (resource is not null) {
                _synthesizer = new Synthesizer(new SoundFont(resource), 48000);
            }
        }
        if (!OperatingSystem.IsWindows()) {
            _soundChannel = softwareMixer.CreateChannel(nameof(GeneralMidiDevice));
        }
        
        _deviceThread = new DeviceThread(nameof(GeneralMidiDevice), PlaybackLoopBody, pauseHandler, loggerService);
        if (OperatingSystem.IsWindows()) {
            NativeMethods.midiOutOpen(out _midiOutHandle, NativeMethods.MIDI_MAPPER, IntPtr.Zero, IntPtr.Zero, 0);
        }
    }

    ~GeneralMidiDevice() => Dispose(false);

    private void PlaybackLoopBody() {
        ((Span<float>)_buffer).Clear();

        FillBuffer(_synthesizer, _buffer);
        _soundChannel?.Render(_buffer);
    }

    private void FillBuffer(Synthesizer? synthesizer, Span<float> data) {
        ExtractAndProcessMidiMessage(_message, synthesizer);
        synthesizer?.RenderInterleaved(data);
    }

    protected override void PlayShortMessage(uint message) {
        if (OperatingSystem.IsWindows()) {
            NativeMethods.midiOutShortMsg(_midiOutHandle, message);
        } else {
            _deviceThread.StartThreadIfNeeded();
            _message = message;
            _deviceThread.Resume();
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
                if (OperatingSystem.IsWindows()) {
                    if (_midiOutHandle != IntPtr.Zero) {
                        NativeMethods.midiOutClose(_midiOutHandle);
                        _midiOutHandle = IntPtr.Zero;
                    }
                }
                _deviceThread.Dispose();
            }

            _disposed = true;
        }
    }
}