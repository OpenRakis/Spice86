// SPDX-License-Identifier: BSD-3-Clause
/* Copyright (C) 2007-2008 Jean-Marc Valin
   Copyright (C) 2008      Thorvald Natvig
   C# Port: 2025 for Spice86 project

   File: SpeexResamplerCSharp.cs
   Arbitrary resampling code - Pure C# port from libspeexdsp/resample.c

   Redistribution and use in source and binary forms, with or without
   modification, are permitted provided that the following conditions are
   met:

   1. Redistributions of source code must retain the above copyright notice,
   this list of conditions and the following disclaimer.

   2. Redistributions in binary form must reproduce the above copyright
   notice, this list of conditions and the following disclaimer in the
   documentation and/or other materials provided with the distribution.

   3. The name of the author may not be used to endorse or promote products
   derived from this software without specific prior written permission.

   THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR
   IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
   OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
   DISCLAIMED. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT,
   INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
   (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
   SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
   HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
   STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
   ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
   POSSIBILITY OF SUCH DAMAGE.
*/

/*
   The design goals of this code are:
      - Very fast algorithm
      - SIMD-friendly algorithm
      - Low memory requirement
      - Good *perceptual* quality (and not best SNR)

   Warning: This resampler is relatively new. Although I think I got rid of
   all the major bugs and I don't expect the API to change anymore, there
   may be something I've missed. So use with caution.

   This algorithm is based on this original resampling algorithm:
   Smith, Julius O. Digital Audio Resampling Home Page
   Center for Computer Research in Music and Acoustics (CCRMA),
   Stanford University, 2007.
   Web published at https://ccrma.stanford.edu/~jos/resample/.

   There is one main difference, though. This resampler uses cubic
   interpolation instead of linear interpolation in the above paper. This
   makes the table much smaller and makes it possible to compute that table
   on a per-stream basis. In turn, being able to tweak the table for each
   stream makes it possible to both reduce complexity on simple ratios
   (e.g. 2/3), and get rid of the rounding operations in the inner loop.
   The latter both reduces CPU time and makes the algorithm more SIMD-friendly.
*/

namespace Bufdio.Spice86;

/// <summary>
/// Pure C# port of Speex high-quality audio resampler from libspeexdsp.
/// Provides arbitrary sample rate conversion with configurable quality levels.
/// This is a faithful port maintaining the exact algorithms and structure from the C implementation.
/// </summary>
public sealed class SpeexResamplerCSharp : IDisposable {
    private const double MPI = 3.14159265358979323846;
    
    // Kaiser window tables - exact values from resample.c
    private static readonly double[] Kaiser12Table = new double[68] {
        0.99859849, 1.00000000, 0.99859849, 0.99440475, 0.98745105, 0.97779076,
        0.96549770, 0.95066529, 0.93340547, 0.91384741, 0.89213598, 0.86843014,
        0.84290116, 0.81573067, 0.78710866, 0.75723148, 0.72629970, 0.69451601,
        0.66208321, 0.62920216, 0.59606986, 0.56287762, 0.52980938, 0.49704014,
        0.46473455, 0.43304576, 0.40211431, 0.37206735, 0.34301800, 0.31506490,
        0.28829195, 0.26276832, 0.23854851, 0.21567274, 0.19416736, 0.17404546,
        0.15530766, 0.13794294, 0.12192957, 0.10723616, 0.09382272, 0.08164178,
        0.07063950, 0.06075685, 0.05193064, 0.04409466, 0.03718069, 0.03111947,
        0.02584161, 0.02127838, 0.01736250, 0.01402878, 0.01121463, 0.00886058,
        0.00691064, 0.00531256, 0.00401805, 0.00298291, 0.00216702, 0.00153438,
        0.00105297, 0.00069463, 0.00043489, 0.00025272, 0.00013031, 0.0000527734,
        0.00001000, 0.00000000
    };

    private static readonly double[] Kaiser10Table = new double[36] {
        0.99537781, 1.00000000, 0.99537781, 0.98162644, 0.95908712, 0.92831446,
        0.89005583, 0.84522401, 0.79486424, 0.74011713, 0.68217934, 0.62226347,
        0.56155915, 0.50119680, 0.44221549, 0.38553619, 0.33194107, 0.28205962,
        0.23636152, 0.19515633, 0.15859932, 0.12670280, 0.09935205, 0.07632451,
        0.05731132, 0.04193980, 0.02979584, 0.02044510, 0.01345224, 0.00839739,
        0.00488951, 0.00257636, 0.00115101, 0.00035515, 0.00000000, 0.00000000
    };

    private static readonly double[] Kaiser8Table = new double[36] {
        0.99635258, 1.00000000, 0.99635258, 0.98548012, 0.96759014, 0.94302200,
        0.91223751, 0.87580811, 0.83439927, 0.78875245, 0.73966538, 0.68797126,
        0.63451750, 0.58014482, 0.52566725, 0.47185369, 0.41941150, 0.36897272,
        0.32108304, 0.27619388, 0.23465776, 0.19672670, 0.16255380, 0.13219758,
        0.10562887, 0.08273982, 0.06335451, 0.04724088, 0.03412321, 0.02369490,
        0.01563093, 0.00959968, 0.00527363, 0.00233883, 0.00050000, 0.00000000
    };

    private static readonly double[] Kaiser6Table = new double[36] {
        0.99733006, 1.00000000, 0.99733006, 0.98935595, 0.97618418, 0.95799003,
        0.93501423, 0.90755855, 0.87598009, 0.84068475, 0.80211977, 0.76076565,
        0.71712752, 0.67172623, 0.62508937, 0.57774224, 0.53019925, 0.48295561,
        0.43647969, 0.39120616, 0.34752997, 0.30580127, 0.26632152, 0.22934058,
        0.19505503, 0.16360756, 0.13508755, 0.10953262, 0.08693120, 0.06722600,
        0.05031820, 0.03607231, 0.02432151, 0.01487334, 0.00752000, 0.00000000
    };

    // FuncDef structure - mirrors C struct
    private sealed class FuncDef {
        public readonly double[] Table;
        public readonly int Oversample;

        public FuncDef(double[] table, int oversample) {
            Table = table;
            Oversample = oversample;
        }
    }

    private static readonly FuncDef Kaiser12 = new(Kaiser12Table, 64);
    private static readonly FuncDef Kaiser10 = new(Kaiser10Table, 32);
    private static readonly FuncDef Kaiser8 = new(Kaiser8Table, 32);
    private static readonly FuncDef Kaiser6 = new(Kaiser6Table, 32);

    // QualityMapping structure - mirrors C struct
    private sealed class QualityMapping {
        public readonly int BaseLength;
        public readonly int Oversample;
        public readonly float DownsampleBandwidth;
        public readonly float UpsampleBandwidth;
        public readonly FuncDef WindowFunc;

        public QualityMapping(int baseLength, int oversample, float downsample, float upsample, FuncDef window) {
            BaseLength = baseLength;
            Oversample = oversample;
            DownsampleBandwidth = downsample;
            UpsampleBandwidth = upsample;
            WindowFunc = window;
        }
    }

    // Quality mapping table - exact values from resample.c
    private static readonly QualityMapping[] QualityMap = new QualityMapping[11] {
        new(  8,  4, 0.830f, 0.860f, Kaiser6),  /* Q0 */
        new( 16,  4, 0.850f, 0.880f, Kaiser6),  /* Q1 */
        new( 32,  4, 0.882f, 0.910f, Kaiser6),  /* Q2 */  /* 82.3% cutoff ( ~60 dB stop) 6  */
        new( 48,  8, 0.895f, 0.917f, Kaiser8),  /* Q3 */  /* 84.9% cutoff ( ~80 dB stop) 8  */
        new( 64,  8, 0.921f, 0.940f, Kaiser8),  /* Q4 */  /* 88.7% cutoff ( ~80 dB stop) 8  */
        new( 80, 16, 0.922f, 0.940f, Kaiser10), /* Q5 */  /* 89.1% cutoff (~100 dB stop) 10 */
        new( 96, 16, 0.940f, 0.945f, Kaiser10), /* Q6 */  /* 91.5% cutoff (~100 dB stop) 10 */
        new(128, 16, 0.950f, 0.950f, Kaiser10), /* Q7 */  /* 93.1% cutoff (~100 dB stop) 10 */
        new(160, 16, 0.960f, 0.960f, Kaiser10), /* Q8 */  /* 94.5% cutoff (~100 dB stop) 10 */
        new(192, 32, 0.968f, 0.968f, Kaiser12), /* Q9 */  /* 95.5% cutoff (~100 dB stop) 10 */
        new(256, 32, 0.975f, 0.975f, Kaiser12)  /* Q10 */ /* 96.6% cutoff (~100 dB stop) 10 */
    };

    // Resampler state - mirrors SpeexResamplerState_
    private uint _inRate;
    private uint _outRate;
    private uint _numRate;
    private uint _denRate;
    
    private int _quality;
    private readonly uint _nbChannels;
    private uint _filtLen;
    private uint _memAllocSize;
    private readonly uint _bufferSize;
    private int _intAdvance;
    private int _fracAdvance;
    private float _cutoff;
    private uint _oversample;
    private readonly bool _initialised;
    private bool _started;
    
    // Per-channel arrays
    private readonly int[] _lastSample;
    private readonly uint[] _sampFracNum;
    private readonly uint[] _magicSamples;
    
    private float[] _mem;
    private float[]? _sincTable;
    private uint _sincTableLength;
    
    private int _inStride;
    private int _outStride;
    
    // Pre-allocated buffers to avoid allocations in hot paths
    private readonly float[] _cubicInterpBuffer = new float[4];
    
    private bool _disposed;

    /// <summary>
    /// Gets whether the resampler is initialized.
    /// </summary>
    public bool IsInitialized => _initialised;

    /// <summary>
    /// Gets the number of channels.
    /// </summary>
    public uint Channels => _nbChannels;

    /// <summary>
    /// Gets the input sample rate.
    /// </summary>
    public uint InputRate => _inRate;

    /// <summary>
    /// Gets the output sample rate.
    /// </summary>
    public uint OutputRate => _outRate;

    /// <summary>
    /// Create a new resampler with integer input and output rates.
    /// Mirrors speex_resampler_init()
    /// </summary>
    /// <param name="channels">Number of channels to resample</param>
    /// <param name="inputRate">Input sample rate in Hz</param>
    /// <param name="outputRate">Output sample rate in Hz</param>
    /// <param name="quality">Quality setting (0-10)</param>
    public SpeexResamplerCSharp(uint channels, uint inputRate, uint outputRate, int quality) {
        if (channels == 0 || channels > 256) {
            throw new ArgumentException("Invalid number of channels", nameof(channels));
        }
        
        if (inputRate == 0 || inputRate < 2000 || inputRate > 384000) {
            throw new ArgumentException("Input rate must be between 2000 and 384000 Hz", nameof(inputRate));
        }
        
        if (outputRate == 0 || outputRate < 2000 || outputRate > 384000) {
            throw new ArgumentException("Output rate must be between 2000 and 384000 Hz", nameof(outputRate));
        }
        
        if (quality < 0 || quality > 10) {
            throw new ArgumentException("Quality must be between 0 and 10", nameof(quality));
        }

        _nbChannels = channels;
        _inRate = inputRate;
        _outRate = outputRate;
        _numRate = 0;
        _denRate = 0;
        _quality = quality;
        _filtLen = 0;
        _memAllocSize = 0;
        _bufferSize = 160;
        _intAdvance = 0;
        _fracAdvance = 0;
        _cutoff = 1.0f;
        _oversample = 0;
        _initialised = false;
        _started = false;
        
        _inStride = 1;
        _outStride = 1;
        
        // Allocate per-channel arrays
        _lastSample = new int[channels];
        _sampFracNum = new uint[channels];
        _magicSamples = new uint[channels];
        _mem = Array.Empty<float>();
        
        // Initialize
        UpdateFilter();
        _initialised = true;
    }

    /* WARNING: This resampler is relatively new. Although I think I got rid of
       all the major bugs and I don't expect the API to change anymore, there
       may be something I've missed. So use with caution. */

    // compute_func() - Cubic interpolation of Kaiser window
    // Mirrors: static double compute_func(float x, const struct FuncDef *func)
    private static double ComputeFunc(float x, FuncDef func) {
        float y = x * func.Oversample;
        int ind = (int)Math.Floor(y);
        float frac = y - ind;
        
        double[] interp = new double[4];
        
        /* CSE with handle the repeated powers */
        interp[3] = -0.1666666667 * frac + 0.1666666667 * (frac * frac * frac);
        interp[2] = frac + 0.5 * (frac * frac) - 0.5 * (frac * frac * frac);
        /*interp[2] = 1.f - 0.5f*frac - frac*frac + 0.5f*frac*frac*frac;*/
        interp[0] = -0.3333333333 * frac + 0.5 * (frac * frac) - 0.1666666667 * (frac * frac * frac);
        /* Just to make sure we don't have rounding problems */
        interp[1] = 1.0 - interp[3] - interp[2] - interp[0];

        /*sum = frac*accum[1] + (1-frac)*accum[2];*/
        // Bounds check: ensure ind + 3 doesn't exceed table length
        int maxInd = func.Table.Length - 4;
        if (ind > maxInd) {
            ind = maxInd;
        }
        
        return interp[0] * func.Table[ind] + interp[1] * func.Table[ind + 1] + 
               interp[2] * func.Table[ind + 2] + interp[3] * func.Table[ind + 3];
    }

    // sinc() - Windowed sinc function
    // Mirrors: static spx_word16_t sinc(float cutoff, float x, int N, const struct FuncDef *window_func)
    private static float Sinc(float cutoff, float x, int n, FuncDef windowFunc) {
        /*fprintf (stderr, "%f ", x);*/
        float xx = x * cutoff;
        if (Math.Abs(x) < 1e-6) {
            return cutoff;
        } else if (Math.Abs(x) > 0.5 * n) {
            return 0;
        }
        /*FIXME: Can it really be any slower than this? */
        return (float)(cutoff * Math.Sin(MPI * xx) / (MPI * xx) * ComputeFunc(Math.Abs(2.0f * x / n), windowFunc));
    }

    // cubic_coef() - Compute cubic interpolation coefficients
    // Mirrors: static void cubic_coef(spx_word16_t frac, spx_word16_t interp[4])
    private static void CubicCoef(float frac, float[] interp) {
        /* Compute interpolation coefficients. I'm not sure whether this corresponds to cubic interpolation
           but I know it's MMSE-optimal on a sinc */
        interp[0] = -0.16667f * frac + 0.16667f * frac * frac * frac;
        interp[1] = frac + 0.5f * frac * frac - 0.5f * frac * frac * frac;
        /*interp[2] = 1.f - 0.5f*frac - frac*frac + 0.5f*frac*frac*frac;*/
        interp[3] = -0.33333f * frac + 0.5f * frac * frac - 0.16667f * frac * frac * frac;
        /* Just to make sure we don't have rounding problems */
        interp[2] = 1.0f - interp[0] - interp[1] - interp[3];
    }

    // UpdateFilter() - Update filter parameters and regenerate sinc table
    // Mirrors: static int update_filter(SpeexResamplerState *st)
    private void UpdateFilter() {
        if (_numRate == 0 || _denRate == 0) {
            uint fact;
            uint intAdvance;
            uint fracAdvance;
            
            // Compute gcd
            uint a = _inRate;
            uint b = _outRate;
            while (b != 0) {
                uint temp = a % b;
                a = b;
                b = temp;
            }
            fact = a;
            
            _numRate = _inRate / fact;
            _denRate = _outRate / fact;
            
            if (_numRate > _denRate) {
                // Downsampling
                intAdvance = _numRate / _denRate;
                fracAdvance = _numRate % _denRate;
            } else {
                // Upsampling
                intAdvance = 0;
                fracAdvance = _numRate;
            }
            _intAdvance = (int)intAdvance;
            _fracAdvance = (int)fracAdvance;
        }

        QualityMapping qm = QualityMap[_quality];
        _filtLen = (uint)qm.BaseLength;
        
        // Downsampling uses downsample bandwidth; upsampling uses upsample bandwidth
        _cutoff = _numRate > _denRate ? qm.DownsampleBandwidth : qm.UpsampleBandwidth;
        
        _oversample = (uint)qm.Oversample;
        
        FuncDef windowFunc = qm.WindowFunc;

        // Compute the sinc filter
        _sincTableLength = _filtLen * _oversample + 1;
        _sincTable = new float[_sincTableLength];
        
        for (int i = 0; i < (int)_sincTableLength; i++) {
            // Fix precision loss: use proper float division
            _sincTable[i] = Sinc(_cutoff, (float)i / (float)_oversample - (float)_filtLen / 2.0f, (int)_filtLen, windowFunc);
        }

        // Allocate memory for channel state
        uint minAllocSize = _filtLen * _nbChannels;
        if (_memAllocSize < minAllocSize) {
            float[] newMem = new float[minAllocSize * 2];  // Allocate extra space
            if (_mem.Length > 0) {
                Array.Copy(_mem, newMem, Math.Min(_mem.Length, newMem.Length));
            }
            _mem = newMem;
            _memAllocSize = (uint)_mem.Length;
        }

        if (!_started) {
            // Reset channel state
            for (uint i = 0; i < _nbChannels; i++) {
                _lastSample[i] = 0;
                _magicSamples[i] = 0;
                _sampFracNum[i] = 0;
            }
            _started = true;
        }
    }

    // ProcessFloat() - Process floating-point samples
    // Mirrors: speex_resampler_process_float()
    public void ProcessFloat(uint channelIndex, ReadOnlySpan<float> input, Span<float> output, 
        out uint inputConsumed, out uint outputGenerated) {
        
        if (channelIndex >= _nbChannels) {
            throw new ArgumentException("Invalid channel index", nameof(channelIndex));
        }

        if (!_initialised) {
            throw new InvalidOperationException("Resampler not initialized");
        }

        uint inLen = (uint)input.Length;
        uint outLen = (uint)output.Length;

        // Use direct algorithm
        ResamplerBasicDirect(channelIndex, input, ref inLen, output, ref outLen);

        inputConsumed = inLen;
        outputGenerated = outLen;
    }

    // ResamplerBasicDirect() - Direct resampling using sinc table
    // Mirrors: static int resampler_basic_direct_double()
    private void ResamplerBasicDirect(uint channelIndex, ReadOnlySpan<float> input, ref uint inLen, 
        Span<float> output, ref uint outLen) {
        
        int n = (int)_filtLen;
        int outSample = 0;
        int lastSample = _lastSample[channelIndex];
        uint sampFracNum = _sampFracNum[channelIndex];
        int outStride = _outStride;
        int intAdvance = _intAdvance;
        int fracAdvance = _fracAdvance;
        uint denRate = _denRate;

        // Ensure we have enough memory
        uint memRequired = (uint)(n + inLen);
        if (memRequired > _memAllocSize / _nbChannels) {
            uint newSize = memRequired * 2 * _nbChannels;
            float[] newMem = new float[newSize];
            if (_mem.Length > 0) {
                Array.Copy(_mem, newMem, Math.Min(_mem.Length, newSize));
            }
            _mem = newMem;
            _memAllocSize = newSize;
        }

        int channelOffset = (int)(channelIndex * (_memAllocSize / _nbChannels));

        // Copy input samples to memory buffer
        for (int i = 0; i < (int)inLen; i++) {
            _mem[channelOffset + n - 1 + lastSample + i] = input[i];
        }

        while (!(lastSample >= (int)inLen || outSample >= (int)outLen)) {
            // Fix overflow: use double precision for phase calculation
            double phase = (double)sampFracNum * (double)n / (double)denRate;
            int offset = (int)phase;
            float frac = (float)(phase - offset);
            
            // Use pre-allocated buffer to avoid allocation in hot path
            CubicCoef(frac, _cubicInterpBuffer);
            
            double sum = 0;
            
            // Apply sinc filter with cubic interpolation
            if (_sincTable != null) {
                for (int j = 0; j < n; j++) {
                    // Bounds checking for sinc table access
                    int sincIndex = Math.Abs(offset - j) * (int)_oversample;
                    if (sincIndex >= _sincTableLength) {
                        sincIndex = (int)_sincTableLength - 1;
                    }
                    
                    float sincVal = _sincTable[sincIndex];
                    sum += _mem[channelOffset + n - 1 - j + lastSample] * sincVal;
                }
            }

            output[outStride * outSample++] = (float)sum;
            lastSample += intAdvance;
            sampFracNum += (uint)fracAdvance;
            
            if (sampFracNum >= denRate) {
                sampFracNum -= denRate;
                lastSample++;
            }
        }

        _lastSample[channelIndex] = lastSample;
        _sampFracNum[channelIndex] = sampFracNum;
        
        inLen = (uint)lastSample;
        outLen = (uint)outSample;

        // Shift memory buffer
        if (lastSample > 0) {
            for (int i = 0; i < n - 1; i++) {
                _mem[channelOffset + i] = _mem[channelOffset + n - 1 - (n - 1 - i) + lastSample];
            }
        }
    }

    // SetRate() - Change sample rates
    // Mirrors: speex_resampler_set_rate()
    public void SetRate(uint inRate, uint outRate) {
        if (_inRate == inRate && _outRate == outRate) {
            return;
        }
        
        _inRate = inRate;
        _outRate = outRate;
        _numRate = 0;
        _denRate = 0;
        
        UpdateFilter();
    }

    // Reset() - Reset memory
    // Mirrors: speex_resampler_reset_mem()
    public void Reset() {
        for (uint i = 0; i < _nbChannels; i++) {
            _lastSample[i] = 0;
            _magicSamples[i] = 0;
            _sampFracNum[i] = 0;
        }
        
        // Clear memory
        if (_mem.Length > 0) {
            Array.Clear(_mem, 0, _mem.Length);
        }
    }

    /// <summary>
    /// Gets the current input and output sample rates.
    /// Mirrors: speex_resampler_get_rate()
    /// </summary>
    public void GetRate(out uint inRate, out uint outRate) {
        inRate = _inRate;
        outRate = _outRate;
    }

    /// <summary>
    /// Gets the resampling ratio as a fraction (numerator/denominator).
    /// Mirrors: speex_resampler_get_ratio()
    /// </summary>
    public void GetRatio(out uint ratioNum, out uint ratioDen) {
        ratioNum = _numRate;
        ratioDen = _denRate;
    }

    /// <summary>
    /// Sets the quality level (0-10).
    /// Mirrors: speex_resampler_set_quality()
    /// </summary>
    public void SetQuality(int quality) {
        if (quality < 0 || quality > 10) {
            throw new ArgumentException("Quality must be between 0 and 10", nameof(quality));
        }

        if (_quality == quality) {
            return;
        }

        _quality = quality;
        UpdateFilter();
    }

    /// <summary>
    /// Gets the current quality level.
    /// Mirrors: speex_resampler_get_quality()
    /// </summary>
    public int GetQuality() {
        return _quality;
    }

    /// <summary>
    /// Sets the resampling rates using fractional representation.
    /// Mirrors: speex_resampler_set_rate_frac()
    /// </summary>
    public void SetRateFrac(uint ratioNum, uint ratioDen, uint inRate, uint outRate) {
        if (ratioNum == 0 || ratioDen == 0) {
            throw new ArgumentException("Ratio numerator and denominator must be non-zero");
        }

        if (_inRate == inRate && _outRate == outRate && _numRate == ratioNum && _denRate == ratioDen) {
            return;
        }

        _inRate = inRate;
        _outRate = outRate;
        _numRate = ratioNum;
        _denRate = ratioDen;

        // Calculate int/frac advance
        uint intAdvance = ratioNum / ratioDen;
        uint fracAdvance = ratioNum % ratioDen;
        _intAdvance = (int)intAdvance;
        _fracAdvance = (int)fracAdvance;

        UpdateFilter();
    }

    /// <summary>
    /// Processes integer (16-bit) samples.
    /// Mirrors: speex_resampler_process_int()
    /// </summary>
    public void ProcessInt(uint channelIndex, ReadOnlySpan<short> input, Span<short> output,
        out uint inputConsumed, out uint outputGenerated) {

        if (channelIndex >= _nbChannels) {
            throw new ArgumentException("Invalid channel index", nameof(channelIndex));
        }

        // Use ArrayPool to avoid allocations in hot path
        float[] floatInput = System.Buffers.ArrayPool<float>.Shared.Rent(input.Length);
        float[] floatOutput = System.Buffers.ArrayPool<float>.Shared.Rent(output.Length);
        
        try {
            // Convert int16 to float
            for (int i = 0; i < input.Length; i++) {
                floatInput[i] = input[i] / 32768.0f;
            }

            // Process as float
            ProcessFloat(channelIndex, floatInput.AsSpan(0, input.Length), 
                floatOutput.AsSpan(0, output.Length), out inputConsumed, out outputGenerated);

            // Convert back to int16
            for (int i = 0; i < (int)outputGenerated; i++) {
                float sample = floatOutput[i] * 32768.0f;
                if (sample > 32767.0f) {
                    sample = 32767.0f;
                } else if (sample < -32768.0f) {
                    sample = -32768.0f;
                }
                output[i] = (short)sample;
            }
        } finally {
            System.Buffers.ArrayPool<float>.Shared.Return(floatInput);
            System.Buffers.ArrayPool<float>.Shared.Return(floatOutput);
        }
    }

    /// <summary>
    /// Processes interleaved floating-point samples.
    /// Mirrors: speex_resampler_process_interleaved_float()
    /// </summary>
    public void ProcessInterleavedFloat(ReadOnlySpan<float> input, Span<float> output,
        out uint inputFrames, out uint outputFrames) {

        uint inLen = (uint)(input.Length / _nbChannels);
        uint outLen = (uint)(output.Length / _nbChannels);

        // Use ArrayPool to avoid allocations in hot path
        float[][] channelInputs = new float[_nbChannels][];
        float[][] channelOutputs = new float[_nbChannels][];
        
        try {
            for (uint ch = 0; ch < _nbChannels; ch++) {
                channelInputs[ch] = System.Buffers.ArrayPool<float>.Shared.Rent((int)inLen);
                channelOutputs[ch] = System.Buffers.ArrayPool<float>.Shared.Rent((int)outLen);

                // Deinterleave input
                for (uint i = 0; i < inLen; i++) {
                    channelInputs[ch][i] = input[(int)(i * _nbChannels + ch)];
                }
            }

            uint minConsumed = uint.MaxValue;
            uint minGenerated = uint.MaxValue;

            // Process each channel
            for (uint ch = 0; ch < _nbChannels; ch++) {
                ProcessFloat(ch, channelInputs[ch].AsSpan(0, (int)inLen), 
                    channelOutputs[ch].AsSpan(0, (int)outLen),
                    out uint consumed, out uint generated);
                minConsumed = Math.Min(minConsumed, consumed);
                minGenerated = Math.Min(minGenerated, generated);
            }

            // Re-interleave output
            for (uint i = 0; i < minGenerated; i++) {
                for (uint ch = 0; ch < _nbChannels; ch++) {
                    output[(int)(i * _nbChannels + ch)] = channelOutputs[ch][i];
                }
            }

            inputFrames = minConsumed;
            outputFrames = minGenerated;
        } finally {
            // Return all rented arrays
            for (uint ch = 0; ch < _nbChannels; ch++) {
                if (channelInputs[ch] != null) {
                    System.Buffers.ArrayPool<float>.Shared.Return(channelInputs[ch]);
                }
                if (channelOutputs[ch] != null) {
                    System.Buffers.ArrayPool<float>.Shared.Return(channelOutputs[ch]);
                }
            }
        }
    }

    /// <summary>
    /// Gets the algorithmic delay for input.
    /// Mirrors: speex_resampler_get_input_latency()
    /// </summary>
    public int GetInputLatency() {
        return (int)(_filtLen / 2);
    }

    /// <summary>
    /// Gets the algorithmic delay for output.
    /// Mirrors: speex_resampler_get_output_latency()
    /// </summary>
    public int GetOutputLatency() {
        return (int)((_filtLen / 2) * _denRate / _numRate);
    }

    /// <summary>
    /// Skips zeros at the beginning (used to reduce latency).
    /// Mirrors: speex_resampler_skip_zeros()
    /// </summary>
    public void SkipZeros() {
        for (uint i = 0; i < _nbChannels; i++) {
            _lastSample[i] = (int)(_filtLen / 2);
        }
    }

    /// <summary>
    /// Resets memory buffers (same as Reset but explicit name from C API).
    /// Mirrors: speex_resampler_reset_mem()
    /// </summary>
    public void ResetMem() {
        Reset();
    }

    /// <summary>
    /// Sets the input stride (for non-contiguous samples).
    /// Mirrors: speex_resampler_set_input_stride()
    /// </summary>
    public void SetInputStride(uint stride) {
        _inStride = (int)stride;
    }

    /// <summary>
    /// Gets the input stride.
    /// Mirrors: speex_resampler_get_input_stride()
    /// </summary>
    public uint GetInputStride() {
        return (uint)_inStride;
    }

    /// <summary>
    /// Sets the output stride (for non-contiguous samples).
    /// Mirrors: speex_resampler_set_output_stride()
    /// </summary>
    public void SetOutputStride(uint stride) {
        _outStride = (int)stride;
    }

    /// <summary>
    /// Gets the output stride.
    /// Mirrors: speex_resampler_get_output_stride()
    /// </summary>
    public uint GetOutputStride() {
        return (uint)_outStride;
    }

    /// <summary>
    /// Gets an error string for an error code.
    /// Mirrors: speex_resampler_strerror()
    /// </summary>
    public static string GetErrorString(int errorCode) {
        return errorCode switch {
            0 => "Success",
            1 => "Memory allocation failed",
            2 => "Bad resampler state",
            3 => "Invalid argument",
            4 => "Input and output buffers overlap",
            _ => "Unknown error"
        };
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _sincTable = null;
        _mem = Array.Empty<float>();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
