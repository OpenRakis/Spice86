namespace Spice86.Backend.Audio.OpenAl;

using Serilog;

using Silk.NET.OpenAL;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;

[UnsupportedOSPlatform("browser")]
public sealed unsafe class OpenAlAudioPlayer : AudioPlayer {
    private readonly ILogger _logger = Program.Logger.ForContext<OpenAlAudioPlayer>();
    private const int MaxAlBuffers = 100;
    private static AL? _al;
    private static ALContext? _alContext;
    private static Device* _device = null;
    private static Context* _context = null;
    private readonly uint _source = 0;
    private bool _disposed = false;
    private const BufferFormat OpenAlBufferFormat = BufferFormat.Stereo16;
    private readonly List<uint> _alBuffers = new();
    private static int _numberOfOpenAlPlayerInstances = 0;

    private OpenAlAudioPlayer(AudioFormat format) : base(format) {
        try {
            _al ??= AL.GetApi(true);
            _al.GetError();
            _alContext ??= ALContext.GetApi(true);
        } catch {
            _al ??= AL.GetApi(false);
            _al.GetError();
            _alContext ??= ALContext.GetApi(false);
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

    private uint GenerateNewOpenAlBuffer() {
        if (_al is null) {
            throw new NullReferenceException(nameof(_al));
        }
        uint buffer = _al.GenBuffer();
        ThrowIfAlError();
        _alBuffers.Add(buffer);
        return buffer;
    }

    public void Reset() {
        if (_source == 0) {
            throw new NotSupportedException($"{nameof(Reset)} was called without a valid source.");
        }
        _al?.SetSourceProperty(_source, SourceInteger.Buffer, 0u);
    }

    protected override void Start(bool useCallback) {
        if (_source == 0) {
            throw new NotSupportedException($"{nameof(Start)} was called without a valid source.");
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

    private SourceState GetSourceState() {
        int state = 0;
        _al?.GetSourceProperty(_source, GetSourceInteger.SourceState, out state);
        var currentState = (SourceState) state;
        if (currentState == SourceState.Stopped) {
            //_logger.Warning("{@Source} was not playing, it is {@State}", _source, currentState);
        }
        return currentState;
    }

    protected override void Stop() {
        if (_source == 0) {
            throw new NotSupportedException($"{nameof(Stop)} was called without a valid source.");
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
        if (processed > 0) {
            uint buffer = 0;
            _al.SourceUnqueueBuffers(_source, 1, &buffer);
            _al.GetError();
            BufferData(buffer, input);
        } else if(_alBuffers.Count < MaxAlBuffers) {
            uint buffer = GenerateNewOpenAlBuffer();
            BufferData(buffer, input);
        } else {
            return 0;
        }
        return input.Length;
    }

    private void BufferData(uint buffer, ReadOnlySpan<byte> input) {
        if (buffer == 0) {
            throw new NotSupportedException($"${nameof(BufferData)} was called without a valid ${nameof(buffer)}.");
        }
        _al?.BufferData(buffer, OpenAlBufferFormat, input.ToArray(), Format.SampleRate);
        ThrowIfAlError();
        Play();
        _al?.SourceQueueBuffers(_source, 1, &buffer);
        ThrowIfAlError();
        Play();
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
                if(_numberOfOpenAlPlayerInstances == 1) {
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