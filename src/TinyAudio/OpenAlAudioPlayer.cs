namespace TinyAudio;

using Silk.NET.OpenAL;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

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
    private readonly uint _bufferIndex;
    private readonly BufferFormat _openAlBufferFormat;
    private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private OpenAlAudioPlayer(AudioFormat format) : base(format) {
        try {
            _al = AL.GetApi(true);
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
        _device = _alContext.OpenDevice("");

        Available = _device != null;

        if (Available) {
            _context = _alContext.CreateContext(_device, null);
            _alContext.MakeContextCurrent(_context);
            if (_al.GetError() != AudioError.NoError) {
                Available = false;
                if (_context != null)
                    _alContext.DestroyContext(_context);
                _alContext.CloseDevice(_device);
                _al.Dispose();
                _alContext.Dispose();
                _disposed = true;
                return;
            }
            _source = _al.GenSource();
            _al.SetSourceProperty(_source, SourceBoolean.Looping, false);
            _al.SetSourceProperty(_source, SourceFloat.Gain, 1.0f);
            _bufferIndex = _al.GenBuffer();
            _openAlBufferFormat = BufferFormat.Stereo16;
        }
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public bool Enabled {
        get => _enabled;
        set {
            if (_enabled == value)
                return;

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

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public float Volume {
        get => _volume;
        set {
            value = Math.Max(0.0f, Math.Min(value, 1.0f));

            if (_volume == value)
                return;

            _volume = value;

            if (Available)
                _al?.SetSourceProperty(_source, SourceFloat.Gain, _volume);
        }
    }

    private int Size { get; set; }

    protected override void Dispose(bool disposing) {
        if (!this._disposed) {
            if (disposing) {
                _cancellationTokenSource.Cancel();

                if (_al?.IsBuffer(_bufferIndex) == true) {
                    _al.DeleteBuffer(_bufferIndex);
                    AudioError error = _al.GetError();
                    if (error != AudioError.NoError) {
                        Console.WriteLine($"OpenAL error while deleting buffer: " + error);
                    }
                }
                Size = 0;

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

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public void Reset() {
        if (!Available)
            return;

        _al?.SetSourceProperty(_source, SourceInteger.Buffer, 0u);
    }

    private bool Available { get; set; }

    protected override unsafe void Start(bool useCallback) {
        if (!Available || !Enabled)
            return;
        if (Streaming)
            return;
        if (_source == 0)
            throw new NotSupportedException("Start was called without a valid source.");
        Streaming = true;
    }

    protected override void Stop() {
        if (!Available || !Enabled)
            return;

        if (!Streaming)
            return;

        if (_source == 0)
            throw new NotSupportedException("Stop was called without a valid source.");

        Streaming = false;

        _al?.SourceStop(_source);
    }

    protected override unsafe int WriteDataInternal(ReadOnlySpan<byte> data) {
        for (int i = 0; i < data.Length; i++) {
            _al?.SourceQueueBuffers(data[i], new uint[1] { _bufferIndex });
        }
        return data.Length;
    }

    public static OpenAlAudioPlayer Create(TimeSpan bufferLength, bool useCallback = false) {
        _bufferLength = bufferLength;
        return new(new AudioFormat(Channels: 2, SampleFormat: SampleFormat.SignedPcm16, SampleRate: 22050));
    }
}