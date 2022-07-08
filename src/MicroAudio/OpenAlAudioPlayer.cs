namespace MicroAudio;

using Silk.NET.OpenAL;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;

[UnsupportedOSPlatform("browser")]
public sealed unsafe class OpenAlAudioPlayer : AudioPlayer {
    private const int MaxAlBuffers = 100;
    private const int OpenAlBufferModulo = 16;
    private readonly Stack<byte[]> _backBuffer = new();
    private static AL? _al = null;
    private static ALContext? _alContext = null;
    private static Device* _device = null;
    private static Context* _context = null;
    private readonly uint _source = 0;
    private bool _disposed = false;
    private readonly BufferFormat _openAlBufferFormat;
    private readonly Dictionary<uint, uint> _alBuffers = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private OpenAlAudioPlayer(AudioFormat format) : base(format) {
        try {
            _al ??= AL.GetApi(true);
            _al.GetError();
            _alContext ??= ALContext.GetApi(true);
        } catch {
            try {
                _al ??= AL.GetApi(false);
                _al.GetError();
                _alContext ??= ALContext.GetApi(false);
            } catch {
                return;
            }
        }

        if (_device is null) {
            _device = _alContext.OpenDevice(null);
        }

        bool available = _device != null;

        if (!available) {
            return;
        }

        if (_context is null) {
            _context = _alContext.CreateContext(_device, null);
            _alContext.MakeContextCurrent(_context);
        }
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
        if (currentState == SourceState.Playing) {
            return;
        }

        _al?.SourcePlay(_source);
        ThrowIfAlError();
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
        int remainingLength = GetRemainingLength(input.Length);
        if (processed > 0) {
            byte[]? data = null;
            byte[] inputToArray = input.ToArray();
            while (processed > 0 && remainingLength > 0) {
                uint buffer = 0;
                _al.SourceUnqueueBuffers(_source, 1, &buffer);
                _al.GetError();
                if (!TryEnqueueData(buffer, inputToArray, ref data, ref remainingLength)) {
                    break;
                }
                processed--;
            }
        } else if(_alBuffers.Count < MaxAlBuffers) {
            byte[]? data = null;
            byte[] inputToArray = input.ToArray();
            while (remainingLength > 0) {
                uint buffer = GenNewBuffer();
                if (!TryEnqueueData(buffer, inputToArray, ref data, ref remainingLength)) {
                    break;
                }
            }
        } else {
            return 0;
        }
        Play();
        return remainingLength;
    }
    private static int GetRemainingLength(int length)
    {
        if (length == 0) {
            return 0;
        }
        int remainingLength = length - (length % OpenAlBufferModulo);
        if (remainingLength == 0) {
            remainingLength = length;
        }
        return remainingLength;
    }

    private bool TryEnqueueData(uint buffer, byte[] inputToArray, ref byte[]? data, ref int remainingLength)
    {
        if (buffer == 0 || inputToArray.Length - data?.Length <= 0)
        {
            return false;
        }

        if (data is null)
        {
            data = inputToArray;
        }
        else
        {
            data = inputToArray[..data.Length];
        }

        while (_backBuffer.TryPop(out byte[]? bytes)) {
            byte[] newData = new byte[bytes.Length + data.Length];
            Array.Copy(bytes, newData, bytes.Length);
            Array.Copy(data, 0, newData, bytes.Length, data.Length);
            data = newData;
        }
        remainingLength = GetRemainingLength(data.Length);
        byte[] currentBytes = data[0..remainingLength];
        if (TryBufferData(buffer, currentBytes)) {
            SourceState state = GetSourceState();
            if (state is SourceState.Playing or SourceState.Paused)
            {
                _al?.SourceQueueBuffers(_source, 1, &buffer);
                ThrowIfAlError();
            }
            else
            {
                Play();
                _al?.SourceQueueBuffers(_source, 1, &buffer);
                ThrowIfAlError();
            }
        }
        return true;
    }

    private bool TryBufferData(uint buffer, byte[] data)
    {
        _al?.BufferData(buffer, _openAlBufferFormat, data, Format.SampleRate);
        if (_al?.GetError() == AudioError.InvalidValue)
        {
            _backBuffer.Push(data);
            return false;
        }
        return true;
    }

    private static void ThrowIfAlError() {
        AudioError? error = _al?.GetError();
        if(error is AudioError.InvalidValue) {
            throw new InvalidDataException(error.ToString());
        }
        else if(error is not AudioError.NoError) {
            throw new InvalidOperationException(error.ToString());
        }
    }

    public static OpenAlAudioPlayer Create() {
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