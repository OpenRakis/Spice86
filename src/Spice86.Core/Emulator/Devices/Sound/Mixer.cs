// SPDX-License-Identifier: GPL-2.0-or-later
// Mixer implementation mirrored from DOSBox Staging
// Reference: src/audio/mixer.cpp

namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Libs.Sound.Common;
using Spice86.Shared.Interfaces;
using System.Collections.Concurrent;
using System.Threading;

/// <summary>
/// Central audio mixer that runs in its own thread and produces final mixed output.
/// Mirrors DOSBox Staging's mixer.cpp architecture.
/// </summary>
public sealed class Mixer : IDisposable {
    private const int DefaultSampleRateHz = 48000;
    private const int DefaultBlocksize = 1024;
    private const int MaxPrebufferMs = 100;
    
    // This shows up nicely as 50% and -6.00 dB in the MIXER command's output
    private const float Minus6db = 0.501f;

    private readonly ILoggerService _loggerService;
    private readonly AudioPlayerFactory _audioPlayerFactory;
    private readonly AudioPlayer _audioPlayer;
    
    // Channels registry - matches DOSBox mixer.channels
    private readonly ConcurrentDictionary<string, MixerChannel> _channels = new();
    
    // Mixer thread and synchronization
    private readonly Thread _mixerThread;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Lock _mixerLock = new();
    
    // Atomic state
    private volatile bool _threadShouldQuit;
    private volatile int _sampleRateHz = DefaultSampleRateHz;
    private volatile int _blocksize = DefaultBlocksize;
   
    // Master volume (atomic via Interlocked operations)
    private AudioFrame _masterGain = new(Minus6db, Minus6db);
    
    // Output buffers - matches DOSBox mixer output_buffer
    private readonly List<AudioFrame> _outputBuffer = new();
    private readonly List<AudioFrame> _reverbAuxBuffer = new();
    private readonly List<AudioFrame> _chorusAuxBuffer = new();
    
    // Final output queue to PortAudio thread
    private readonly ConcurrentQueue<AudioFrame> _finalOutputQueue = new();
    private readonly SemaphoreSlim _outputAvailable = new(0, 1);
    
    private bool _disposed;

    public Mixer(ILoggerService loggerService, AudioEngine audioEngine) {
        _loggerService = loggerService ?? throw new ArgumentNullException(nameof(loggerService));
        _audioPlayerFactory = new AudioPlayerFactory(_loggerService, audioEngine);
        
        // Create the audio player with our sample rate and blocksize
        _audioPlayer = _audioPlayerFactory.CreatePlayer(_sampleRateHz, _blocksize);
        
        // Start mixer thread
        _mixerThread = new Thread(MixerThreadLoop) {
            Name = "Spice86-Mixer",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        
        _mixerThread.Start();
        
        _loggerService.Information("MIXER: Initialized stereo {SampleRate} Hz audio with {BlockSize} sample frame buffer",
            _sampleRateHz, _blocksize);
    }

    /// <summary>
    /// Gets the current mixer sample rate.
    /// </summary>
    public int SampleRateHz => _sampleRateHz;

    /// <summary>
    /// Gets the current blocksize.
    /// </summary>
    public int Blocksize => _blocksize;

    /// <summary>
    /// Gets or sets the master volume gain.
    /// </summary>
    public AudioFrame MasterGain {
        get {
            lock (_mixerLock) {
                return _masterGain;
            }
        }
        set {
            lock (_mixerLock) {
                _masterGain = value;
            }
        }
    }

    /// <summary>
    /// Adds a channel to the mixer. Mirrors MIXER_AddChannel from DOSBox.
    /// </summary>
    public MixerChannel AddChannel(
        Action<int> handler,
        int sampleRateHz,
        string name,
        HashSet<ChannelFeature> features) {
        
        if (sampleRateHz == 0) {
            sampleRateHz = _sampleRateHz;
        }

        MixerChannel channel = new(handler, name, features, _loggerService);
        channel.SetSampleRate(sampleRateHz);
        channel.SetAppVolume(new AudioFrame(1.0f, 1.0f));
        channel.SetUserVolume(new AudioFrame(1.0f, 1.0f));
        
        int channelRate = channel.GetSampleRate();
        if (channelRate == _sampleRateHz) {
            _loggerService.Information("{ChannelName}: Operating at {Rate} Hz without resampling",
                name, channelRate);
        } else {
            _loggerService.Information("{ChannelName}: Operating at {Rate} Hz and {Direction} to the output rate",
                name, channelRate,
                channelRate > _sampleRateHz ? "downsampling" : "upsampling");
        }

        // Add to channels registry
        if (!_channels.TryAdd(name, channel)) {
            // Replace existing
            _channels[name] = channel;
        }

        // Set default state
        channel.Enable(false);
        channel.SetChannelMap(new StereoLine { Left = LineIndex.Left, Right = LineIndex.Right });

        return channel;
    }

    /// <summary>
    /// Finds a channel by name. Mirrors MIXER_FindChannel from DOSBox.
    /// </summary>
    public MixerChannel? FindChannel(string name) {
        _channels.TryGetValue(name, out MixerChannel? channel);
        return channel;
    }

    /// <summary>
    /// Removes a channel from the mixer.
    /// </summary>
    public void DeregisterChannel(string name) {
        if (_channels.TryRemove(name, out MixerChannel? channel)) {
            channel.Enable(false);
            _loggerService.Debug("MIXER: Deregistered channel {ChannelName}", name);
        }
    }

    /// <summary>
    /// Main mixer thread loop - mirrors mixer_thread_loop from DOSBox.
    /// </summary>
    private void MixerThreadLoop() {
        _loggerService.Debug("MIXER: Mixer thread started");
        
        CancellationToken token = _cancellationTokenSource.Token;

        try {
            while (!_threadShouldQuit && !token.IsCancellationRequested) {
                // Mix one blocksize worth of frames
                int framesRequested = _blocksize;
                
                lock (_mixerLock) {
                    MixSamples(framesRequested);
                }

                // Enqueue output to PortAudio consumer
                foreach (AudioFrame frame in _outputBuffer) {
                    _finalOutputQueue.Enqueue(frame);
                }
                
                _outputAvailable.Release(_outputBuffer.Count);

                // Consume from queue to audio player (non-blocking)
                ConsumeOutputQueue();
                
                // Small sleep to avoid busy-wait (matches DOSBox sleep logic)
                Thread.Sleep(1);
            }
        } catch (Exception ex) {
            _loggerService.Error(ex, "MIXER: Mixer thread encountered an error");
        } finally {
            _loggerService.Debug("MIXER: Mixer thread stopped");
        }
    }

    /// <summary>
    /// Mix samples from all channels - mirrors mix_samples from DOSBox.
    /// </summary>
    private void MixSamples(int framesRequested) {
        // Clear output buffers
        _outputBuffer.Clear();
        _outputBuffer.Capacity = Math.Max(_outputBuffer.Capacity, framesRequested);
        
        _reverbAuxBuffer.Clear();
        _chorusAuxBuffer.Clear();

        // Initialize with silence
        for (int i = 0; i < framesRequested; i++) {
            _outputBuffer.Add(new AudioFrame(0.0f, 0.0f));
            _reverbAuxBuffer.Add(new AudioFrame(0.0f, 0.0f));
            _chorusAuxBuffer.Add(new AudioFrame(0.0f, 0.0f));
        }

        // Mix all enabled channels
        foreach (MixerChannel channel in _channels.Values) {
            if (!channel.IsEnabled) {
                continue;
            }

            // Request frames from the channel
            channel.Mix(framesRequested);

            // Accumulate channel output into master mix
            int numFrames = Math.Min(framesRequested, channel.AudioFrames.Count);
            for (int i = 0; i < numFrames; i++) {
                AudioFrame channelFrame = channel.AudioFrames[i];
                
                // Add to master output
                _outputBuffer[i] = _outputBuffer[i].Add(channelFrame);
                
                // TODO: Reverb and chorus sends
            }

            // Remove consumed frames from channel
            if (numFrames > 0) {
                channel.AudioFrames.RemoveRange(0, numFrames);
            }
        }

        // Apply master gain
        AudioFrame masterGainSnapshot = _masterGain;
        for (int i = 0; i < _outputBuffer.Count; i++) {
            _outputBuffer[i] = _outputBuffer[i].Multiply(masterGainSnapshot);
        }

        // TODO: Master compressor, reverb, chorus processing
    }

    /// <summary>
    /// Consumes frames from the output queue and writes them to PortAudio.
    /// </summary>
    private void ConsumeOutputQueue() {
        const int MaxFramesToWrite = 2048;
        Span<float> interleavedBuffer = stackalloc float[MaxFramesToWrite * 2];
        
        int frameCount = 0;
        while (frameCount < MaxFramesToWrite && _finalOutputQueue.TryDequeue(out AudioFrame frame)) {
            int offset = frameCount * 2;
            interleavedBuffer[offset] = frame.Left;
            interleavedBuffer[offset + 1] = frame.Right;
            frameCount++;
        }

        if (frameCount > 0) {
            // Normalize to -1.0 to +1.0 range for audio output
            const float normalizeFactor = 1.0f / 32768.0f;
            for (int i = 0; i < frameCount * 2; i++) {
                interleavedBuffer[i] *= normalizeFactor;
            }
            
            _audioPlayer.WriteData(interleavedBuffer.Slice(0, frameCount * 2));
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _loggerService.Debug("MIXER: Disposing mixer");

        _threadShouldQuit = true;
        _cancellationTokenSource.Cancel();
        
        if (_mixerThread.IsAlive) {
            _mixerThread.Join(1000);
        }

        _cancellationTokenSource.Dispose();
        _outputAvailable.Dispose();
        _audioPlayer.Dispose();

        _disposed = true;
    }
}
