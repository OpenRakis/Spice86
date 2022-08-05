namespace Spice86.Core.Backend.Audio.OpenAl;

using Silk.NET.OpenAL;

using Spice86.Core.Backend.Audio;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
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
    private const BufferFormat _openAlBufferFormat = BufferFormat.Stereo16;
    private readonly List<uint> _alBuffers = new();
    private static int _numberOfOpenAlPlayerInstances = 0;
    private bool _started;

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
        _numberOfOpenAlPlayerInstances++;
    }

    private uint GenerateNewBuffer() {
        if (_al is null) {
            throw new NotSupportedException("GenerateNewBuffer was called without a valid OpenAL instance.");
        }
        uint buffer = _al.GenBuffer();
        ThrowIfAlError();
        _alBuffers.Add(buffer);
        return buffer;
    }

    protected override void Start(bool useCallback) {
        if (_source == 0) {
            throw new NotSupportedException("Start was called without a valid source.");
        }
        Play();
        _started = true;
    }

    private void Play() {
        SourceState currentState = GetSourceState();
        if (currentState == SourceState.Playing) {
            return;
        }
        _al?.SourcePlay(_source);
        ThrowIfAlError();
    }

    private SourceState GetSourceState() {
        int state = 0;
        _al?.GetSourceProperty(_source, GetSourceInteger.SourceState, out state);
        return (SourceState)state;
    }

    protected override void Stop() {
        if (_source == 0) {
            throw new NotSupportedException("Stop was called without a valid source.");
        }
        _al?.SourceStop(_source);
        _started = false;
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
        } else if (_alBuffers.Count < MaxAlBuffers) {
            byte[]? data = null;
            byte[] inputToArray = input.ToArray();
            while (remainingLength > 0) {
                uint buffer = GenerateNewBuffer();
                if (!TryEnqueueData(buffer, inputToArray, ref data, ref remainingLength)) {
                    break;
                }
            }
        } else {
            return 0;
        }
        if (_started) {
            Play();
        }
        return remainingLength > input.Length ? input.Length : remainingLength;
    }
    private static int GetRemainingLength(int length) {
        if (length == 0) {
            return 0;
        }
        int remainingLength = length - length % OpenAlBufferModulo;
        if (remainingLength == 0) {
            remainingLength = length;
        }
        return remainingLength;
    }

    private bool TryEnqueueData(uint buffer, byte[] inputToArray, ref byte[]? data, ref int remainingLength) {
        if (buffer == 0 || inputToArray.Length - data?.Length <= 0) {
            return false;
        }

        if (data is null) {
            data = inputToArray;
        } else {
            data = inputToArray[..data.Length];
        }
        if (_backBuffer.TryPeek(out _)) {
            while (_backBuffer.TryPop(out byte[]? bytes)) {
                byte[] newData = new byte[bytes.Length + data.Length];
                Array.Copy(bytes, newData, bytes.Length);
                Array.Copy(data, 0, newData, bytes.Length, data.Length);
                data = newData;
            }
            return TryQueueBuffer(buffer, data);
        } else {
            remainingLength = GetRemainingLength(data.Length);
            byte[] currentBytes = data[0..remainingLength];
            return TryQueueBuffer(buffer, currentBytes);
        }
    }

    private bool TryQueueBuffer(uint buffer, byte[] currentBytes) {
        if (TryBufferData(buffer, currentBytes)) {
            _al?.SourceQueueBuffers(_source, 1, &buffer);
            ThrowIfAlError();
            return true;
        }
        return false;
    }

    private bool TryBufferData(uint buffer, byte[] data) {
        _al?.BufferData(buffer, _openAlBufferFormat, data, Format.SampleRate);
        if (_al?.GetError() == AudioError.InvalidValue) {
            _backBuffer.Push(data);
            return false;
        }
        return true;
    }

    private static void ThrowIfAlError() {
        AudioError? error = _al?.GetError();
        if (error is AudioError.InvalidValue) {
            throw new InvalidDataException(error.ToString());
        } else if (error is not AudioError.NoError) {
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
                Stop();
                foreach (uint bufferIndex in _alBuffers) {
                    _al?.DeleteBuffer(bufferIndex);
                    AudioError? error = _al?.GetError();
                    if (error != AudioError.NoError) {
                        Console.WriteLine($"OpenAL error while deleting buffer: {error}");
                    }
                }
                if (_numberOfOpenAlPlayerInstances == 1) {
                    _al?.DeleteSource(_source);
                    _alContext?.DestroyContext(_context);
                    _alContext?.CloseDevice(_device);
                    _al?.Dispose();
                    _alContext?.Dispose();
                }
            }
            _numberOfOpenAlPlayerInstances--;
            _disposed = true;
        }
    }
}
