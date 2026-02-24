using Spice86.Audio.Common;
using Spice86.Audio.Filters.IirFilters.Filters.Butterworth;

namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Audio.Backend.Audio;
using Spice86.Audio.Filters;
using Spice86.Core.Emulator.VM;

using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

using AudioFrame = AudioFrame;
using HighPassFilter = HighPass;

/// <summary>
/// Central audio mixer that runs in its own thread and produces final mixed output.
/// </summary>
public sealed class SoftwareMixer : IDisposable {
    private const int DefaultSampleRateHz = 48000;
    private const int DefaultBlocksize = 1024;
    private const int DefaultPrebufferMs = 25;

    // This shows up nicely as 50% and -6.00 dB in the MIXER command's output
    private const float Minus6db = 0.501f;

    private readonly AudioPlayerFactory _audioPlayerFactory;
    private readonly AudioPlayer _audioPlayer;
    private readonly IPauseHandler _pauseHandler;

    // Channels registry - matches DOSBox mixer.channels
    private readonly Dictionary<string, SoundChannel> _channels = new();
    private readonly Dictionary<string, SoundChannelSettings> _channelSettingsCache = new();

    // Queue notifiers for devices that run on the main thread.
    // The mixer thread can be waiting on these queues. We need to stop them
    // before acquiring the mutex lock to avoid a deadlock.
    private readonly List<IMixerQueueNotifier> _queueNotifiers = new();

    // Mixer thread that produces audio and sends to the audio backend
    private readonly Thread _mixerThread;
    private readonly Lock _mixerLock = new();

    private volatile bool _threadShouldQuit;
    private readonly int _sampleRateHz = DefaultSampleRateHz;
    private readonly int _blocksize = DefaultBlocksize;
    private readonly int _prebufferMs = DefaultPrebufferMs;

    private AudioFrame _masterVolume = new(Minus6db, Minus6db);

    // Controls whether audio is playing, muted, or disabled
    private MixerState _state = MixerState.On;

    private readonly AudioFrameBuffer _outputBuffer = new(0);
    private readonly AudioFrameBuffer _reverbAuxBuffer = new(0);
    private readonly AudioFrameBuffer _chorusAuxBuffer = new(0);

    private bool _doCompressor = false;
    private readonly Compressor _compressor = new();

    // Reverb state - MVerb professional algorithmic reverb
    private readonly bool _doReverb;
    private readonly MVerb _mverb = new();
    private readonly float _reverbSynthSendLevel = 0.0f;
    private readonly float _reverbDigitalSendLevel = 0.0f;

    private float _reverbLeftIn;
    private float _reverbRightIn;

    private readonly bool _doChorus = false;
    private readonly ChorusEngine _chorusEngine;
    private readonly float _chorusSynthSendLevel = 0.0f;
    private readonly float _chorusDigitalSendLevel = 0.0f;

    // Crossfeed state - stereo mixing for headphone spatialization
    private readonly bool _doCrossfeed = false;
    private readonly float _crossfeedGlobalStrength = 0.0f; // Varies by preset: Light=0.20f, Normal=0.40f, Strong=0.60f

    // Used on reverb input and master output
    private readonly HighPassFilter[] _reverbHighPassFilter;
    private readonly HighPassFilter[] _masterHighPassFilter;
    private const int HighPassFilterOrder = 2; // 2nd-order Butterworth
    private const float MasterHighPassCutoffHz = 20.0f;

    private bool _disposed;

    /// <summary>
    /// Creates a new Mixer instance.
    /// </summary>
    /// <param name="audioEngine">Audio engine to use.</param>
    /// <param name="pauseHandler">Pause handler to mute audio when emulator is paused.</param>
    public SoftwareMixer(AudioEngine audioEngine, IPauseHandler pauseHandler) {
        _pauseHandler = pauseHandler ?? throw new ArgumentNullException(nameof(pauseHandler));
        _audioPlayerFactory = new AudioPlayerFactory(audioEngine);

        // Create the audio player with our sample rate and blocksize
        _audioPlayer = _audioPlayerFactory.CreatePlayer(_sampleRateHz, _blocksize, _prebufferMs);
        if (_audioPlayer.Format.SampleRate > 0) {
            _sampleRateHz = _audioPlayer.Format.SampleRate;
        }
        if (_audioPlayer.BufferFrames > 0) {
            _blocksize = _audioPlayer.BufferFrames;
        }

        // Initialize high-pass filters (2 channels - left and right)
        _reverbHighPassFilter = new HighPassFilter[2];
        _masterHighPassFilter = new HighPassFilter[2];
        const float DefaultReverbHighPassHz = 200.0f;

        for (int i = 0; i < 2; i++) {
            _reverbHighPassFilter[i] = new HighPassFilter(HighPassFilterOrder);
            _reverbHighPassFilter[i].Setup(HighPassFilterOrder, _sampleRateHz, DefaultReverbHighPassHz);

            _masterHighPassFilter[i] = new HighPassFilter(HighPassFilterOrder);
            _masterHighPassFilter[i].Setup(HighPassFilterOrder, _sampleRateHz, MasterHighPassCutoffHz);
        }

        // Initialize MVerb with default parameters
        _mverb.SetSampleRate(_sampleRateHz);

        // Initialize ChorusEngine with default sample rate
        _chorusEngine = new ChorusEngine(_sampleRateHz);

        _chorusEngine.SetEnablesChorus(isChorus1Enabled: true, isChorus2Enabled: false);

        // Initialize compressor with default parameters
        InitCompressor(compressorEnabled: true);

        _audioPlayer.Start();

        _mixerThread = new Thread(MixerThreadLoop) {
            Name = nameof(SoftwareMixer),
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _mixerThread.Start();

        _pauseHandler.Pausing += OnEmulatorPausing;
        _pauseHandler.Resumed += OnEmulatorResumed;
    }

    private void OnEmulatorPausing() {
        Mute();
    }

    private void OnEmulatorResumed() {
        Unmute();
    }

    /// <summary>
    /// Registers a queue notifier for a device that produces audio on the main thread.
    /// The notifier will be called before the mixer mutex is acquired and after it is released.
    /// </summary>
    /// <param name="notifier">The device notifier to register.</param>
    public void RegisterQueueNotifier(IMixerQueueNotifier notifier) {
        _queueNotifiers.Add(notifier);
    }

    /// <summary>
    /// Locks the mixer thread to prevent mixing during critical operations.
    /// The queues listed here are for audio devices that run on the main thread.
    /// The mixer thread can be waiting on the main thread to produce audio in these
    /// queues. We need to stop them before acquiring a mutex lock to avoid a
    /// deadlock. These are called infrequently when global mixer state is changed
    /// (mostly on device init/destroy and in the MIXER command line program).
    /// Individual channels also have a mutex which can be safely acquired without
    /// stopping these queues.
    /// </summary>
    public void LockMixerThread() {
        foreach (IMixerQueueNotifier notifier in _queueNotifiers) {
            notifier.NotifyLockMixer();
        }
        _mixerLock.Enter();
    }

    /// <summary>
    /// Unlocks the mixer thread after critical operations complete.
    /// Restarts the device queues to resume normal operation.
    /// </summary>
    public void UnlockMixerThread() {
        foreach (IMixerQueueNotifier notifier in _queueNotifiers) {
            notifier.NotifyUnlockMixer();
        }
        _mixerLock.Exit();
    }

    /// <summary>
    /// Mutes audio output while keeping the audio device active.
    /// </summary>
    public void Mute() {
        LockMixerThread();
        try {
            if (_state == MixerState.On) {
                // Clear out any audio in the queue to avoid a stutter on un-mute
                _audioPlayer.ClearQueuedData();
                _state = MixerState.Muted;
            }
        } finally {
            UnlockMixerThread();
        }
    }

    /// <summary>
    /// Unmutes audio output, resuming playback.
    /// </summary>
    public void Unmute() {
        LockMixerThread();
        try {
            if (_state == MixerState.Muted) {
                _state = MixerState.On;
            }
        } finally {
            UnlockMixerThread();
        }
    }

    /// <summary>
    /// Applies global crossfeed settings to all channels.
    /// </summary>
    private void SetGlobalCrossfeed() {
        // Apply preset-specific crossfeed strength to OPL and CMS channels only
        float globalStrength = _doCrossfeed ? _crossfeedGlobalStrength : 0.0f;
        foreach (SoundChannel channel in _channels.Values) {
            string name = channel.Name;
            bool applyCrossfeed = name is "Opl" or "Cms";
            if (applyCrossfeed && channel.HasFeature(ChannelFeature.Stereo)) {
                channel.CrossfeedStrength = globalStrength;
            } else {
                channel.CrossfeedStrength = 0.0f;
            }
        }
    }

    private void SetGlobalReverb() {
        foreach (SoundChannel channel in _channels.Values) {
            if (!_doReverb || !channel.HasFeature(ChannelFeature.ReverbSend)) {
                channel.ReverbLevel = 0.0f;
            } else if (channel.HasFeature(ChannelFeature.Synthesizer)) {
                channel.ReverbLevel = _reverbSynthSendLevel;
            } else if (channel.HasFeature(ChannelFeature.DigitalAudio)) {
                channel.ReverbLevel = _reverbDigitalSendLevel;
            }
        }
    }

    private void SetGlobalChorus() {
        foreach (SoundChannel channel in _channels.Values) {
            if (!_doChorus || !channel.HasFeature(ChannelFeature.ChorusSend)) {
                channel.ChorusLevel = 0.0f;
            } else if (channel.HasFeature(ChannelFeature.Synthesizer)) {
                channel.ChorusLevel = _chorusSynthSendLevel;
            } else if (channel.HasFeature(ChannelFeature.DigitalAudio)) {
                channel.ChorusLevel = _chorusDigitalSendLevel;
            }
        }
    }

    /// <summary>
    /// Adds a new mixer channel with the specified configuration and registers it with the mixer.
    /// </summary>
    /// <param name="handler">A callback that is invoked with the channel's sample rate whenever it changes.</param>
    /// <param name="sampleRateHz">The desired sample rate for the channel, in hertz.</param>
    /// <param name="name">The unique name used to identify the channel within the mixer.</param>
    /// <param name="features">A set of features that define the channel's capabilities and behavior.</param>
    /// <returns>A MixerChannel instance configured with the specified settings and registered in the mixer.</returns>
    public SoundChannel AddChannel(
        Action<int> handler,
        int sampleRateHz,
        string name,
        HashSet<ChannelFeature> features) {

        if (sampleRateHz == 0) {
            sampleRateHz = _sampleRateHz;
        }

        SoundChannel channel = new(handler, name, features);
        channel.SetMixerSampleRate(_sampleRateHz); // Tell channel about mixer rate
        channel.        SampleRate = sampleRateHz;
        channel.AppVolume = new AudioFrame(1.0f, 1.0f);
        channel.UserVolume = new AudioFrame(1.0f, 1.0f);

        // Add to channels registry
        if (!_channels.TryAdd(name, channel)) {
            // Replace existing
            _channels[name] = channel;
        }

        if (_channelSettingsCache.TryGetValue(name, out SoundChannelSettings cachedSettings)) {
            channel.SetSettings(cachedSettings);
            ApplyCachedEffectSettings(channel, cachedSettings);
        } else {
            // Set default state
            channel.Enable(false);
            channel.UserVolume = new AudioFrame(1.0f, 1.0f);
            channel.SetChannelMap(new StereoLine { Left = LineIndex.Left, Right = LineIndex.Right });
            SetGlobalCrossfeed();
            SetGlobalReverb();
            SetGlobalChorus();
        }

        return channel;
    }

    /// <summary>
    /// Finds a channel by name.
    /// </summary>
    public SoundChannel? FindChannel(string name) {
        _channels.TryGetValue(name, out SoundChannel? channel);
        return channel;
    }

    private void ApplyCachedEffectSettings(SoundChannel channel, SoundChannelSettings settings) {
        if (_doCrossfeed) {
            channel.CrossfeedStrength = settings.CrossfeedStrength;
        } else {
            channel.CrossfeedStrength = 0.0f;
        }

        if (_doReverb) {
            channel.ReverbLevel = settings.ReverbLevel;
        } else {
            channel.ReverbLevel = 0.0f;
        }

        if (_doChorus) {
            channel.ChorusLevel = settings.ChorusLevel;
        } else {
            channel.ChorusLevel = 0.0f;
        }
    }

    /// <summary>
    /// Gets all registered mixer channels.
    /// </summary>
    public IEnumerable<SoundChannel> AllChannels => _channels.Values;

    private void MixerThreadLoop() {
        while (!_threadShouldQuit) {
            lock (_mixerLock) {
                // "Underflow" is not a concern since moving to a threaded
                // mixer. If the CPU is running slower than real-time, the audio
                // drivers will naturally slow down the audio. Therefore, we can
                // always request at least a blocksize worth of audio.
                int framesRequested = _blocksize;

                MixSamples(framesRequested);
            }

            double expectedTimeMs = (double)_blocksize / _sampleRateHz * 1000.0;

            if (_state == MixerState.NoSound) {
                // SDL callback is not running. Mixed sound gets
                // discarded. Sleep for the expected duration to
                // simulate the time it would have taken to playback the
                // audio.
                Thread.Sleep(TimeSpan.FromMilliseconds(expectedTimeMs));
                continue;
            } else if (_state == MixerState.Muted) {
                // SDL callback remains active. Enqueue silence.
                _outputBuffer.Resize(_blocksize);
                _outputBuffer.AsSpan().Clear();

                Span<float> silenceInterleaved = MemoryMarshal.Cast<AudioFrame, float>(_outputBuffer.AsSpan());
                _audioPlayer.WriteData(silenceInterleaved);
                continue;
            }

            _audioPlayer.WriteData(
                MemoryMarshal.Cast<AudioFrame, float>(_outputBuffer.AsSpan()));
        }
    }

    private void MixSamples(int framesRequested) {
        _outputBuffer.Resize(framesRequested);
        _reverbAuxBuffer.Resize(framesRequested);
        _chorusAuxBuffer.Resize(framesRequested);
        _outputBuffer.AsSpan().Clear();
        _reverbAuxBuffer.AsSpan().Clear();
        _chorusAuxBuffer.AsSpan().Clear();

        Span<AudioFrame> output = _outputBuffer.AsSpan();
        Span<AudioFrame> reverbAux = _reverbAuxBuffer.AsSpan();
        Span<AudioFrame> chorusAux = _chorusAuxBuffer.AsSpan();

        // Render all channels and accumulate results in the master mixbuffer
        foreach (SoundChannel channel in _channels.Values) {
            channel.Mix(framesRequested);
            Span<AudioFrame> channelFrames = channel.AudioFrames.AsSpan();
            int numFrames = Math.Min(output.Length, channelFrames.Length);
            for (int i = 0; i < numFrames; i++) {
                if (channel.DoSleep) {
                    output[i] += channel.MaybeFadeOrListen(channelFrames[i]);
                } else {
                    output[i] += channelFrames[i];
                }
                if (_doReverb && channel.DoReverbSend) {
                    reverbAux[i] += channelFrames[i] * channel.ReverbSendGain;
                }
                if (_doChorus && channel.DoChorusSend) {
                    chorusAux[i] += channelFrames[i] * channel.ChorusSendGain;
                }
            }
            channel.AudioFrames.RemoveRange(0, numFrames);
            if (channel.DoSleep) {
                channel.MaybeSleep();
            }
        }
        if (_doReverb) {
            ApplyReverb();
        }
        if (_doChorus) {
            ApplyChorus();
        }

        // Apply high-pass filter to the master output
        Span<AudioFrame> masterOutput = _outputBuffer.AsSpan();
        for (int i = 0; i < masterOutput.Length; i++) {
            ref AudioFrame frame = ref masterOutput[i];
            frame = new AudioFrame(
                _masterHighPassFilter[0].Filter(frame.Left),
                _masterHighPassFilter[1].Filter(frame.Right)
            );
        }

        // Apply master gain
        AudioFrame gain = _masterVolume;
        for (int i = 0; i < masterOutput.Length; i++) {
            masterOutput[i] *= gain;
        }

        if (_doCompressor) {
            // Apply compressor to the master output as the very last step
            for (int i = 0; i < masterOutput.Length; i++) {
                masterOutput[i] = _compressor.Process(masterOutput[i]);
            }
        }

        // Normalize the final output before sending to SDL
        for (int i = 0; i < masterOutput.Length; i++) {
            ref AudioFrame frame = ref masterOutput[i];
            frame = new AudioFrame(
                NormalizeSample(frame.Left),
                NormalizeSample(frame.Right)
            );
        }
    }

    private static float NormalizeSample(float sample) {
        return sample / 32768.0f;
    }

    private void InitCompressor(bool compressorEnabled) {
        _doCompressor = compressorEnabled;
        LockMixerThread();
        const float ZeroDbfsSampleValue = 32767.0f;
        const float ThresholdDb = -6.0f;
        const float Ratio = 3.0f;
        const float AttackTimeMs = 0.01f;
        const float ReleaseTimeMs = 5000.0f;
        const float RmsWindowMs = 10.0f;

        _compressor.Configure(
            _sampleRateHz,
            ZeroDbfsSampleValue,
            ThresholdDb,
            Ratio,
            AttackTimeMs,
            ReleaseTimeMs,
            RmsWindowMs
        );
        UnlockMixerThread();
    }

    private void ApplyReverb() {
        // Apply reverb effect to the reverb aux buffer, then mix the
        // results to the master output.
        Span<AudioFrame> reverbAux = _reverbAuxBuffer.AsSpan();
        Span<AudioFrame> output = _outputBuffer.AsSpan();

        for (int i = 0; i < reverbAux.Length; i++) {
            // High-pass filter the reverb input
            AudioFrame inFrame = reverbAux[i];
            inFrame = new AudioFrame(
                _reverbHighPassFilter[0].Filter(inFrame.Left),
                _reverbHighPassFilter[1].Filter(inFrame.Right)
            );
            // MVerb operates on two non-interleaved sample streams
            _reverbLeftIn = inFrame.Left;
            _reverbRightIn = inFrame.Right;
            _mverb.Process(ref _reverbLeftIn, ref _reverbRightIn);
            output[i] += new AudioFrame(_reverbLeftIn, _reverbRightIn);
        }
    }

    private void ApplyChorus() {
        // Apply chorus effect to the chorus aux buffer, then mix to master output
        Span<AudioFrame> chorusAux = _chorusAuxBuffer.AsSpan();
        Span<AudioFrame> output = _outputBuffer.AsSpan();
        for (int i = 0; i < chorusAux.Length; i++) {
            float left = chorusAux[i].Left;
            float right = chorusAux[i].Right;
            // Process through TAL-Chorus engine (in-place modification)
            _chorusEngine.Process(ref left, ref right);
            // Add processed chorus to output buffer
            output[i] += new AudioFrame(left, right);
        }
    }

    private void CloseAudioDevice() {
        _threadShouldQuit = true;
        _audioPlayer.MuteOutput();
        if (_mixerThread.IsAlive) {
            _mixerThread.Join(TimeSpan.FromSeconds(2));
        }
        foreach (SoundChannel channel in _channels.Values) {
            channel.Enable(false);
        }
        _audioPlayer.Dispose();
    }

    /// <summary>
    /// Disposes of the mixer, stopping the audio thread and releasing resources.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _pauseHandler.Pausing -= OnEmulatorPausing;
        _pauseHandler.Resumed -= OnEmulatorResumed;
        CloseAudioDevice();
        _disposed = true;
    }

    /// <summary>
    /// Generic callback for audio devices that generate audio on the main thread.
    /// These devices produce audio on the main thread and consume on the mixer thread.
    /// This callback dispatches to the appropriate AddSamples method based on the sample type.
    /// </summary>
    /// <typeparam name="TDevice">The device type implementing IAudioQueueDevice.</typeparam>
    /// <typeparam name="TItem">The sample type (float or AudioFrame) in the device queue.</typeparam>
    /// <param name="framesRequested">Number of audio frames requested by the mixer.</param>
    /// <param name="device">The audio device (passed as 'this' from the device).</param>
    internal static void PullFromQueueCallback<TDevice, TItem>(int framesRequested, TDevice device)
        where TDevice : IAudioQueueDevice<TItem>
        where TItem : struct {
        // Size to 2x blocksize. The mixer callback will request 1x blocksize.
        // This provides a good size to avoid over-runs and stalls.
        int queueSize = (int)Math.Ceiling(device.Channel.FramesPerBlock * 2.0f);
        device.OutputQueue.Resize(queueSize);

        // Dequeue samples in bulk
        TItem[] toMix = new TItem[framesRequested];
        int framesReceived = device.OutputQueue.BulkDequeue(toMix, framesRequested);

        if (framesReceived > 0) {
            if (typeof(TItem) == typeof(float)) {
                float[] floatArray = toMix as float[] ?? [];
                // PcSpeaker produces mono float data
                device.Channel.AddSamplesGeneric(framesReceived, floatArray.AsSpan(), isStereo: false);
            } else if (typeof(TItem) == typeof(AudioFrame)) {
                AudioFrame[] frameArray = toMix as AudioFrame[] ?? [];
                device.Channel.AddAudioFrames(frameArray.AsSpan());
            }
        }

        // Fill any shortfall with silence
        if (framesReceived < framesRequested) {
            device.Channel.AddSilence();
        }
    }
}