namespace Spice86.MicroBenchmarkTemplate;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Libs.Sound.Common;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Benchmark for AudioFrame struct operations.
/// AudioFrame is used heavily in audio mixing - every sample touches it.
/// DOSBox reference: audio_frame.h struct AudioFrame operations.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 2)]
public class AudioFrameBenchmark {
    private AudioFrame[] _frames = null!;
    private AudioFrame _gainFrame;
    private float _scalarGain;
    
    private const int FrameCount = 48000; // 1 second at 48kHz
    private const int Iterations = 100;

    [GlobalSetup]
    public void Setup() {
        _frames = new AudioFrame[FrameCount];
        for (int i = 0; i < FrameCount; i++) {
            _frames[i] = new AudioFrame(i * 0.001f, i * 0.001f);
        }
        _gainFrame = new AudioFrame(0.8f, 0.8f);
        _scalarGain = 0.8f;
    }

    /// <summary>
    /// Baseline: Direct field access and arithmetic.
    /// </summary>
    [Benchmark(Baseline = true)]
    public float DirectFieldArithmetic() {
        float total = 0;
        for (int iter = 0; iter < Iterations; iter++) {
            for (int i = 0; i < FrameCount; i++) {
                total += _frames[i].Left * _scalarGain;
                total += _frames[i].Right * _scalarGain;
            }
        }
        return total;
    }

    /// <summary>
    /// Using operator* overload.
    /// </summary>
    [Benchmark]
    public float OperatorMultiply() {
        float total = 0;
        for (int iter = 0; iter < Iterations; iter++) {
            for (int i = 0; i < FrameCount; i++) {
                var result = _frames[i] * _scalarGain;
                total += result.Left + result.Right;
            }
        }
        return total;
    }

    /// <summary>
    /// Using Multiply method.
    /// </summary>
    [Benchmark]
    public float MultiplyMethod() {
        float total = 0;
        for (int iter = 0; iter < Iterations; iter++) {
            for (int i = 0; i < FrameCount; i++) {
                var result = _frames[i].Multiply(_scalarGain);
                total += result.Left + result.Right;
            }
        }
        return total;
    }

    /// <summary>
    /// Frame * Frame multiplication.
    /// </summary>
    [Benchmark]
    public float FrameMultiplyFrame() {
        float total = 0;
        for (int iter = 0; iter < Iterations; iter++) {
            for (int i = 0; i < FrameCount; i++) {
                var result = _frames[i] * _gainFrame;
                total += result.Left + result.Right;
            }
        }
        return total;
    }

    /// <summary>
    /// Indexer access pattern (used in channel mapping).
    /// </summary>
    [Benchmark]
    public float IndexerAccess() {
        float total = 0;
        for (int iter = 0; iter < Iterations; iter++) {
            for (int i = 0; i < FrameCount; i++) {
                total += _frames[i][0];
                total += _frames[i][1];
            }
        }
        return total;
    }

    /// <summary>
    /// Creating new AudioFrame instances.
    /// </summary>
    [Benchmark]
    public AudioFrame FrameConstruction() {
        AudioFrame result = default;
        for (int iter = 0; iter < Iterations; iter++) {
            for (int i = 0; i < FrameCount; i++) {
                result = new AudioFrame(i * 0.001f, i * 0.001f);
            }
        }
        return result;
    }

    /// <summary>
    /// Span of AudioFrame memory operations.
    /// </summary>
    [Benchmark]
    public void SpanCopy() {
        var source = _frames.AsSpan();
        var dest = new AudioFrame[FrameCount];
        for (int iter = 0; iter < Iterations; iter++) {
            source.CopyTo(dest);
        }
    }

    /// <summary>
    /// Span clear operation.
    /// </summary>
    [Benchmark]
    public void SpanClear() {
        var span = _frames.AsSpan();
        for (int iter = 0; iter < Iterations; iter++) {
            span.Clear();
        }
    }

    /// <summary>
    /// LERP interpolation (used in upsampling).
    /// </summary>
    [Benchmark]
    public AudioFrame LerpInterpolation() {
        AudioFrame a = new AudioFrame(0.0f, 0.0f);
        AudioFrame b = new AudioFrame(1.0f, 1.0f);
        AudioFrame result = default;
        
        for (int iter = 0; iter < Iterations; iter++) {
            for (int i = 0; i < FrameCount; i++) {
                float t = (float)i / FrameCount;
                result = new AudioFrame(
                    a.Left * (1.0f - t) + b.Left * t,
                    a.Right * (1.0f - t) + b.Right * t
                );
            }
        }
        return result;
    }

    /// <summary>
    /// Inline LERP pattern.
    /// </summary>
    [Benchmark]
    public AudioFrame InlineLerp() {
        AudioFrame a = new AudioFrame(0.0f, 0.0f);
        AudioFrame b = new AudioFrame(1.0f, 1.0f);
        AudioFrame result = default;
        
        for (int iter = 0; iter < Iterations; iter++) {
            for (int i = 0; i < FrameCount; i++) {
                float t = (float)i / FrameCount;
                result = LerpInline(a, b, t);
            }
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static AudioFrame LerpInline(AudioFrame a, AudioFrame b, float t) {
        return new AudioFrame(
            a.Left * (1.0f - t) + b.Left * t,
            a.Right * (1.0f - t) + b.Right * t
        );
    }
}

/// <summary>
/// Benchmark for AudioFrameBuffer operations.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class AudioFrameBufferBenchmark {
    private AudioFrameBuffer _buffer = null!;
    private AudioFrame[] _sourceArray = null!;
    
    private const int BufferSize = 4096;
    private const int Iterations = 1000;

    [GlobalSetup]
    public void Setup() {
        _buffer = new AudioFrameBuffer(BufferSize);
        _sourceArray = new AudioFrame[BufferSize];
        for (int i = 0; i < BufferSize; i++) {
            _sourceArray[i] = new AudioFrame(i * 0.001f, i * 0.001f);
        }
    }

    /// <summary>
    /// Baseline: Individual Add operations.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int IndividualAdd() {
        for (int iter = 0; iter < Iterations; iter++) {
            _buffer.Clear();
            for (int i = 0; i < BufferSize; i++) {
                _buffer.Add(_sourceArray[i]);
            }
        }
        return _buffer.Count;
    }

    /// <summary>
    /// Bulk AddRange with span.
    /// </summary>
    [Benchmark]
    public int BulkAddRange() {
        for (int iter = 0; iter < Iterations; iter++) {
            _buffer.Clear();
            _buffer.AddRange(_sourceArray.AsSpan());
        }
        return _buffer.Count;
    }

    /// <summary>
    /// Clear operation cost.
    /// </summary>
    [Benchmark]
    public void ClearOperation() {
        _buffer.AddRange(_sourceArray.AsSpan());
        for (int iter = 0; iter < Iterations * 100; iter++) {
            _buffer.Clear();
            _buffer.AddRange(_sourceArray.AsSpan());
        }
    }

    /// <summary>
    /// Indexer read access.
    /// </summary>
    [Benchmark]
    public float IndexerRead() {
        _buffer.Clear();
        _buffer.AddRange(_sourceArray.AsSpan());
        
        float total = 0;
        for (int iter = 0; iter < Iterations; iter++) {
            for (int i = 0; i < _buffer.Count; i++) {
                total += _buffer[i].Left;
            }
        }
        return total;
    }

    /// <summary>
    /// AsSpan iteration.
    /// </summary>
    [Benchmark]
    public float SpanIteration() {
        _buffer.Clear();
        _buffer.AddRange(_sourceArray.AsSpan());
        
        float total = 0;
        for (int iter = 0; iter < Iterations; iter++) {
            foreach (var frame in _buffer.AsSpan()) {
                total += frame.Left;
            }
        }
        return total;
    }

    /// <summary>
    /// Resize operation.
    /// </summary>
    [Benchmark]
    public void ResizeOperation() {
        for (int iter = 0; iter < Iterations; iter++) {
            _buffer.Clear();
            _buffer.Resize(BufferSize);
            _buffer.Resize(BufferSize / 2);
        }
    }

    /// <summary>
    /// EnsureCapacity overhead.
    /// </summary>
    [Benchmark]
    public void EnsureCapacity() {
        var buffer = new AudioFrameBuffer(16);
        for (int iter = 0; iter < Iterations; iter++) {
            buffer.Clear();
            for (int i = 0; i < BufferSize; i++) {
                buffer.Add(_sourceArray[i]);
            }
        }
    }
}

/// <summary>
/// Benchmark for Lock contention patterns used in MixerChannel.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class LockContentionBenchmark {
    private readonly Lock _newLock = new();
    private readonly object _objectLock = new();
    
    private const int Iterations = 1_000_000;

    /// <summary>
    /// New Lock type (C# 13).
    /// </summary>
    [Benchmark(Baseline = true)]
    public int NewLockType() {
        int counter = 0;
        for (int i = 0; i < Iterations; i++) {
            lock (_newLock) {
                counter++;
            }
        }
        return counter;
    }

    /// <summary>
    /// Traditional object lock.
    /// </summary>
    [Benchmark]
    public int ObjectLock() {
        int counter = 0;
        for (int i = 0; i < Iterations; i++) {
            lock (_objectLock) {
                counter++;
            }
        }
        return counter;
    }

    /// <summary>
    /// Monitor.Enter/Exit pattern.
    /// </summary>
    [Benchmark]
    public int MonitorEnterExit() {
        int counter = 0;
        for (int i = 0; i < Iterations; i++) {
            bool taken = false;
            try {
                Monitor.Enter(_objectLock, ref taken);
                counter++;
            } finally {
                if (taken) Monitor.Exit(_objectLock);
            }
        }
        return counter;
    }

    /// <summary>
    /// SpinLock (no contention case).
    /// </summary>
    [Benchmark]
    public int SpinLockNoContention() {
        var spinLock = new SpinLock(false);
        int counter = 0;
        for (int i = 0; i < Iterations; i++) {
            bool taken = false;
            try {
                spinLock.Enter(ref taken);
                counter++;
            } finally {
                if (taken) spinLock.Exit(false);
            }
        }
        return counter;
    }
}
