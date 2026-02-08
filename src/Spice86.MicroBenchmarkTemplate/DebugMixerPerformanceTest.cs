namespace Spice86.MicroBenchmarkTemplate;

using System;
using System.Diagnostics;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Libs.Sound.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth;

/// <summary>
/// Debug mode performance test for Mixer hot paths.
/// Does NOT use BenchmarkDotNet - runs raw timing measurements.
/// </summary>
public static class DebugMixerPerformanceTest
{
    private const int Blocksize = 1024;
    private const int ChannelCount = 8;
    private const int SampleRate = 48000;
    private const int Iterations = 1000;

    public static void Run()
    {
        Console.WriteLine();
        Console.WriteLine("=== Mixer Hot Path Performance Test ===");
        Console.WriteLine($"Build: {(IsDebugBuild() ? "DEBUG" : "RELEASE")}");
        Console.WriteLine($"Blocksize: {Blocksize}, Channels: {ChannelCount}, Iterations: {Iterations}");
        Console.WriteLine();

        // Setup
        var outputBuffer = new AudioFrameBuffer(Blocksize);
        var reverbAuxBuffer = new AudioFrameBuffer(Blocksize);
        var chorusAuxBuffer = new AudioFrameBuffer(Blocksize);

        var channelFrames = new AudioFrame[ChannelCount][];
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            channelFrames[ch] = new AudioFrame[Blocksize];
            for (int i = 0; i < Blocksize; i++)
            {
                float sample = MathF.Sin(i * 0.1f) * 16384.0f;
                channelFrames[ch][i] = new AudioFrame(sample, sample);
            }
        }

        var mverb = new MVerb();
        mverb.SetSampleRate(SampleRate);
        mverb.SetParameter((int)MVerb.Parameter.Mix, 1.0f);

        var chorusEngine = new ChorusEngine(SampleRate);
        chorusEngine.SetEnablesChorus(true, false);

        var compressor = new Compressor();
        compressor.Configure(SampleRate, 32767.0f, -6.0f, 3.0f, 0.01f, 5000.0f, 10.0f);

        var masterHighPassFilter = new HighPass[2];
        var reverbHighPassFilter = new HighPass[2];
        for (int i = 0; i < 2; i++)
        {
            masterHighPassFilter[i] = new HighPass(2);
            masterHighPassFilter[i].Setup(2, SampleRate, 20.0f);
            reverbHighPassFilter[i] = new HighPass(2);
            reverbHighPassFilter[i].Setup(2, SampleRate, 200.0f);
        }

        var reverbLeftIn = new float[Blocksize];
        var reverbRightIn = new float[Blocksize];
        var reverbLeftOut = new float[Blocksize];
        var reverbRightOut = new float[Blocksize];

        AudioFrame masterGain = new AudioFrame(0.5f, 0.5f);
        float reverbSendGain = 0.5f;
        float chorusSendGain = 0.5f;

        // Warmup
        for (int w = 0; w < 10; w++)
        {
            outputBuffer.Resize(Blocksize);
            outputBuffer.AsSpan().Clear();
        }

        var sw = new Stopwatch();

        // Test 1: Buffer operations only
        sw.Restart();
        for (int iter = 0; iter < Iterations; iter++)
        {
            outputBuffer.Resize(Blocksize);
            reverbAuxBuffer.Resize(Blocksize);
            chorusAuxBuffer.Resize(Blocksize);
            outputBuffer.AsSpan().Clear();
            reverbAuxBuffer.AsSpan().Clear();
            chorusAuxBuffer.AsSpan().Clear();
        }
        sw.Stop();
        PrintResult("Buffer Resize+Clear", sw.ElapsedTicks, Iterations);

        // Test 2: Channel mixing loop
        sw.Restart();
        for (int iter = 0; iter < Iterations; iter++)
        {
            outputBuffer.Resize(Blocksize);
            outputBuffer.AsSpan().Clear();
            for (int ch = 0; ch < ChannelCount; ch++)
            {
                for (int i = 0; i < Blocksize; i++)
                {
                    outputBuffer[i] = outputBuffer[i] + channelFrames[ch][i];
                }
            }
        }
        sw.Stop();
        PrintResult("Channel Mixing (8 channels)", sw.ElapsedTicks, Iterations);

        // Test 3: Channel mixing with reverb/chorus sends
        sw.Restart();
        for (int iter = 0; iter < Iterations; iter++)
        {
            outputBuffer.Resize(Blocksize);
            reverbAuxBuffer.Resize(Blocksize);
            chorusAuxBuffer.Resize(Blocksize);
            outputBuffer.AsSpan().Clear();
            reverbAuxBuffer.AsSpan().Clear();
            chorusAuxBuffer.AsSpan().Clear();
            for (int ch = 0; ch < ChannelCount; ch++)
            {
                for (int i = 0; i < Blocksize; i++)
                {
                    AudioFrame frame = channelFrames[ch][i];
                    outputBuffer[i] = outputBuffer[i] + frame;
                    reverbAuxBuffer[i] = reverbAuxBuffer[i] + (frame * reverbSendGain);
                    chorusAuxBuffer[i] = chorusAuxBuffer[i] + (frame * chorusSendGain);
                }
            }
        }
        sw.Stop();
        PrintResult("Channel Mixing + Sends", sw.ElapsedTicks, Iterations);

        // Test 4: Reverb processing (batched)
        for (int i = 0; i < Blocksize; i++)
        {
            reverbAuxBuffer[i] = new AudioFrame(MathF.Sin(i * 0.1f) * 1000.0f, MathF.Sin(i * 0.1f) * 1000.0f);
        }
        sw.Restart();
        for (int iter = 0; iter < Iterations; iter++)
        {
            for (int i = 0; i < Blocksize; i++)
            {
                AudioFrame frame = reverbAuxBuffer[i];
                reverbLeftIn[i] = reverbHighPassFilter[0].Filter(frame.Left);
                reverbRightIn[i] = reverbHighPassFilter[1].Filter(frame.Right);
            }
            mverb.Process(reverbLeftIn, reverbRightIn, reverbLeftOut, reverbRightOut, Blocksize);
        }
        sw.Stop();
        PrintResult("Reverb (MVerb batched)", sw.ElapsedTicks, Iterations);

        // Test 5: Chorus processing
        for (int i = 0; i < Blocksize; i++)
        {
            chorusAuxBuffer[i] = new AudioFrame(MathF.Sin(i * 0.1f) * 1000.0f, MathF.Sin(i * 0.1f) * 1000.0f);
        }
        sw.Restart();
        for (int iter = 0; iter < Iterations; iter++)
        {
            for (int i = 0; i < Blocksize; i++)
            {
                AudioFrame frame = chorusAuxBuffer[i];
                float left = frame.Left;
                float right = frame.Right;
                chorusEngine.Process(ref left, ref right);
            }
        }
        sw.Stop();
        PrintResult("Chorus (TAL-Chorus)", sw.ElapsedTicks, Iterations);

        // Test 6: High-pass filter
        for (int i = 0; i < Blocksize; i++)
        {
            outputBuffer[i] = new AudioFrame(MathF.Sin(i * 0.1f) * 16384.0f, MathF.Sin(i * 0.1f) * 16384.0f);
        }
        sw.Restart();
        for (int iter = 0; iter < Iterations; iter++)
        {
            for (int i = 0; i < outputBuffer.Count; i++)
            {
                AudioFrame frame = outputBuffer[i];
                outputBuffer[i] = new AudioFrame(
                    masterHighPassFilter[0].Filter(frame.Left),
                    masterHighPassFilter[1].Filter(frame.Right)
                );
            }
        }
        sw.Stop();
        PrintResult("High-pass Filter", sw.ElapsedTicks, Iterations);

        // Test 7: Compressor
        sw.Restart();
        for (int iter = 0; iter < Iterations; iter++)
        {
            for (int i = 0; i < outputBuffer.Count; i++)
            {
                outputBuffer[i] = compressor.Process(outputBuffer[i]);
            }
        }
        sw.Stop();
        PrintResult("Compressor", sw.ElapsedTicks, Iterations);

        // Test 8: Full MixSamples (no effects)
        sw.Restart();
        for (int iter = 0; iter < Iterations; iter++)
        {
            outputBuffer.Resize(Blocksize);
            outputBuffer.AsSpan().Clear();

            // Mix channels
            for (int ch = 0; ch < ChannelCount; ch++)
            {
                for (int i = 0; i < Blocksize; i++)
                {
                    outputBuffer[i] = outputBuffer[i] + channelFrames[ch][i];
                }
            }

            // High-pass
            for (int i = 0; i < outputBuffer.Count; i++)
            {
                AudioFrame frame = outputBuffer[i];
                outputBuffer[i] = new AudioFrame(
                    masterHighPassFilter[0].Filter(frame.Left),
                    masterHighPassFilter[1].Filter(frame.Right)
                );
            }

            // Master gain
            for (int i = 0; i < outputBuffer.Count; i++)
            {
                outputBuffer[i] = outputBuffer[i] * masterGain;
            }

            // Normalize
            for (int i = 0; i < outputBuffer.Count; i++)
            {
                AudioFrame frame = outputBuffer[i];
                outputBuffer[i] = new AudioFrame(
                    frame.Left / 32768.0f,
                    frame.Right / 32768.0f
                );
            }
        }
        sw.Stop();
        PrintResult("Full MixSamples (no effects)", sw.ElapsedTicks, Iterations);

        // Test 9: Full MixSamples (all effects)
        sw.Restart();
        for (int iter = 0; iter < Iterations; iter++)
        {
            outputBuffer.Resize(Blocksize);
            reverbAuxBuffer.Resize(Blocksize);
            chorusAuxBuffer.Resize(Blocksize);

            outputBuffer.AsSpan().Clear();
            reverbAuxBuffer.AsSpan().Clear();
            chorusAuxBuffer.AsSpan().Clear();

            // Mix channels with sends
            for (int ch = 0; ch < ChannelCount; ch++)
            {
                for (int i = 0; i < Blocksize; i++)
                {
                    AudioFrame frame = channelFrames[ch][i];
                    outputBuffer[i] = outputBuffer[i] + frame;
                    reverbAuxBuffer[i] = reverbAuxBuffer[i] + (frame * reverbSendGain);
                    chorusAuxBuffer[i] = chorusAuxBuffer[i] + (frame * chorusSendGain);
                }
            }

            // Reverb
            for (int i = 0; i < Blocksize; i++)
            {
                AudioFrame frame = reverbAuxBuffer[i];
                reverbLeftIn[i] = reverbHighPassFilter[0].Filter(frame.Left);
                reverbRightIn[i] = reverbHighPassFilter[1].Filter(frame.Right);
            }
            mverb.Process(reverbLeftIn, reverbRightIn, reverbLeftOut, reverbRightOut, Blocksize);
            for (int i = 0; i < Blocksize; i++)
            {
                outputBuffer[i] = outputBuffer[i] + new AudioFrame(reverbLeftOut[i], reverbRightOut[i]);
            }

            // Chorus
            for (int i = 0; i < Blocksize; i++)
            {
                AudioFrame frame = chorusAuxBuffer[i];
                float left = frame.Left;
                float right = frame.Right;
                chorusEngine.Process(ref left, ref right);
                outputBuffer[i] = outputBuffer[i] + new AudioFrame(left, right);
            }

            // High-pass
            for (int i = 0; i < outputBuffer.Count; i++)
            {
                AudioFrame frame = outputBuffer[i];
                outputBuffer[i] = new AudioFrame(
                    masterHighPassFilter[0].Filter(frame.Left),
                    masterHighPassFilter[1].Filter(frame.Right)
                );
            }

            // Master gain
            for (int i = 0; i < outputBuffer.Count; i++)
            {
                outputBuffer[i] = outputBuffer[i] * masterGain;
            }

            // Compressor
            for (int i = 0; i < outputBuffer.Count; i++)
            {
                outputBuffer[i] = compressor.Process(outputBuffer[i]);
            }

            // Normalize
            for (int i = 0; i < outputBuffer.Count; i++)
            {
                AudioFrame frame = outputBuffer[i];
                outputBuffer[i] = new AudioFrame(
                    frame.Left / 32768.0f,
                    frame.Right / 32768.0f
                );
            }
        }
        sw.Stop();
        PrintResult("Full MixSamples (ALL effects)", sw.ElapsedTicks, Iterations);

        // Summary
        Console.WriteLine();
        Console.WriteLine("=== Summary ===");
        double msPerMixerTick = (double)Blocksize / SampleRate * 1000.0;
        Console.WriteLine($"Audio blocksize: {Blocksize} frames = {msPerMixerTick:F2}ms @ {SampleRate}Hz");
        Console.WriteLine($"At 3000 cycles/ms: ~{(int)(3000 * msPerMixerTick)} CPU cycles between mixer ticks");
        Console.WriteLine($"At 10000 cycles/ms: ~{(int)(10000 * msPerMixerTick)} CPU cycles between mixer ticks");
        Console.WriteLine();
        Console.WriteLine("If mixer work takes >1ms/tick, it's a potential bottleneck.");
        Console.WriteLine("Mixer runs on separate thread, so main concern is thread contention.");
    }

    private static void PrintResult(string name, long ticks, int iterations)
    {
        double totalMs = (double)ticks / Stopwatch.Frequency * 1000.0;
        double msPerIter = totalMs / iterations;
        double usPerIter = msPerIter * 1000.0;
        double nsPerFrame = usPerIter * 1000.0 / Blocksize;

        Console.WriteLine($"  {name,-35} {msPerIter,8:F3} ms/iter ({nsPerFrame,6:F1} ns/frame)");
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }
}
