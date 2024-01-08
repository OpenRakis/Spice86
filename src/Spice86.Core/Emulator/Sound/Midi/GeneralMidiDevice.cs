namespace Spice86.Core.Emulator.Sound.Midi;

using MeltySynth;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Emulator.Sound.Midi.Windows;

using OperatingSystem = System.OperatingSystem;

/// <summary>
/// Represents an external General MIDI device. <br/>
/// http://midi.teragonaudio.com/tech/lowmidi.htm
/// <remarks>On non-Windows: Uses a soundfont, not the host OS APIs. This is not a MIDI passthrough.</remarks>
/// </summary>
internal sealed class GeneralMidiDevice : MidiDevice {
    private readonly AudioPlayer _audioPlayer;

    private bool _disposed;
    private bool _threadStarted;

    private readonly ManualResetEvent _fillBufferEvent = new(false);
    private readonly Thread? _playbackThread;
    private volatile bool _endThread;
    private volatile uint _message;

    /// <summary>
    /// The soundfont file name we use for all General MIDI preset sounds.
    /// </summary>
    private const string SoundFont = "2MGM.sf2";

    private IntPtr _midiOutHandle;

    public GeneralMidiDevice(AudioPlayerFactory audioPlayerFactory) {
        _audioPlayer = audioPlayerFactory.CreatePlayer(48000, 2048);
        _playbackThread = new Thread(RenderThreadMethod) {
            Name = "GeneralMIDIAudio"
        };
        if (OperatingSystem.IsWindows()) {
            NativeMethods.midiOutOpen(out _midiOutHandle, NativeMethods.MIDI_MAPPER, IntPtr.Zero, IntPtr.Zero, 0);
        }
    }

    ~GeneralMidiDevice() => Dispose(false);

    private void StartThreadIfNeeded() {
        if(!_disposed && !_endThread && !_threadStarted) {
            _playbackThread?.Start();
            _threadStarted = true;
        }
    }

    private void RenderThreadMethod() {
        if (!File.Exists(SoundFont)) {
            return;
        }
        // General MIDI needs a large buffer to store preset PCM data of musical instruments.
        // Too small and it's garbled.
        // Too large and we can't render in time, therefore there is only silence.
        Span<float> data = stackalloc float[16384];
        Synthesizer synthesizer = new(new SoundFont(SoundFont), _audioPlayer.Format.SampleRate);
        while (!_endThread) {
            if(!_endThread) {
                _fillBufferEvent.WaitOne(Timeout.Infinite);
            }
            FillBuffer(synthesizer, data);
            _audioPlayer.WriteFullBuffer(data);
            data.Clear();
        }
    }

    private void FillBuffer(Synthesizer synthesizer, Span<float> data) {
        ExtractAndProcessMidiMessage(_message, synthesizer);
        synthesizer.RenderInterleaved(data);
    }

    protected override void PlayShortMessage(uint message) {
        if (OperatingSystem.IsWindows()) {
            NativeMethods.midiOutShortMsg(_midiOutHandle, message);
        } else {
            StartThreadIfNeeded();
            _message = message;
            WakeUpRenderThread();
        }
    }

    private void WakeUpRenderThread() {
        if(!_disposed && !_endThread) {
            _fillBufferEvent.Set();
        }
    }

    private static void ExtractAndProcessMidiMessage(uint packedMessage, Synthesizer synthesizer) {
        byte[] bytes = BitConverter.GetBytes(packedMessage);

        // Extract MIDI status from the low word, low-order byte
        byte midiStatus = bytes[0];

        // Extract first data byte from the low word, high-order byte
        byte data1 = bytes[1];

        // Extract second data byte from the high word, low-order byte
        byte data2 = bytes[2];

        byte command = (byte) (midiStatus & 0xF0);

        if (command is < 0x80 or > 0xE0) {
            return;
        }

        // it's a voice message
        // find the channel by masking off all but the low 4 bits
        byte channel = (byte) (midiStatus & 0x0F);

        synthesizer.ProcessMidiMessage(channel, command, data1, data2);
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
            if(disposing) {
                if(OperatingSystem.IsWindows()) {
                    if (_midiOutHandle != IntPtr.Zero) {
                        NativeMethods.midiOutClose(_midiOutHandle);
                        _midiOutHandle = IntPtr.Zero;
                    }
                }
                _endThread = true;
                _fillBufferEvent.Set();
                if(_playbackThread?.IsAlive == true) {
                    _playbackThread.Join();
                }
                _fillBufferEvent.Dispose();
                _audioPlayer.Dispose();
            }
            _disposed = true;
        }
    }
}
