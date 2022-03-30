namespace Spice86.Emulator.Sound.PCSpeaker;

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using TinyAudio;

/// <summary>
/// Emulates a PC speaker.
/// </summary>
public sealed class InternalSpeaker {
    /// <summary>
    /// Value into which the input frequency is divided to get the frequency in Hz.
    /// </summary>
    private const double FrequencyFactor = 1193180;

    private readonly int outputSampleRate = 48000;
    private readonly int ticksPerSample;
    private readonly LatchedUInt16 frequencyRegister = new();
    private readonly Stopwatch durationTimer = new();
    private readonly ConcurrentQueue<QueuedNote> queuedNotes = new();
    private readonly object threadStateLock = new();
    private SpeakerControl controlRegister = SpeakerControl.UseTimer;
    private Task? generateWaveformTask;
    private readonly CancellationTokenSource cancelGenerateWaveform = new();
    private int currentPeriod;
    private Configuration Configuration { get; init; }
    /// <summary>
    /// Initializes a new instance of the InternalSpeaker class.
    /// </summary>
    public InternalSpeaker(Configuration configuration) {
        Configuration = configuration;
        this.frequencyRegister.ValueChanged += this.FrequencyChanged;
        this.ticksPerSample = (int)(Stopwatch.Frequency / (double)this.outputSampleRate);
    }

    /// <summary>
    /// Gets the current frequency in Hz.
    /// </summary>
    private double Frequency => FrequencyFactor / this.frequencyRegister;
    /// <summary>
    /// Gets the current period in samples.
    /// </summary>
    private int PeriodInSamples => (int)(this.outputSampleRate / this.Frequency);

    public byte ReadByte(int port) {
        if (port == 0x61) {
            return (byte)this.controlRegister;
        }

        throw new NotSupportedException();
    }
    public void WriteByte(int port, byte value) {
        if (port == 0x61) {
            SpeakerControl oldValue = this.controlRegister;
            this.controlRegister = (SpeakerControl)value;
            if ((oldValue & SpeakerControl.SpeakerOn) != 0 && (this.controlRegister & SpeakerControl.SpeakerOn) == 0) {
                this.SpeakerDisabled();
            }
        } else if (port == 0x42) {
            this.frequencyRegister.WriteByte(value);
        } else {
            throw new NotSupportedException();
        }
    }

    public void Dispose() {
        this.frequencyRegister.ValueChanged -= this.FrequencyChanged;
        lock (this.threadStateLock) {
            this.cancelGenerateWaveform.Cancel();
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
        this.currentPeriod = 0;
    }
    /// <summary>
    /// Invoked when the frequency has changed.
    /// </summary>
    /// <param name="source">Source of the event.</param>
    /// <param name="e">Unused EventArgs instance.</param>
    private void FrequencyChanged(object? source, EventArgs e) {
        this.EnqueueCurrentNote();

        this.durationTimer.Reset();
        this.durationTimer.Start();
        this.currentPeriod = this.PeriodInSamples;
    }
    /// <summary>
    /// Enqueues the current note.
    /// </summary>
    private void EnqueueCurrentNote() {
        if (this.durationTimer.IsRunning && this.currentPeriod != 0) {
            this.durationTimer.Stop();

            int periodDuration = this.ticksPerSample * this.currentPeriod;
            int repetitions = (int)(this.durationTimer.ElapsedTicks / periodDuration);
            this.queuedNotes.Enqueue(new QueuedNote(this.currentPeriod, repetitions));

            lock (this.threadStateLock) {
                if (this.generateWaveformTask == null || this.generateWaveformTask.IsCompleted) {
                    this.generateWaveformTask = Task.Run(this.GenerateWaveformAsync);
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
        if (!OperatingSystem.IsWindows()) {
            return;
        }
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
            if (this.queuedNotes.TryDequeue(out QueuedNote note)) {
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

                await Task.Delay(5, this.cancelGenerateWaveform.Token);
                idleCount++;
            }

            this.cancelGenerateWaveform.Token.ThrowIfCancellationRequested();
        }
    }

    private static void FillWithSilence(AudioPlayer player) {
        if (!OperatingSystem.IsWindows()) {
            return;
        }
        float[]? buffer = new float[4096];
        Span<float> span = buffer.AsSpan();

        while (player.WriteData(span) > 0) {
        }
    }
}
