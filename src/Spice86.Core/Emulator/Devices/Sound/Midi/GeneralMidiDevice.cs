namespace Spice86.Core.Emulator.Devices.Sound.Midi;

using MeltySynth;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using Windows;

using OperatingSystem = System.OperatingSystem;

/// <summary>
/// Represents an external General MIDI device. <br/>
/// http://midi.teragonaudio.com/tech/lowmidi.htm
/// <remarks>On non-Windows: Uses a soundfont, not the host OS APIs. This is not a MIDI passthrough.</remarks>
/// </summary>
internal sealed class GeneralMidiDevice : MidiDevice {
    private readonly SoundChannel _soundChannel;
    private readonly Synthesizer _synthesizer;

    private bool _disposed;
    private bool _threadStarted;

    private readonly ManualResetEvent _fillBufferEvent = new(false);
    private readonly Thread? _playbackThread;
    private readonly ILoggerService _loggerService;
    private readonly IPauseHandler _pauseHandler;
    private volatile bool _endThread;
    private volatile uint _message;

    /// <summary>
    /// The file name of the soundfont we load and use for all General MIDI preset sounds.
    /// </summary>
    public const string SoundFont = "2MGM.sf2";

    private IntPtr _midiOutHandle;

    public GeneralMidiDevice(Synthesizer synthesizer, SoundChannel generalMidiSoundChannel, ILoggerService loggerService,  IPauseHandler pauseHandler) {
        _synthesizer = synthesizer;
        _pauseHandler = pauseHandler;
        _loggerService = loggerService;
        _soundChannel = generalMidiSoundChannel;
        _playbackThread = new Thread(RenderThreadMethod) {
            Name = nameof(GeneralMidiDevice)
        };
        if (OperatingSystem.IsWindows()) {
            NativeMethods.midiOutOpen(out _midiOutHandle, NativeMethods.MIDI_MAPPER, IntPtr.Zero, IntPtr.Zero, 0);
        }
    }

    ~GeneralMidiDevice() => Dispose(false);

    private void StartThreadIfNeeded() {
        if (_disposed || _endThread || _threadStarted || _playbackThread == null) {
            return;
        }
        _loggerService.Information("Starting thread '{ThreadName}'", _playbackThread.Name ?? nameof(GeneralMidiDevice));
        _threadStarted = true;
        _playbackThread.Start();
    }

    private void RenderThreadMethod() {
        if (!File.Exists(SoundFont)) {
            return;
        }

        // General MIDI needs a large buffer to store preset PCM data of musical instruments.
        // Too small and it's garbled.
        // Too large and we can't render in time, therefore there is only silence.
        Span<float> buffer = stackalloc float[16384];
        while (!_endThread) {
            _pauseHandler.WaitIfPaused();
            _fillBufferEvent.WaitOne(Timeout.Infinite);
            buffer.Clear();

            FillBuffer(_synthesizer, buffer);
            _soundChannel.Render(buffer);
            _fillBufferEvent.Reset();
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
        if (!_disposed && !_endThread) {
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

        byte command = (byte)(midiStatus & 0xF0);

        if (command is < 0x80 or > 0xE0) {
            return;
        }

        // it's a voice message
        // find the channel by masking off all but the low 4 bits
        byte channel = (byte)(midiStatus & 0x0F);

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
            if (disposing) {
                if (OperatingSystem.IsWindows()) {
                    if (_midiOutHandle != IntPtr.Zero) {
                        NativeMethods.midiOutClose(_midiOutHandle);
                        _midiOutHandle = IntPtr.Zero;
                    }
                }

                _endThread = true;
                _fillBufferEvent.Set();
                if (_playbackThread?.IsAlive == true) {
                    _playbackThread.Join();
                }

                _fillBufferEvent.Dispose();
            }

            _disposed = true;
        }
    }
}