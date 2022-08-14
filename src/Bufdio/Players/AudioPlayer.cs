using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bufdio.Common;
using Bufdio.Decoders;
using Bufdio.Decoders.FFmpeg;
using Bufdio.Engines;
using Bufdio.Exceptions;
using Bufdio.Processors;
using Bufdio.Utilities;
using Bufdio.Utilities.Extensions;

namespace Bufdio.Players;

/// <summary>
/// A class that provides functionalities for loading and controlling audio playback.
/// <para>Implements: <see cref="IAudioPlayer"/></para>
/// </summary>
public class AudioPlayer : IAudioPlayer
{
    private const int MinQueueSize = 8;
    private const int MaxQueueSize = 128;
    private bool _disposed;

    /// <summary>
    /// Initializes <see cref="AudioPlayer"/> instance by providing <see cref="IAudioEngine"/> instance.
    /// </summary>
    /// <param name="engine">An <see cref="IAudioEngine"/> instance.</param>
    public AudioPlayer(IAudioEngine engine)
    {
        Ensure.NotNull(engine, nameof(engine));
        Engine = engine;
        VolumeProcessor = new VolumeProcessor { Volume = 1 };
        Queue = new ConcurrentQueue<AudioFrame>();
    }

    /// <summary>
    /// Initializes <see cref="AudioPlayer"/> instance by using default audio engine.
    /// </summary>
    public AudioPlayer() : this(new PortAudioEngine())
    {
    }

    /// <inheritdoc />
    public event EventHandler StateChanged;

    /// <inheritdoc />
    public event EventHandler PositionChanged;

    /// <inheritdoc />
    public bool IsLoaded { get; protected set; }

    /// <inheritdoc />
    public TimeSpan Duration { get; protected set; }

    /// <inheritdoc />
    public TimeSpan Position { get; protected set; }

    /// <inheritdoc />
    public PlaybackState State { get; protected set; }

    /// <inheritdoc />
    public bool IsSeeking { get; private set; }

    /// <inheritdoc />
    public float Volume
    {
        get => VolumeProcessor.Volume;
        set => VolumeProcessor.Volume = value.VerifyVolume();
    }

    /// <inheritdoc />
    public ISampleProcessor CustomSampleProcessor { get; set; }

    /// <inheritdoc />
    public ILogger Logger { get; set; }

    /// <summary>
    /// Gets or sets current <see cref="IAudioDecoder"/> instance.
    /// </summary>
    protected IAudioDecoder CurrentDecoder { get; set; }

    /// <summary>
    /// Gets or sets current specified audio URL.
    /// </summary>
    protected string CurrentUrl { get; set; }

    /// <summary>
    /// Gets or sets current specified audio stream.
    /// </summary>
    protected Stream CurrentStream { get; set; }

    /// <summary>
    /// Gets <see cref="IAudioEngine"/> instance.
    /// </summary>
    protected IAudioEngine Engine { get; }

    /// <summary>
    /// Gets <see cref="VolumeProcessor"/> instance.
    /// </summary>
    protected VolumeProcessor VolumeProcessor { get; }

    /// <summary>
    /// Gets queue object that holds queued audio frames.
    /// </summary>
    protected ConcurrentQueue<AudioFrame> Queue { get; }

    /// <summary>
    /// Gets current audio decoder thread.
    /// </summary>
    protected Thread DecoderThread { get; private set; }

    /// <summary>
    /// Gets current audio engine thread.
    /// </summary>
    protected Thread EngineThread { get; private set; }

    /// <summary>
    /// Gets whether or not the decoder thread reach end of file.
    /// </summary>
    protected bool IsEOF { get; private set; }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when given url is null.</exception>
    public Task<bool> LoadAsync(string url)
    {
        Ensure.NotNull(url, nameof(url));
        Ensure.That<BufdioException>(State == PlaybackState.Idle, "Playback thread is currently running.");

        LoadInternal(() => CreateDecoder(url));

        if (IsLoaded)
        {
            CurrentUrl = url;
            CurrentStream = null;
        }

        return Task.FromResult(IsLoaded);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when given stream is null.</exception>
    public Task<bool> LoadAsync(Stream stream)
    {
        Ensure.NotNull(stream, nameof(stream));
        Ensure.That<BufdioException>(State == PlaybackState.Idle, "Playback thread is currently running.");

        LoadInternal(() => CreateDecoder(stream));

        if (IsLoaded)
        {
            CurrentUrl = null;
            CurrentStream = stream;
        }

        return Task.FromResult(IsLoaded);
    }

    /// <inheritdoc />
    /// <exception cref="BufdioException">Thrown when audio is not loaded.</exception>
    public void Play()
    {
        Ensure.That<BufdioException>(IsLoaded, "No loaded audio for playback.");

        if (State is PlaybackState.Playing or PlaybackState.Buffering)
        {
            return;
        }

        if (State == PlaybackState.Paused)
        {
            SetAndRaiseStateChanged(PlaybackState.Playing);
            return;
        }

        EnsureThreadsDone();

        Seek(Position);
        IsEOF = false;

        DecoderThread = new Thread(RunDecoder) { Name = "Decoder Thread", IsBackground = true };
        EngineThread = new Thread(RunEngine) { Name = "Engine Thread", IsBackground = true };

        SetAndRaiseStateChanged(PlaybackState.Playing);

        DecoderThread.Start();
        EngineThread.Start();
    }

    /// <inheritdoc />
    public void Pause()
    {
        if (State is PlaybackState.Playing or PlaybackState.Buffering)
        {
            SetAndRaiseStateChanged(PlaybackState.Paused);
        }
    }

    /// <inheritdoc />
    public void Seek(TimeSpan position)
    {
        if (!IsLoaded || IsSeeking || CurrentDecoder == null)
        {
            return;
        }

        IsSeeking = true;
        Queue.Clear();

        // Sleep to produce smooth seek
        if (DecoderThread is { IsAlive: true } || EngineThread is { IsAlive: true })
        {
            Thread.Sleep(100);
        }

        Logger?.LogInfo($"Seeking to: {position}.");

        if (!CurrentDecoder.TrySeek(position, out var error))
        {
            Logger?.LogError($"Unable to seek audio stream: {error}");
            IsSeeking = false;
            return;
        }

        IsSeeking = false;
        SetAndRaisePositionChanged(position);

        Logger?.LogInfo($"Successfully seeks to {position}.");
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (State == PlaybackState.Idle)
        {
            return;
        }

        State = PlaybackState.Idle;
        EnsureThreadsDone();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates an <see cref="IAudioDecoder"/> instance.
    /// By default, it will returns a new <see cref="FFmpegDecoder"/> instance.
    /// </summary>
    /// <param name="url">Audio URL or path to be loaded.</param>
    /// <returns>A new <see cref="FFmpegDecoder"/> instance.</returns>
    protected virtual IAudioDecoder CreateDecoder(string url)
    {
        return new FFmpegDecoder(url);
    }

    /// <summary>
    /// Creates an <see cref="IAudioDecoder"/> instance.
    /// By default, it will returns a new <see cref="FFmpegDecoder"/> instance.
    /// </summary>
    /// <param name="stream">Audio stream to be loaded.</param>
    /// <returns>A new <see cref="FFmpegDecoder"/> instance.</returns>
    protected virtual IAudioDecoder CreateDecoder(Stream stream)
    {
        return new FFmpegDecoder(stream);
    }

    /// <summary>
    /// Sets <see cref="State"/> value and raise <see cref="StateChanged"/> if value is changed.
    /// </summary>
    /// <param name="state">Playback state.</param>
    protected virtual void SetAndRaiseStateChanged(PlaybackState state)
    {
        var raise = State != state;
        State = state;

        if (raise && StateChanged != null)
        {
            StateChanged.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Sets <see cref="Position"/> value and raise <see cref="PositionChanged"/> if value is changed.
    /// </summary>
    /// <param name="position">Playback position.</param>
    protected virtual void SetAndRaisePositionChanged(TimeSpan position)
    {
        var raise = position != Position;
        Position = position;

        if (raise && PositionChanged != null)
        {
            PositionChanged.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Handles audio decoder error, returns <c>true</c> to continue decoder thread, <c>false</c> will
    /// break the thread. By default, this will try to re-initializes <see cref="CurrentDecoder"/>
    /// and seeks to the last position.
    /// </summary>
    /// <param name="result">Failed audio decoder result.</param>
    /// <returns><c>true</c> will continue decoder thread, <c>false</c> will break the thread.</returns>
    protected virtual bool HandleDecoderError(AudioDecoderResult result)
    {
        Queue.Clear();
        Logger?.LogWarning($"Failed to decode audio frame, retrying: {result.ErrorMessage}");

        CurrentDecoder?.Dispose();
        CurrentDecoder = null;

        while (CurrentDecoder == null)
        {
            if (State == PlaybackState.Idle)
            {
                IsLoaded = false;
                return false;
            }

            try
            {
                CurrentDecoder = CurrentUrl != null ? CreateDecoder(CurrentUrl) : CreateDecoder(CurrentStream);
                break;
            }
            catch (Exception ex)
            {
                Logger?.LogWarning($"Unable to recreate audio decoder, retrying: {ex.Message}");
                Thread.Sleep(1000);
            }
        }

        Logger?.LogInfo($"Audio decoder has been recreated, seeking to the last position ({Position}).");
        Seek(Position);

        return true;
    }

    /// <summary>
    /// Run <see cref="VolumeProcessor"/> and <see cref="CustomSampleProcessor"/> to the specified samples.
    /// </summary>
    /// <param name="samples">Audio samples to process to.</param>
    protected virtual void ProcessSampleProcessors(Span<float> samples)
    {
        if (CustomSampleProcessor is { IsEnabled: true })
        {
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = CustomSampleProcessor.Process(samples[i]);
            }
        }

        if (VolumeProcessor.Volume != 1.0f)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = VolumeProcessor.Process(samples[i]);
            }
        }
    }

    private void LoadInternal(Func<IAudioDecoder> decoderFactory)
    {
        Logger?.LogInfo("Loading audio to the player.");

        CurrentDecoder?.Dispose();
        CurrentDecoder = null;
        IsLoaded = false;

        try
        {
            CurrentDecoder = decoderFactory();
            Duration = CurrentDecoder.StreamInfo.Duration;

            Logger?.LogInfo("Audio successfully loaded.");
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            CurrentDecoder = null;
            Logger?.LogError($"Failed to load audio: {ex.Message}");
            IsLoaded = false;
        }

        SetAndRaisePositionChanged(TimeSpan.Zero);
    }

    private void RunDecoder()
    {
        Logger?.LogInfo("Decoder thread is started.");

        while (State != PlaybackState.Idle)
        {
            while (IsSeeking)
            {
                if (State == PlaybackState.Idle)
                {
                    break;
                }

                Queue.Clear();
                Thread.Sleep(10);
            }

            var result = CurrentDecoder.DecodeNextFrame();

            if (result.IsEOF)
            {
                IsEOF = true;
                EngineThread.EnsureThreadDone(() => IsSeeking);

                if (IsSeeking)
                {
                    IsEOF = false;
                    Queue.Clear();

                    continue;
                }

                break;
            }

            if (!result.IsSucceeded)
            {
                if (HandleDecoderError(result))
                {
                    continue;
                }

                IsEOF = true; // ends the engine thread
                break;
            }

            while (Queue.Count >= MaxQueueSize)
            {
                if (State == PlaybackState.Idle)
                {
                    break;
                }

                Thread.Sleep(100);
            }

            Queue.Enqueue(result.Frame);
        }

        Logger?.LogInfo("Decoder thread is completed.");
    }

    private void RunEngine()
    {
        Logger?.LogInfo("Engine thread is started.");

        while (State != PlaybackState.Idle)
        {
            if (State == PlaybackState.Paused || IsSeeking)
            {
                Thread.Sleep(10);
                continue;
            }

            if (Queue.Count < MinQueueSize && !IsEOF)
            {
                SetAndRaiseStateChanged(PlaybackState.Buffering);
                Thread.Sleep(10);
                continue;
            }

            if (!Queue.TryDequeue(out var frame))
            {
                if (IsEOF)
                {
                    break;
                }

                Thread.Sleep(10);
                continue;
            }

            var samples = MemoryMarshal.Cast<byte, float>(frame.Data);
            ProcessSampleProcessors(samples);

            SetAndRaiseStateChanged(PlaybackState.Playing);
            Engine.Send(samples);

            SetAndRaisePositionChanged(TimeSpan.FromMilliseconds(frame.PresentationTime));
        }

        // Don't calls Seek(), the Play() method will do the job! The Seek() method will sets IsSeeking to true.
        // This can be an endless cycle since the decoder thread will spins and wait the engine thread
        // to complete, and break the spin when IsSeeking value is true.
        SetAndRaisePositionChanged(TimeSpan.Zero);

        // Just fire and forget, and it should be non-blocking event.
        Task.Run(() => SetAndRaiseStateChanged(PlaybackState.Idle));

        Logger?.LogInfo("Engine thread is completed.");
    }

    private void EnsureThreadsDone()
    {
        EngineThread?.EnsureThreadDone();
        DecoderThread?.EnsureThreadDone();

        EngineThread = null;
        DecoderThread = null;
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        State = PlaybackState.Idle;
        EnsureThreadsDone();

        Engine.Dispose();
        CurrentDecoder?.Dispose();
        Queue.Clear();

        GC.SuppressFinalize(this);

        _disposed = true;
    }
}
