namespace Spice86.Emulator.Sound.PCSpeaker;

using Backend.Audio.OpenAl;

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Spice86.Backend.Audio.OpenAl;

/// <summary>
/// Emulates a PC speaker.
/// </summary>
public sealed class InternalSpeaker {
    /// <summary>
    /// Value into which the input frequency is divided to get the frequency in Hz.
    /// </summary>
    private const double FrequencyFactor = 1193180;

    private readonly int _outputSampleRate = 48000;
    private readonly int _ticksPerSample;
    private readonly LatchedUInt16 _frequencyRegister = new();
    private readonly Stopwatch _durationTimer = new();
    private readonly ConcurrentQueue<QueuedNote> _queuedNotes = new();
    private readonly object _threadStateLock = new();
    private SpeakerControl _controlRegister = SpeakerControl.UseTimer;
    private Task? _generateWaveformTask;
    private readonly CancellationTokenSource _cancelGenerateWaveform = new();
    private int _currentPeriod;
    private Configuration Configuration { get; init; }
    /// <summary>
    /// Initializes a new instance of the InternalSpeaker class.
    /// </summary>
    public InternalSpeaker(Configuration configuration) {
        Configuration = configuration;
        this._frequencyRegister.ValueChanged += this.FrequencyChanged;
        this._ticksPerSample = (int)(Stopwatch.Frequency / (double)this._outputSampleRate);
    }

    /// <summary>
    /// Gets the current frequency in Hz.
    /// </summary>
    private double Frequency => FrequencyFactor / this._frequencyRegister;
    /// <summary>
    /// Gets the current period in samples.
    /// </summary>
    private int PeriodInSamples => (int)(this._outputSampleRate / this.Frequency);

    public byte ReadByte(int port) {
        if (port == 0x61) {
            return (byte)this._controlRegister;
        }

        throw new NotSupportedException();
    }
    public void WriteByte(int port, byte value) {
        if (port == 0x61) {
            SpeakerControl oldValue = this._controlRegister;
            this._controlRegister = (SpeakerControl)value;
            if ((oldValue & SpeakerControl.SpeakerOn) != 0 && (this._controlRegister & SpeakerControl.SpeakerOn) == 0) {
                this.SpeakerDisabled();
            }
        } else if (port == 0x42) {
            this._frequencyRegister.WriteByte(value);
        } else {
            throw new NotSupportedException();
        }
    }

    public void Dispose() {
        this._frequencyRegister.ValueChanged -= this.FrequencyChanged;
        lock (this._threadStateLock) {
            this._cancelGenerateWaveform.Cancel();
        }
    }

    /// <summary>
    /// Fills a buffer with silence.
    /// </summary>
    /// <param name="buffer">Buffer to fill.</param>
    private static void GenerateSilence(Span<byte> buffer) => buffer.Fill(127);

    /// <summary>
    /// Invoked when the speaker has been turned off.
    /// </summary>
    private void SpeakerDisabled() {
        this.EnqueueCurrentNote();
        this._currentPeriod = 0;
    }
    /// <summary>
    /// Invoked when the frequency has changed.
    /// </summary>
    /// <param name="source">Source of the event.</param>
    /// <param name="e">Unused EventArgs instance.</param>
    private void FrequencyChanged(object? source, EventArgs e) {
        this.EnqueueCurrentNote();

        this._durationTimer.Reset();
        this._durationTimer.Start();
        this._currentPeriod = this.PeriodInSamples;
    }
    /// <summary>
    /// Enqueues the current note.
    /// </summary>
    private void EnqueueCurrentNote() {
        if (this._durationTimer.IsRunning && this._currentPeriod != 0) {
            this._durationTimer.Stop();

            int periodDuration = this._ticksPerSample * this._currentPeriod;
            int repetitions = (int)(this._durationTimer.ElapsedTicks / periodDuration);
            this._queuedNotes.Enqueue(new QueuedNote(this._currentPeriod, repetitions));

            lock (this._threadStateLock) {
                if (this._generateWaveformTask == null || this._generateWaveformTask.IsCompleted) {
                    this._generateWaveformTask = Task.Run(this.GenerateWaveformAsync);
                }
            }
        }
    }
    /// <summary>
    /// Fills a buffer with a square wave of the current frequency.
    /// </summary>
    /// <param name="buffer">Buffer to fill.</param>
    /// <param name="period">The number of samples in the period.</param>
    /// <returns>Number of bytes written to the buffer.</returns>
    private int GenerateSquareWave(Span<byte> buffer, int period) {
        if (period < 2) {
            buffer[0] = 127;
            return 1;
        }

        int halfPeriod = period / 2;
        buffer[..halfPeriod].Fill(96);
        buffer.Slice(halfPeriod, halfPeriod).Fill(120);

        return period;
    }
    /// <summary>
    /// Generates the PC speaker waveform.
    /// </summary>
    private async Task GenerateWaveformAsync() {
        if (!Configuration.CreateAudioBackend) {
            return;
        }
        using AudioPlayer? player = Audio.CreatePlayer();
        if (player is null) {
            return;
        }
        FillWithSilence(player);

        byte[]? buffer = new byte[4096];
        byte[]? writeBuffer = buffer;
        bool expandToStereo = false;
        if (player.Format.Channels == 2) {
            writeBuffer = new byte[buffer.Length * 2];
            expandToStereo = true;
        }

        player.BeginPlayback();

        int idleCount = 0;

        while (idleCount < 10000) {
            if (this._queuedNotes.TryDequeue(out QueuedNote note)) {
                int samples = GenerateSquareWave(buffer, note.Period);
                int periods = note.PeriodCount;

                if (expandToStereo) {
                    ChannelAdapter.MonoToStereo(buffer.AsSpan(0, samples), writeBuffer.AsSpan(0, samples * 2));
                    samples *= 2;
                }

                while (periods > 0) {
                    Audio.WriteFullBuffer(player, writeBuffer.AsSpan(0, samples));
                    periods--;
                }

                GenerateSilence(buffer);
                idleCount = 0;
            } else {
                float[]? floatArray = new float[buffer.Length];

                for (int i = 0; i < buffer.Length; i++) {
                    floatArray[i] = buffer[i];
                }


                while (player.WriteData(floatArray.AsSpan()) > 0) {
                }

                await Task.Delay(5, this._cancelGenerateWaveform.Token);
                idleCount++;
            }

            this._cancelGenerateWaveform.Token.ThrowIfCancellationRequested();
        }
    }

    private static void FillWithSilence(AudioPlayer player) {
        float[]? buffer = new float[4096];
        Span<float> span = buffer.AsSpan();

        while (player.WriteData(span) > 0) {
        }
    }
}
