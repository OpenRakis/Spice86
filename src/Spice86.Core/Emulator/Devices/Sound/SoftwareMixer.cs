namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Shared.Emulator.Audio;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

/// <summary>
/// Basic software mixer for sound channels.
/// </summary>
public sealed class SoftwareMixer : IDisposable, IDebuggableComponent {
    private readonly Dictionary<SoundChannel, AudioPlayer> _channels = new();
    private readonly AudioPlayerFactory _audioPlayerFactory;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftwareMixer"/> class.
    /// </summary>
    /// <param name="audioPlayerFactory">The factory for creating an audio player for each new sound channel.</param>
    public SoftwareMixer(AudioPlayerFactory audioPlayerFactory) {
        _audioPlayerFactory = audioPlayerFactory;
    }

    internal void Register(SoundChannel soundChannel) {
        _channels.Add(soundChannel, _audioPlayerFactory.CreatePlayer());
    }

    /// <summary>
    /// Gets the sound channels in a read-only dictionary.
    /// </summary>
    public IDictionary<SoundChannel, AudioPlayer> Channels => new ReadOnlyDictionary<SoundChannel, AudioPlayer>(_channels);

    internal void Render<T>(ref AudioFrame<T> frame, SoundChannel channel) where T : unmanaged {
        if (channel.Volume == 0 || channel.IsMuted) {
            _channels[channel].WriteSilence();
            return;
        }
        if (typeof(T) == typeof(float)) {
            ApplyStereoSeparation(ref Unsafe.As<AudioFrame<T>, AudioFrame<float>>(ref frame), channel);
            ApplyVolume(ref Unsafe.As<AudioFrame<T>, AudioFrame<float>>(ref frame), channel);
        }
        else if (typeof(T) == typeof(int)) {
            ApplyStereoSeparation(ref Unsafe.As<AudioFrame<T>, AudioFrame<int>>(ref frame), channel);
            ApplyVolume(ref Unsafe.As<AudioFrame<T>, AudioFrame<int>>(ref frame), channel);
        }
        else if (typeof(T) == typeof(short)) {
            ApplyStereoSeparation(ref Unsafe.As<AudioFrame<T>, AudioFrame<short>>(ref frame), channel);
            ApplyVolume(ref Unsafe.As<AudioFrame<T>, AudioFrame<short>>(ref frame), channel);
        }
        _channels[channel].WriteData(frame);
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                foreach (AudioPlayer audioPlayer in _channels.Values) {
                    audioPlayer.Dispose();
                }
                _channels.Clear();
            }
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        Dispose(true);
    }

    /// <inheritdoc/>
    public void Accept<T>(T emulatorDebugger) where T : IInternalDebugger {
        emulatorDebugger.Visit(this);
    }
    
    private static void ApplyVolume(ref AudioFrame<float> frame, SoundChannel channel) {
        float volumeFactor = channel.Volume / 100f;
        frame.Left *= volumeFactor;
        frame.Right *= volumeFactor;
    }

    private static void ApplyVolume(ref AudioFrame<int> frame, SoundChannel channel) {
        float volumeFactor = channel.Volume / 100f;
        frame.Left = (int)(frame.Left * volumeFactor);
        frame.Right = (int)(frame.Right * volumeFactor);
    }

    private static void ApplyVolume(ref AudioFrame<short> frame, SoundChannel channel) {
        float volumeFactor = channel.Volume / 100f;
        frame.Left = (short)(frame.Left * volumeFactor);
        frame.Right = (short)(frame.Right * volumeFactor);
    }

    private static void ApplyStereoSeparation(ref AudioFrame<float> frame, SoundChannel channel) {
        float separation = channel.StereoSeparation / 100f;
        frame.Left *= (1 - separation);
        frame.Right *= (1 + separation);
    }

    private static void ApplyStereoSeparation(ref AudioFrame<int> frame, SoundChannel channel) {
        float separation = channel.StereoSeparation / 100f;
        frame.Left = (int)(frame.Left * (1 - separation));
        frame.Right = (int)(frame.Right * (1 + separation));
    }

    private static void ApplyStereoSeparation(ref AudioFrame<short> frame, SoundChannel channel) {
        float separation = channel.StereoSeparation / 100f;
        frame.Left = (short)(frame.Left * (1 - separation));
        frame.Right = (short)(frame.Right * (1 + separation));
    }
}
