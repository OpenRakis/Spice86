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
    private static TimeSpan _bufferLength;

    private readonly AL? _al = null;
    private readonly ALContext? _alContext = null;
    private readonly Device* _device = null;
    private readonly Context* _context = null;
    private readonly uint _source = 0;
    private bool _disposed = false;
    private float _volume = 1.0f;
    private bool _enabled = true;
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
                _alContext = ALContext.GetApi(false);
            } catch {
                Available = false;
                return;
            }
        }
        _device = _alContext.OpenDevice(null);

        Available = _device != null;

        if (Available) {
            _context = _alContext.CreateContext(_device, null);
            _alContext.MakeContextCurrent(_context);
            if (_al?.GetError() != AudioError.NoError) {
                Available = false;
                if (_context != null) {
                    _alContext.DestroyContext(_context);
                }

                _alContext.CloseDevice(_device);
                _al?.Dispose();
                _alContext.Dispose();
                _disposed = true;
                return;
            }

            if (_al is not null) {
                _source = _al.GenSource();                
            }
            _openAlBufferFormat = BufferFormat.Stereo16;
        }
    }
    
    public bool Enabled {
        get => _enabled;
        set {
            if (_enabled == value) {
                return;
            }

            if (!value && Available && Streaming) {
                Stop();
                Reset();
            }

            _enabled = value;
        }
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    private bool Streaming { get; set; } = false;

    public float Volume {
        get => _volume;
        set {
            value = Math.Max(0.0f, Math.Min(value, 1.0f));

            if (Math.Abs(_volume - value) < 0.00001f) {
                return;
            }

            _volume = value;

            if (Available) {
                _al?.SetSourceProperty(_source, SourceFloat.Gain, _volume);
            }
        }
    }

    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _cancellationTokenSource.Cancel();
                foreach (var bufferIndex in _alBuffers) {
                    _al?.DeleteBuffer(bufferIndex.Key);
                    AudioError? error = _al?.GetError();
                    if (error != AudioError.NoError) {
                        Console.WriteLine($"OpenAL error while deleting buffer: {error}");
                    }
                }
                _alBuffers.Clear();
                if (Available) {
                    _al?.DeleteSource(_source);
                    _alContext?.DestroyContext(_context);
                    _alContext?.CloseDevice(_device);
                    _al?.Dispose();
                    _alContext?.Dispose();
                }

                Streaming = false;
                Enabled = false;
                Available = false;
            }

            _disposed = true;
        }
    }

    public void Reset() {
        if (!Available) {
            return;
        }

        _al?.SetSourceProperty(_source, SourceInteger.Buffer, 0u);
    }

    private bool Available { get; set; }

    protected override void Start(bool useCallback) {
        if (!Available || !Enabled || Streaming) {
            return;
        }
        if (_source == 0) {
            throw new NotSupportedException("Start was called without a valid source.");
        }
        Streaming = true;
        _al?.SetSourceProperty(_source, SourceBoolean.Looping, false);
        _al?.SetSourceProperty(_source, SourceFloat.Gain, 1.0f);
        _al?.SetSourceProperty(_source, SourceInteger.ByteOffset, 0);
        _al?.SourcePlay(_source);
    }

    protected override void Stop() {
        if (!Available || !Enabled || !Streaming) {
            return;
        }
        if (_source == 0) {
            throw new NotSupportedException("Stop was called without a valid source.");
        }
        Streaming = false;
        _al?.SourceStop(_source);
    }

    protected override int WriteDataInternal(ReadOnlySpan<byte> data) {
        if (_al is null) {
            throw new NullReferenceException(nameof(_al));
        }
        ThrowIfAlError();
        int processed = 0;
        _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out processed);
        uint buffer = 0;
        System.Diagnostics.Debug.WriteLine(processed);
        if (processed == 0) {
            buffer = _al.GenBuffer();
            _alBuffers.Add(buffer, buffer);
        }
        else if (processed >= 1) {
            _al.SourceUnqueueBuffers(_source, 1, &buffer);            
        }
        _al.BufferData(buffer, _openAlBufferFormat, data.ToArray(), Format.SampleRate);
        ThrowIfAlError();
        _al.SourceQueueBuffers(_source,1, &buffer);
        ThrowIfAlError();
        _al.SourcePlay(_source);
        ThrowIfAlError();
        return data.Length;
    }

    private void ThrowIfAlError()
    {
        AudioError? error = _al?.GetError();
        if (error != AudioError.NoError)
        {
            throw new InvalidDataException(error.ToString());
        }
    }

    public static OpenAlAudioPlayer Create(TimeSpan bufferLength, bool useCallback = false) {
        _bufferLength = bufferLength;
        return new OpenAlAudioPlayer(new AudioFormat(Channels: 2, SampleFormat: SampleFormat.SignedPcm16,
            SampleRate: 48000));
    }
}