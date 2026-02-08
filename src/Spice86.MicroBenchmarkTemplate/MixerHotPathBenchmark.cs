namespace Spice86.MicroBenchmarkTemplate;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Libs.Sound.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Benchmark for Mixer hot path operations.
/// Focuses on MixSamples() inner loop which runs every audio tick (~21ms at 1024 blocksize/48kHz).
/// DOSBox reference: mixer.cpp mix_samples() lines 2394-2538
/// </summary>
[SimpleJob(RuntimeMoniker.HostProcess)]
[MemoryDiagnoser]
public class MixerHotPathBenchmark
{
    private const int Blocksize = 1024;
    private const int ChannelCount = 8; // Typical number of active channels
    private const int SampleRate = 48000;

    private AudioFrameBuffer _outputBuffer = null!;
    private AudioFrameBuffer _reverbAuxBuffer = null!;
    private AudioFrameBuffer _chorusAuxBuffer = null!;
    private AudioFrame[][] _channelFrames = null!;

    // Effects
    private MVerb _mverb = null!;
    private ChorusEngine _chorusEngine = null!;
    private Compressor _compressor = null!;
    private HighPass[] _masterHighPassFilter = null!;
    private HighPass[] _reverbHighPassFilter = null!;

    // Pre-allocated reverb buffers (DOSBox pattern)
    private float[] _reverbLeftIn = null!;
    private float[] _reverbRightIn = null!;
    private float[] _reverbLeftOut = null!;
    private float[] _reverbRightOut = null!;

    private AudioFrame _masterGain;
    private float _reverbSendGain;
    private float _chorusSendGain;

    [GlobalSetup]
    public void Setup()
    {
        _outputBuffer = new AudioFrameBuffer(Blocksize);
        _reverbAuxBuffer = new AudioFrameBuffer(Blocksize);
        _chorusAuxBuffer = new AudioFrameBuffer(Blocksize);

        // Initialize channel frames with test data
        _channelFrames = new AudioFrame[ChannelCount][];
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            _channelFrames[ch] = new AudioFrame[Blocksize];
            for (int i = 0; i < Blocksize; i++)
            {
                float sample = MathF.Sin(i * 0.1f) * 16384.0f;
                _channelFrames[ch][i] = new AudioFrame(sample, sample);
            }
        }

        // Initialize effects
        _mverb = new MVerb();
        _mverb.SetSampleRate(SampleRate);
        _mverb.SetParameter((int)MVerb.Parameter.Mix, 1.0f);
        _mverb.SetParameter((int)MVerb.Parameter.Size, 0.5f);
        _mverb.SetParameter((int)MVerb.Parameter.Decay, 0.5f);

        _chorusEngine = new ChorusEngine(SampleRate);
        _chorusEngine.SetEnablesChorus(isChorus1Enabled: true, isChorus2Enabled: false);

        _compressor = new Compressor();
        _compressor.Configure(SampleRate, 32767.0f, -6.0f, 3.0f, 0.01f, 5000.0f, 10.0f);

        _masterHighPassFilter = new HighPass[2];
        _reverbHighPassFilter = new HighPass[2];
        for (int i = 0; i < 2; i++)
        {
            _masterHighPassFilter[i] = new HighPass(2);
            _masterHighPassFilter[i].Setup(2, SampleRate, 20.0f);
            _reverbHighPassFilter[i] = new HighPass(2);
            _reverbHighPassFilter[i].Setup(2, SampleRate, 200.0f);
        }

        _reverbLeftIn = new float[Blocksize];
        _reverbRightIn = new float[Blocksize];
        _reverbLeftOut = new float[Blocksize];
        _reverbRightOut = new float[Blocksize];

        _masterGain = new AudioFrame(0.5f, 0.5f);
        _reverbSendGain = 0.5f;
        _chorusSendGain = 0.5f;
    }

    /// <summary>
    /// Baseline: Just buffer resize and clear operations.
    /// This is the minimum overhead for any mix operation.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int BufferOperationsOnly()
    {
        _outputBuffer.Resize(Blocksize);
        _reverbAuxBuffer.Resize(Blocksize);
        _chorusAuxBuffer.Resize(Blocksize);

        _outputBuffer.AsSpan().Clear();
        _reverbAuxBuffer.AsSpan().Clear();
        _chorusAuxBuffer.AsSpan().Clear();

        return _outputBuffer.Count;
    }

    /// <summary>
    /// Channel mixing loop only - the core accumulation.
    /// DOSBox: lines 2408-2441
    /// </summary>
    [Benchmark]
    public float ChannelMixingLoop()
    {
        _outputBuffer.Resize(Blocksize);
        _outputBuffer.AsSpan().Clear();

        // Mix all channels (simulated - no actual MixerChannel objects)
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            for (int i = 0; i < Blocksize; i++)
            {
                _outputBuffer[i] = _outputBuffer[i] + _channelFrames[ch][i];
            }
        }

        return _outputBuffer[0].Left;
    }

    /// <summary>
    /// Channel mixing with reverb/chorus send accumulation.
    /// </summary>
    [Benchmark]
    public float ChannelMixingWithSends()
    {
        _outputBuffer.Resize(Blocksize);
        _reverbAuxBuffer.Resize(Blocksize);
        _chorusAuxBuffer.Resize(Blocksize);

        _outputBuffer.AsSpan().Clear();
        _reverbAuxBuffer.AsSpan().Clear();
        _chorusAuxBuffer.AsSpan().Clear();

        // Mix all channels with reverb/chorus sends
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            for (int i = 0; i < Blocksize; i++)
            {
                AudioFrame frame = _channelFrames[ch][i];
                _outputBuffer[i] = _outputBuffer[i] + frame;
                _reverbAuxBuffer[i] = _reverbAuxBuffer[i] + (frame * _reverbSendGain);
                _chorusAuxBuffer[i] = _chorusAuxBuffer[i] + (frame * _chorusSendGain);
            }
        }

        return _outputBuffer[0].Left;
    }

    /// <summary>
    /// MVerb reverb processing - per-frame (DOSBox style).
    /// This is expensive because MVerb.Process is called per-frame.
    /// </summary>
    [Benchmark]
    public float ReverbProcessing_PerFrame()
    {
        _outputBuffer.Resize(Blocksize);
        _reverbAuxBuffer.Resize(Blocksize);

        // Fill with test data
        for (int i = 0; i < Blocksize; i++)
        {
            _reverbAuxBuffer[i] = new AudioFrame(MathF.Sin(i * 0.1f) * 1000.0f, MathF.Sin(i * 0.1f) * 1000.0f);
        }

        // Process reverb per-frame (DOSBox pattern)
        for (int i = 0; i < Blocksize; i++)
        {
            AudioFrame inFrame = _reverbAuxBuffer[i];
            inFrame = new AudioFrame(
                _reverbHighPassFilter[0].Filter(inFrame.Left),
                _reverbHighPassFilter[1].Filter(inFrame.Right)
            );

            _reverbLeftIn[0] = inFrame.Left;
            _reverbRightIn[0] = inFrame.Right;

            _mverb.Process(_reverbLeftIn, _reverbRightIn, _reverbLeftOut, _reverbRightOut, 1);

            _outputBuffer[i] = _outputBuffer[i] + new AudioFrame(_reverbLeftOut[0], _reverbRightOut[0]);
        }

        return _outputBuffer[0].Left;
    }

    /// <summary>
    /// MVerb reverb processing - batch (potentially faster).
    /// Process entire block at once.
    /// </summary>
    [Benchmark]
    public float ReverbProcessing_Batched()
    {
        _outputBuffer.Resize(Blocksize);
        _reverbAuxBuffer.Resize(Blocksize);

        // Fill with test data and apply high-pass
        for (int i = 0; i < Blocksize; i++)
        {
            AudioFrame frame = new AudioFrame(MathF.Sin(i * 0.1f) * 1000.0f, MathF.Sin(i * 0.1f) * 1000.0f);
            _reverbLeftIn[i] = _reverbHighPassFilter[0].Filter(frame.Left);
            _reverbRightIn[i] = _reverbHighPassFilter[1].Filter(frame.Right);
        }

        // Process entire block at once
        _mverb.Process(_reverbLeftIn, _reverbRightIn, _reverbLeftOut, _reverbRightOut, Blocksize);

        // Copy results to output
        for (int i = 0; i < Blocksize; i++)
        {
            _outputBuffer[i] = _outputBuffer[i] + new AudioFrame(_reverbLeftOut[i], _reverbRightOut[i]);
        }

        return _outputBuffer[0].Left;
    }

    /// <summary>
    /// Chorus processing.
    /// </summary>
    [Benchmark]
    public float ChorusProcessing()
    {
        _outputBuffer.Resize(Blocksize);
        _chorusAuxBuffer.Resize(Blocksize);

        // Fill with test data
        for (int i = 0; i < Blocksize; i++)
        {
            _chorusAuxBuffer[i] = new AudioFrame(MathF.Sin(i * 0.1f) * 1000.0f, MathF.Sin(i * 0.1f) * 1000.0f);
        }

        // Process chorus
        for (int i = 0; i < Blocksize; i++)
        {
            AudioFrame frame = _chorusAuxBuffer[i];
            float left = frame.Left;
            float right = frame.Right;
            _chorusEngine.Process(ref left, ref right);
            _outputBuffer[i] = _outputBuffer[i] + new AudioFrame(left, right);
        }

        return _outputBuffer[0].Left;
    }

    /// <summary>
    /// High-pass filter application.
    /// </summary>
    [Benchmark]
    public float HighPassFilterApplication()
    {
        _outputBuffer.Resize(Blocksize);

        // Fill with test data
        for (int i = 0; i < Blocksize; i++)
        {
            _outputBuffer[i] = new AudioFrame(MathF.Sin(i * 0.1f) * 16384.0f, MathF.Sin(i * 0.1f) * 16384.0f);
        }

        // Apply high-pass filter
        for (int i = 0; i < Blocksize; i++)
        {
            AudioFrame frame = _outputBuffer[i];
            _outputBuffer[i] = new AudioFrame(
                _masterHighPassFilter[0].Filter(frame.Left),
                _masterHighPassFilter[1].Filter(frame.Right)
            );
        }

        return _outputBuffer[0].Left;
    }

    /// <summary>
    /// Master gain application.
    /// </summary>
    [Benchmark]
    public float MasterGainApplication()
    {
        _outputBuffer.Resize(Blocksize);

        // Fill with test data
        for (int i = 0; i < Blocksize; i++)
        {
            _outputBuffer[i] = new AudioFrame(MathF.Sin(i * 0.1f) * 16384.0f, MathF.Sin(i * 0.1f) * 16384.0f);
        }

        // Apply master gain
        for (int i = 0; i < Blocksize; i++)
        {
            _outputBuffer[i] = _outputBuffer[i] * _masterGain;
        }

        return _outputBuffer[0].Left;
    }

    /// <summary>
    /// Compressor processing.
    /// </summary>
    [Benchmark]
    public float CompressorProcessing()
    {
        _outputBuffer.Resize(Blocksize);

        // Fill with test data
        for (int i = 0; i < Blocksize; i++)
        {
            _outputBuffer[i] = new AudioFrame(MathF.Sin(i * 0.1f) * 16384.0f, MathF.Sin(i * 0.1f) * 16384.0f);
        }

        // Apply compressor
        for (int i = 0; i < Blocksize; i++)
        {
            _outputBuffer[i] = _compressor.Process(_outputBuffer[i]);
        }

        return _outputBuffer[0].Left;
    }

    /// <summary>
    /// Normalization loop.
    /// </summary>
    [Benchmark]
    public float NormalizationLoop()
    {
        _outputBuffer.Resize(Blocksize);

        // Fill with test data
        for (int i = 0; i < Blocksize; i++)
        {
            _outputBuffer[i] = new AudioFrame(MathF.Sin(i * 0.1f) * 16384.0f, MathF.Sin(i * 0.1f) * 16384.0f);
        }

        // Normalize
        for (int i = 0; i < Blocksize; i++)
        {
            AudioFrame frame = _outputBuffer[i];
            _outputBuffer[i] = new AudioFrame(
                frame.Left / 32768.0f,
                frame.Right / 32768.0f
            );
        }

        return _outputBuffer[0].Left;
    }

    /// <summary>
    /// Full MixSamples simulation WITHOUT effects.
    /// </summary>
    [Benchmark]
    public float FullMixSamples_NoEffects()
    {
        _outputBuffer.Resize(Blocksize);
        _outputBuffer.AsSpan().Clear();

        // Mix channels
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            for (int i = 0; i < Blocksize; i++)
            {
                _outputBuffer[i] = _outputBuffer[i] + _channelFrames[ch][i];
            }
        }

        // Apply high-pass filter
        for (int i = 0; i < _outputBuffer.Count; i++)
        {
            AudioFrame frame = _outputBuffer[i];
            _outputBuffer[i] = new AudioFrame(
                _masterHighPassFilter[0].Filter(frame.Left),
                _masterHighPassFilter[1].Filter(frame.Right)
            );
        }

        // Apply master gain
        for (int i = 0; i < _outputBuffer.Count; i++)
        {
            _outputBuffer[i] = _outputBuffer[i] * _masterGain;
        }

        // Normalize
        for (int i = 0; i < _outputBuffer.Count; i++)
        {
            AudioFrame frame = _outputBuffer[i];
            _outputBuffer[i] = new AudioFrame(
                frame.Left / 32768.0f,
                frame.Right / 32768.0f
            );
        }

        return _outputBuffer[0].Left;
    }

    /// <summary>
    /// Full MixSamples simulation WITH all effects (reverb, chorus, compressor).
    /// This simulates the worst-case mixing scenario.
    /// </summary>
    [Benchmark]
    public float FullMixSamples_WithAllEffects()
    {
        _outputBuffer.Resize(Blocksize);
        _reverbAuxBuffer.Resize(Blocksize);
        _chorusAuxBuffer.Resize(Blocksize);

        _outputBuffer.AsSpan().Clear();
        _reverbAuxBuffer.AsSpan().Clear();
        _chorusAuxBuffer.AsSpan().Clear();

        // Mix channels with sends
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            for (int i = 0; i < Blocksize; i++)
            {
                AudioFrame frame = _channelFrames[ch][i];
                _outputBuffer[i] = _outputBuffer[i] + frame;
                _reverbAuxBuffer[i] = _reverbAuxBuffer[i] + (frame * _reverbSendGain);
                _chorusAuxBuffer[i] = _chorusAuxBuffer[i] + (frame * _chorusSendGain);
            }
        }

        // Apply reverb (batched)
        for (int i = 0; i < Blocksize; i++)
        {
            AudioFrame frame = _reverbAuxBuffer[i];
            _reverbLeftIn[i] = _reverbHighPassFilter[0].Filter(frame.Left);
            _reverbRightIn[i] = _reverbHighPassFilter[1].Filter(frame.Right);
        }
        _mverb.Process(_reverbLeftIn, _reverbRightIn, _reverbLeftOut, _reverbRightOut, Blocksize);
        for (int i = 0; i < Blocksize; i++)
        {
            _outputBuffer[i] = _outputBuffer[i] + new AudioFrame(_reverbLeftOut[i], _reverbRightOut[i]);
        }

        // Apply chorus
        for (int i = 0; i < Blocksize; i++)
        {
            AudioFrame frame = _chorusAuxBuffer[i];
            float left = frame.Left;
            float right = frame.Right;
            _chorusEngine.Process(ref left, ref right);
            _outputBuffer[i] = _outputBuffer[i] + new AudioFrame(left, right);
        }

        // Apply high-pass filter
        for (int i = 0; i < _outputBuffer.Count; i++)
        {
            AudioFrame frame = _outputBuffer[i];
            _outputBuffer[i] = new AudioFrame(
                _masterHighPassFilter[0].Filter(frame.Left),
                _masterHighPassFilter[1].Filter(frame.Right)
            );
        }

        // Apply master gain
        for (int i = 0; i < _outputBuffer.Count; i++)
        {
            _outputBuffer[i] = _outputBuffer[i] * _masterGain;
        }

        // Apply compressor
        for (int i = 0; i < _outputBuffer.Count; i++)
        {
            _outputBuffer[i] = _compressor.Process(_outputBuffer[i]);
        }

        // Normalize
        for (int i = 0; i < _outputBuffer.Count; i++)
        {
            AudioFrame frame = _outputBuffer[i];
            _outputBuffer[i] = new AudioFrame(
                frame.Left / 32768.0f,
                frame.Right / 32768.0f
            );
        }

        return _outputBuffer[0].Left;
    }

    /// <summary>
    /// Simulate per-instruction impact: how much time does mixer work take per CPU cycle?
    /// At 3000 cycles/ms and 48kHz audio, each mixer tick (1024 frames) spans ~21ms.
    /// That's 63,000 CPU cycles per mixer tick.
    /// </summary>
    [Benchmark]
    public double PerCycleImpact()
    {
        const double MsPerMixerTick = (double)Blocksize / SampleRate * 1000.0;

        // Run mixer work
        _outputBuffer.Resize(Blocksize);
        _outputBuffer.AsSpan().Clear();

        for (int ch = 0; ch < ChannelCount; ch++)
        {
            for (int i = 0; i < Blocksize; i++)
            {
                _outputBuffer[i] = _outputBuffer[i] + _channelFrames[ch][i];
            }
        }

        // This work happens once per ~63,000 cycles (at 3000 c/ms)
        // So per-cycle overhead is minimal if mixer runs on separate thread
        return MsPerMixerTick;
    }
}
