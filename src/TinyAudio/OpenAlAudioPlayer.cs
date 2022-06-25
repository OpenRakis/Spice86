namespace TinyAudio;

using Silk.NET.OpenAL;

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;

public sealed unsafe class OpenAlAudioPlayer : AudioPlayer {
    private const int MaxAlBuffers = 2;
    private static TimeSpan _bufferLength;
    private readonly AL? _al = null;
    private readonly ALContext? _alContext = null;
    private readonly Device* _device = null;
    private readonly Context* _context = null;
    private readonly uint _source = 0;
    private bool _disposed = false;
    private readonly BufferFormat _openAlBufferFormat;
    private readonly Dictionary<uint, uint> _alBuffers = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private OpenAlAudioPlayer(AudioFormat format) : base(format) {
        try {
            _al = AL.GetApi(true);
            _al.GetError();
            _alContext = ALContext.GetApi(true);
        } catch {
            try {
                _al = AL.GetApi(false);
                _al.GetError();
                _alContext = ALContext.GetApi(false);
            } catch {
                return;
            }
        }
        _device = _alContext.OpenDevice(null);

        bool available = _device != null;

        if (available) {
            _context = _alContext.CreateContext(_device, null);
            _alContext.MakeContextCurrent(_context);
            if (_al?.GetError() != AudioError.NoError) {
                if (_context != null) {
                    _alContext.DestroyContext(_context);
                }

                _alContext.CloseDevice(_device);
                _al?.Dispose();
                _alContext.Dispose();
                _disposed = true;
                return;
            }

            if (available) {
                _source = _al.GenSource();
                _al?.SetSourceProperty(_source, SourceBoolean.Looping, false);
                _al?.SetSourceProperty(_source, SourceFloat.ReferenceDistance, 0);
                _al?.SetSourceProperty(_source, SourceFloat.Pitch, 1.0f);
                _al?.SetSourceProperty(_source, SourceFloat.Pitch, 1.0f);
                _al?.SetSourceProperty(_source, SourceFloat.Gain, 1.0f);
                _al?.SetSourceProperty(_source, SourceInteger.ByteOffset, 0);
                _al?.SetSourceProperty(_source, SourceInteger.SourceType, (int)SourceType.Streaming);
                _al?.SetSourceProperty(_source, SourceBoolean.SourceRelative, false);
                _al?.GetError();
                for (int i = 0; i < MaxAlBuffers; i++) {
                    if (_al is not null) {
                        var buffer = _al.GenBuffer();
                        ThrowIfAlError();
                        _alBuffers.Add(buffer, buffer);
                    }
                }
                _al?.SourceQueueBuffers(_source, _alBuffers.Keys.ToArray());
                ThrowIfAlError();
            }
            _openAlBufferFormat = BufferFormat.Stereo16;
        }
    }

    public void Reset() {
        if (_source == 0) {
            throw new NotSupportedException("Reset was called without a valid source.");
        }
        _al?.SetSourceProperty(_source, SourceInteger.Buffer, 0u);
    }

    protected override void Start(bool useCallback) {
        if (_source == 0) {
            throw new NotSupportedException("Start was called without a valid source.");
        }
        Play();
    }

    private void Play() {
        int state = 0;
        _al?.GetSourceProperty(_source, GetSourceInteger.SourceState, out state);
        var currentState = (SourceState)state;
        if (currentState != SourceState.Playing) {
            _al?.SourcePlay(_source);
            //System.Diagnostics.Debug.WriteLine("Source was not playing...");
        }
    }

    protected override void Stop() {
        if (_source == 0) {
            throw new NotSupportedException("Stop was called without a valid source.");
        }
        _al?.SourceStop(_source);
    }

    protected override int WriteDataInternal(ReadOnlySpan<byte> data) {
        if (_al is null) {
            throw new NullReferenceException(nameof(_al));
        }
        Play();
        int processed = 0;
        _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out processed);
        while (processed >= 1) {
            uint buffer = 0;
            _al.SourceUnqueueBuffers(_source, 1, &buffer);
            ThrowIfAlError();
            if (data.Length > 0 && buffer > 0) {
                _al.BufferData(buffer, _openAlBufferFormat, data.ToArray(), Format.SampleRate);
                ThrowIfAlError();
                _al.SourceQueueBuffers(_source, new uint[] { buffer });
                ThrowIfAlError();
            }
            _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out processed);
        }
        Play();
        return data.Length;
    }

    private void ThrowIfAlError() {
        AudioError? error = _al?.GetError();
        if (error != AudioError.NoError) {
            throw new InvalidDataException(error.ToString());
        }
    }

    public static OpenAlAudioPlayer Create(TimeSpan bufferLength, bool useCallback = false) {
        _bufferLength = bufferLength;
        return new OpenAlAudioPlayer(new AudioFormat(Channels: 2, SampleFormat: SampleFormat.SignedPcm16,
            SampleRate: 44100));
    }

    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _cancellationTokenSource.Cancel();
                Stop();
                foreach (KeyValuePair<uint, uint> bufferIndex in _alBuffers) {
                    _al?.DeleteBuffer(bufferIndex.Key);
                    AudioError? error = _al?.GetError();
                    if (error != AudioError.NoError) {
                        Console.WriteLine($"OpenAL error while deleting buffer: {error}");
                    }
                }
                _alBuffers.Clear();
                if (_device is not null) {
                    _al?.DeleteSource(_source);
                    _alContext?.DestroyContext(_context);
                    _alContext?.CloseDevice(_device);
                    _al?.Dispose();
                    _alContext?.Dispose();
                }
            }
            _disposed = true;
        }
    }

}