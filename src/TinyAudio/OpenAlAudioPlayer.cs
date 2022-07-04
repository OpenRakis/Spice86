namespace TinyAudio;

using Silk.NET.OpenAL;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

public sealed unsafe class OpenAlAudioPlayer : AudioPlayer {
    private const int MaxAlBuffers = 10;
    private const int OpenALBufferModulo = 16;
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

        if (!available) {
            return;
        }

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
            ThrowIfAlError();
            _al?.SetSourceProperty(_source, SourceBoolean.Looping, false);
            _al?.SetSourceProperty(_source, SourceFloat.Pitch, 1.0f);
            _al?.SetSourceProperty(_source, SourceFloat.Gain, 1.0f);
            _al?.SetSourceProperty(_source, SourceInteger.ByteOffset, 0);
            ThrowIfAlError();
        }
        _openAlBufferFormat = BufferFormat.Stereo16;
    }

    private uint GenNewBuffer()
    {
        if (_al is null) {
            throw new NullReferenceException(nameof(_al));
        }
        uint buffer = _al.GenBuffer();
        ThrowIfAlError();
        _alBuffers.Add(buffer, buffer);
        return buffer;
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
        SourceState currentState = GetSourceState();
        if (currentState != SourceState.Playing) {
            _al?.SourcePlay(_source);
            ThrowIfAlError();
        }
    }

    private SourceState GetSourceState()
    {
        int state = 0;
        _al?.GetSourceProperty(_source, GetSourceInteger.SourceState, out state);
        var currentState = (SourceState) state;
        return currentState;
    }

    protected override void Stop() {
        if (_source == 0) {
            throw new NotSupportedException("Stop was called without a valid source.");
        }
        _al?.SourceStop(_source);
    }

    /// <summary>
    /// Gives the converted audio data to the OpenAL backend.
    /// </summary>
    protected override int WriteDataInternal(ReadOnlySpan<byte> input) {
        if (_al is null) {
            throw new NullReferenceException(nameof(_al));
        }
        _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
        // We must use a multiple of 16 or else _al.BufferData will return InvalidValue
        int remainingLength = input.Length - (input.Length % OpenALBufferModulo);
        if (processed > 0) {
            byte[]? data = null;
            byte[] inputToArray = input.ToArray();
            while (processed > 0 && remainingLength > 0) {
                uint buffer = 0;
                _al.SourceUnqueueBuffers(_source, 1, &buffer);
                _al.GetError();
                if (BufferData(buffer, inputToArray, ref data, ref remainingLength)) {
                    break;
                }
                processed--;
            }
        } else if(_alBuffers.Count < MaxAlBuffers) {
            byte[]? data = null;
            byte[] inputToArray = input.ToArray();
            while (remainingLength > 0) {
                uint buffer = GenNewBuffer();
                if (BufferData(buffer, inputToArray, ref data, ref remainingLength)) {
                    break;
                }
            }
        } else {
            Play();
            return 0;
        }
        Play();
        return remainingLength;
    }

    private bool BufferData(uint buffer, byte[] inputToArray, ref byte[]? data, ref int remainingLength)
    {
        if (buffer == 0)
        {
            return true;
        }

        if (data is null)
        {
            data = inputToArray;
        }
        else
        {
            if (inputToArray.Length - data.Length <= 0)
            {
                return true;
            }

            data = inputToArray[..data.Length];
            remainingLength = data.Length - (data.Length % OpenALBufferModulo);
        }

        byte[] bytes = data[0..remainingLength];
        _al?.BufferData(buffer, _openAlBufferFormat, bytes, Format.SampleRate);
        ThrowIfAlError();
        SourceState state = GetSourceState();
        if (state is SourceState.Playing or SourceState.Paused)
        {
            _al?.SourceQueueBuffers(_source, 1, &buffer);
            ThrowIfAlError();
            Play();
        }
        else
        {
            Play();
            _al?.SourceQueueBuffers(_source, 1, &buffer);
            ThrowIfAlError();
        }

        return false;
    }

    private void ThrowIfAlError() {
        AudioError? error = _al?.GetError();
        if(error is AudioError.InvalidValue) {
            throw new InvalidDataException(error.ToString());
        }
        else if(error is not AudioError.NoError) {
            throw new InvalidOperationException(error.ToString());
        }
    }

    public static OpenAlAudioPlayer Create(TimeSpan bufferLength, bool useCallback = false) {
        return new OpenAlAudioPlayer(new AudioFormat(SampleRate: 48000, Channels: 2,
            SampleFormat: SampleFormat.SignedPcm16));
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