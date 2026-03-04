// SPDX-License-Identifier: BSD-3-Clause
/* Copyright (C) 2007-2008 Jean-Marc Valin
   Copyright (C) 2008      Thorvald Natvig
   C# Port: 2025 for Spice86 project

   Arbitrary resampling code - Faithful C# port from libspeexdsp/resample.c

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

namespace Bufdio.Spice86;

/// <summary>
/// Pure C# port of Speex high-quality audio resampler from libspeexdsp.
/// This is a faithful, minimal-deviation port of the floating-point C implementation.
/// Maintains exact algorithm fidelity to the original resample.c.
/// </summary>
public sealed class SpeexResamplerCSharp {
    // Error codes
    private const int RESAMPLER_ERR_SUCCESS = 0;
    private const int RESAMPLER_ERR_ALLOC_FAILED = 1;
    private const int RESAMPLER_ERR_BAD_STATE = 2;
    private const int RESAMPLER_ERR_INVALID_ARG = 3;
    private const int RESAMPLER_ERR_PTR_OVERLAP = 4;
    private const int RESAMPLER_ERR_OVERFLOW = 5;

    private const double M_PI = 3.14159265358979323846;

    // Kaiser window tables - exact from resample.c
    private static readonly double[] kaiser12_table = new double[68] {
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

    private static readonly double[] kaiser10_table = new double[36] {
        0.99537781, 1.00000000, 0.99537781, 0.98162644, 0.95908712, 0.92831446,
        0.89005583, 0.84522401, 0.79486424, 0.74011713, 0.68217934, 0.62226347,
        0.56155915, 0.50119680, 0.44221549, 0.38553619, 0.33194107, 0.28205962,
        0.23636152, 0.19515633, 0.15859932, 0.12670280, 0.09935205, 0.07632451,
        0.05731132, 0.04193980, 0.02979584, 0.02044510, 0.01345224, 0.00839739,
        0.00488951, 0.00257636, 0.00115101, 0.00035515, 0.00000000, 0.00000000
    };

    private static readonly double[] kaiser8_table = new double[36] {
        0.99635258, 1.00000000, 0.99635258, 0.98548012, 0.96759014, 0.94302200,
        0.91223751, 0.87580811, 0.83439927, 0.78875245, 0.73966538, 0.68797126,
        0.63451750, 0.58014482, 0.52566725, 0.47185369, 0.41941150, 0.36897272,
        0.32108304, 0.27619388, 0.23465776, 0.19672670, 0.16255380, 0.13219758,
        0.10562887, 0.08273982, 0.06335451, 0.04724088, 0.03412321, 0.02369490,
        0.01563093, 0.00959968, 0.00527363, 0.00233883, 0.00050000, 0.00000000
    };

    private static readonly double[] kaiser6_table = new double[36] {
        0.99733006, 1.00000000, 0.99733006, 0.98935595, 0.97618418, 0.95799003,
        0.93501423, 0.90755855, 0.87598009, 0.84068475, 0.80211977, 0.76076565,
        0.71712752, 0.67172623, 0.62508937, 0.57774224, 0.53019925, 0.48295561,
        0.43647969, 0.39120616, 0.34752997, 0.30580127, 0.26632152, 0.22934058,
        0.19505503, 0.16360756, 0.13508755, 0.10953262, 0.08693120, 0.06722600,
        0.05031820, 0.03607231, 0.02432151, 0.01487334, 0.00752000, 0.00000000
    };

    private sealed class FuncDef {
        public readonly double[] table;
        public readonly int oversample;

        public FuncDef(double[] table, int oversample) {
            this.table = table;
            this.oversample = oversample;
        }
    }

    private static readonly FuncDef KAISER12 = new(kaiser12_table, 64);
    private static readonly FuncDef KAISER10 = new(kaiser10_table, 32);
    private static readonly FuncDef KAISER8 = new(kaiser8_table, 32);
    private static readonly FuncDef KAISER6 = new(kaiser6_table, 32);

    private sealed class QualityMapping {
        public readonly int base_length;
        public readonly int oversample;
        public readonly float downsample_bandwidth;
        public readonly float upsample_bandwidth;
        public readonly FuncDef window_func;

        public QualityMapping(int base_length, int oversample, float downsample_bandwidth,
                            float upsample_bandwidth, FuncDef window_func) {
            this.base_length = base_length;
            this.oversample = oversample;
            this.downsample_bandwidth = downsample_bandwidth;
            this.upsample_bandwidth = upsample_bandwidth;
            this.window_func = window_func;
        }
    }

    private static readonly QualityMapping[] quality_map = new QualityMapping[11] {
        new(  8,  4, 0.830f, 0.860f, KAISER6),
        new( 16,  4, 0.850f, 0.880f, KAISER6),
        new( 32,  4, 0.882f, 0.910f, KAISER6),
        new( 48,  8, 0.895f, 0.917f, KAISER8),
        new( 64,  8, 0.921f, 0.940f, KAISER8),
        new( 80, 16, 0.922f, 0.940f, KAISER10),
        new( 96, 16, 0.940f, 0.945f, KAISER10),
        new(128, 16, 0.950f, 0.950f, KAISER10),
        new(160, 16, 0.960f, 0.960f, KAISER10),
        new(192, 32, 0.968f, 0.968f, KAISER12),
        new(256, 32, 0.975f, 0.975f, KAISER12)
    };

    // State
    private uint in_rate;
    private uint out_rate;
    private uint num_rate;
    private uint den_rate;
    private int quality;
    private readonly uint nb_channels;
    private uint filt_len;
    private uint mem_alloc_size;
    private const uint buffer_size = 160;
    private int int_advance;
    private int frac_advance;
    private float cutoff;
    private uint oversample;
    private int started;

    private int[] last_sample;
    private uint[] samp_frac_num;
    private uint[] magic_samples;
    private float[] mem;
    private float[] sinc_table;
    private uint sinc_table_length;
    private delegate int ResamplerFunc(uint channel_index, float[] inBuf, int inBufOffset, ref uint in_len, float[] outBuf, int outBufOffset, ref uint out_len, int outStride);
    private ResamplerFunc resampler_ptr;

    private int in_stride;
    private int out_stride;

    public SpeexResamplerCSharp(uint channels, uint in_rate_init, uint out_rate_init, int quality) {
        if (channels == 0 || channels > 256) {
            throw new ArgumentException("channels");
        }
        if (quality < 0 || quality > 10) {
            throw new ArgumentException("quality");
        }

        nb_channels = channels;
        this.in_rate = in_rate_init;
        this.out_rate = out_rate_init;
        this.quality = quality;
        this.num_rate = in_rate_init;
        this.den_rate = out_rate_init;

        last_sample = new int[channels];
        samp_frac_num = new uint[channels];
        magic_samples = new uint[channels];
        mem = Array.Empty<float>();
        sinc_table = Array.Empty<float>();
        sinc_table_length = 0;
        mem_alloc_size = 0;

        in_stride = 1;
        out_stride = 1;
        started = 0;
        resampler_ptr = resampler_basic_zero;

        if (update_filter() == RESAMPLER_ERR_SUCCESS) {
            // Filter updated successfully
        }
    }

    private static double compute_func(float x, FuncDef func) {
        float y = x * func.oversample;
        int ind = (int)Math.Floor(y);
        float frac = y - ind;

        double[] interp = new double[4];
        interp[3] = -0.1666666667 * frac + 0.1666666667 * (frac * frac * frac);
        interp[2] = frac + 0.5 * (frac * frac) - 0.5 * (frac * frac * frac);
        interp[0] = -0.3333333333 * frac + 0.5 * (frac * frac) - 0.1666666667 * (frac * frac * frac);
        interp[1] = 1.0 - interp[3] - interp[2] - interp[0];

        return interp[0] * func.table[ind] + interp[1] * func.table[ind + 1] +
               interp[2] * func.table[ind + 2] + interp[3] * func.table[ind + 3];
    }

    private static float sinc(float cutoff, float x, int N, FuncDef window_func) {
        float xx = x * cutoff;
        if (Math.Abs(x) < 1e-6) {
            return cutoff;
        } else if (Math.Abs(x) > 0.5f * N) {
            return 0;
        }
        return (float)(cutoff * Math.Sin(M_PI * xx) / (M_PI * xx) * compute_func(Math.Abs(2.0f * x / N), window_func));
    }

    private static void cubic_coef(float frac, float[] interp) {
        interp[0] = -0.16667f * frac + 0.16667f * frac * frac * frac;
        interp[1] = frac + 0.5f * frac * frac - 0.5f * frac * frac * frac;
        interp[3] = -0.33333f * frac + 0.5f * frac * frac - 0.16667f * frac * frac * frac;
        interp[2] = 1.0f - interp[0] - interp[1] - interp[3];
    }

    private int resampler_basic_direct_single(uint channel_index, float[] inBuf, int inBufOffset, ref uint in_len, float[] outBuf, int outBufOffset, ref uint out_len, int outStride) {
        int N = (int)filt_len;
        int out_sample = 0;
        int last_sample_val = last_sample[channel_index];
        uint samp_frac_num_val = samp_frac_num[channel_index];

        while (!(last_sample_val >= (int)in_len || out_sample >= (int)out_len)) {
            int sincIdx = (int)(samp_frac_num_val * N);
            double sum = 0;
            for (int j = 0; j < N; j++) {
                sum += sinc_table[sincIdx + j] * inBuf[inBufOffset + last_sample_val + j];
            }
            outBuf[outBufOffset + outStride * out_sample++] = (float)sum;

            last_sample_val += int_advance;
            samp_frac_num_val += (uint)frac_advance;
            if (samp_frac_num_val >= den_rate) {
                samp_frac_num_val -= den_rate;
                last_sample_val++;
            }
        }

        last_sample[channel_index] = last_sample_val;
        samp_frac_num[channel_index] = samp_frac_num_val;
        return out_sample;
    }

    private int resampler_basic_direct_double(uint channel_index, float[] inBuf, int inBufOffset, ref uint in_len, float[] outBuf, int outBufOffset, ref uint out_len, int outStride) {
        int N = (int)filt_len;
        int out_sample = 0;
        int last_sample_val = last_sample[channel_index];
        uint samp_frac_num_val = samp_frac_num[channel_index];

        while (!(last_sample_val >= (int)in_len || out_sample >= (int)out_len)) {
            int sincIdx = (int)(samp_frac_num_val * N);
            double sum = 0;
            for (int j = 0; j < N; j += 4) {
                sum += sinc_table[sincIdx + j] * inBuf[inBufOffset + last_sample_val + j];
                if (j + 1 < N) sum += sinc_table[sincIdx + j + 1] * inBuf[inBufOffset + last_sample_val + j + 1];
                if (j + 2 < N) sum += sinc_table[sincIdx + j + 2] * inBuf[inBufOffset + last_sample_val + j + 2];
                if (j + 3 < N) sum += sinc_table[sincIdx + j + 3] * inBuf[inBufOffset + last_sample_val + j + 3];
            }
            outBuf[outBufOffset + outStride * out_sample++] = (float)sum;

            last_sample_val += int_advance;
            samp_frac_num_val += (uint)frac_advance;
            if (samp_frac_num_val >= den_rate) {
                samp_frac_num_val -= den_rate;
                last_sample_val++;
            }
        }

        last_sample[channel_index] = last_sample_val;
        samp_frac_num[channel_index] = samp_frac_num_val;
        return out_sample;
    }

    private int resampler_basic_interpolate_single(uint channel_index, float[] inBuf, int inBufOffset, ref uint in_len, float[] outBuf, int outBufOffset, ref uint out_len, int outStride) {
        int N = (int)filt_len;
        int out_sample = 0;
        int last_sample_val = last_sample[channel_index];
        uint samp_frac_num_val = samp_frac_num[channel_index];

        while (!(last_sample_val >= (int)in_len || out_sample >= (int)out_len)) {
            int offset = (int)(samp_frac_num_val * oversample / den_rate);
            float frac = ((float)((samp_frac_num_val * oversample) % den_rate)) / den_rate;
            float[] interp = new float[4];
            cubic_coef(frac, interp);

            double accum0 = 0, accum1 = 0, accum2 = 0, accum3 = 0;
            for (int j = 0; j < N; j++) {
                float curr = inBuf[inBufOffset + last_sample_val + j];
                int idx = 4 + (j + 1) * (int)oversample - offset;
                accum0 += curr * sinc_table[idx - 2];
                accum1 += curr * sinc_table[idx - 1];
                accum2 += curr * sinc_table[idx];
                accum3 += curr * sinc_table[idx + 1];
            }

            double sum = interp[0] * accum0 + interp[1] * accum1 + interp[2] * accum2 + interp[3] * accum3;
            outBuf[outBufOffset + outStride * out_sample++] = (float)sum;

            last_sample_val += int_advance;
            samp_frac_num_val += (uint)frac_advance;
            if (samp_frac_num_val >= den_rate) {
                samp_frac_num_val -= den_rate;
                last_sample_val++;
            }
        }

        last_sample[channel_index] = last_sample_val;
        samp_frac_num[channel_index] = samp_frac_num_val;
        return out_sample;
    }

    private int resampler_basic_interpolate_double(uint channel_index, float[] inBuf, int inBufOffset, ref uint in_len, float[] outBuf, int outBufOffset, ref uint out_len, int outStride) {
        int N = (int)filt_len;
        int out_sample = 0;
        int last_sample_val = last_sample[channel_index];
        uint samp_frac_num_val = samp_frac_num[channel_index];

        while (!(last_sample_val >= (int)in_len || out_sample >= (int)out_len)) {
            int offset = (int)(samp_frac_num_val * oversample / den_rate);
            float frac = ((float)((samp_frac_num_val * oversample) % den_rate)) / den_rate;
            float[] interp = new float[4];
            cubic_coef(frac, interp);

            double accum0 = 0, accum1 = 0, accum2 = 0, accum3 = 0;
            for (int j = 0; j < N; j++) {
                double curr = inBuf[inBufOffset + last_sample_val + j];
                int idx = 4 + (j + 1) * (int)oversample - offset;
                accum0 += curr * sinc_table[idx - 2];
                accum1 += curr * sinc_table[idx - 1];
                accum2 += curr * sinc_table[idx];
                accum3 += curr * sinc_table[idx + 1];
            }

            double sum = interp[0] * accum0 + interp[1] * accum1 + interp[2] * accum2 + interp[3] * accum3;
            outBuf[outBufOffset + outStride * out_sample++] = (float)sum;

            last_sample_val += int_advance;
            samp_frac_num_val += (uint)frac_advance;
            if (samp_frac_num_val >= den_rate) {
                samp_frac_num_val -= den_rate;
                last_sample_val++;
            }
        }

        last_sample[channel_index] = last_sample_val;
        samp_frac_num[channel_index] = samp_frac_num_val;
        return out_sample;
    }

    private int resampler_basic_zero(uint channel_index, float[] inBuf, int inBufOffset, ref uint in_len, float[] outBuf, int outBufOffset, ref uint out_len, int outStride) {
        int out_sample = 0;
        int last_sample_val = last_sample[channel_index];
        uint samp_frac_num_val = samp_frac_num[channel_index];

        while (!(last_sample_val >= (int)in_len || out_sample >= (int)out_len)) {
            outBuf[outBufOffset + outStride * out_sample++] = 0;

            last_sample_val += int_advance;
            samp_frac_num_val += (uint)frac_advance;
            if (samp_frac_num_val >= den_rate) {
                samp_frac_num_val -= den_rate;
                last_sample_val++;
            }
        }

        last_sample[channel_index] = last_sample_val;
        samp_frac_num[channel_index] = samp_frac_num_val;
        return out_sample;
    }

    private static int multiply_frac(out uint result, uint value, uint num, uint den) {
        result = 0;
        uint major = value / den;
        uint remain = value % den;

        if (remain > uint.MaxValue / num || major > uint.MaxValue / num ||
            major * num > uint.MaxValue - remain * num / den) {
            return RESAMPLER_ERR_OVERFLOW;
        }
        result = remain * num / den + major * num;
        return RESAMPLER_ERR_SUCCESS;
    }

    private int update_filter() {
        uint old_length = filt_len;
        uint old_alloc_size = mem_alloc_size;
        bool use_direct;
        uint min_sinc_table_length;
        uint min_alloc_size;

        int_advance = (int)(num_rate / den_rate);
        frac_advance = (int)(num_rate % den_rate);
        oversample = (uint)quality_map[quality].oversample;
        filt_len = (uint)quality_map[quality].base_length;

        if (num_rate > den_rate) {
            cutoff = quality_map[quality].downsample_bandwidth * den_rate / (float)num_rate;
            if (multiply_frac(out filt_len, filt_len, num_rate, den_rate) != RESAMPLER_ERR_SUCCESS) {
                goto fail;
            }
            filt_len = ((filt_len - 1) & (~0x7u)) + 8;
            if (2 * den_rate < num_rate) oversample >>= 1;
            if (4 * den_rate < num_rate) oversample >>= 1;
            if (8 * den_rate < num_rate) oversample >>= 1;
            if (16 * den_rate < num_rate) oversample >>= 1;
            if (oversample < 1) oversample = 1;
        } else {
            cutoff = quality_map[quality].upsample_bandwidth;
        }

        use_direct = filt_len * den_rate <= filt_len * oversample + 8 &&
                     int.MaxValue / sizeof_float / den_rate >= filt_len;

        if (use_direct) {
            min_sinc_table_length = filt_len * den_rate;
        } else {
            if ((int.MaxValue / sizeof_float - 8) / oversample < filt_len) {
                goto fail;
            }
            min_sinc_table_length = filt_len * oversample + 8;
        }

        if (sinc_table_length < min_sinc_table_length) {
            sinc_table = new float[min_sinc_table_length];
            sinc_table_length = min_sinc_table_length;
        }

        if (use_direct) {
            for (uint i = 0; i < den_rate; i++) {
                for (int j = 0; j < (int)filt_len; j++) {
                    sinc_table[i * filt_len + (uint)j] =
                        sinc(cutoff, ((j - (int)filt_len / 2 + 1) - ((float)i) / den_rate), (int)filt_len,
                             quality_map[quality].window_func);
                }
            }
            if (quality > 8) {
                resampler_ptr = resampler_basic_direct_double;
            } else {
                resampler_ptr = resampler_basic_direct_single;
            }
        } else {
            for (int i = -4; i < (int)(oversample * filt_len + 4); i++) {
                sinc_table[i + 4] = sinc(cutoff, i / (float)oversample - filt_len / 2.0f, (int)filt_len,
                                        quality_map[quality].window_func);
            }
            if (quality > 8) {
                resampler_ptr = resampler_basic_interpolate_double;
            } else {
                resampler_ptr = resampler_basic_interpolate_single;
            }
        }

        min_alloc_size = filt_len - 1 + buffer_size;
        if (min_alloc_size > mem_alloc_size) {
            if (int.MaxValue / sizeof_float / nb_channels < min_alloc_size) {
                goto fail;
            }
            mem = new float[nb_channels * min_alloc_size * 2];
            mem_alloc_size = min_alloc_size;
        }

        if (started == 0) {
            for (uint i = 0; i < nb_channels * mem_alloc_size; i++) {
                mem[i] = 0;
            }
        } else if (filt_len > old_length) {
            for (uint i = nb_channels; i-- > 0;) {
                uint j;
                uint olen = old_length;
                uint start = i * mem_alloc_size;
                uint magic = magic_samples[i];

                olen = old_length + 2 * magic;
                for (j = old_length - 1 + magic; j-- > 0;) {
                    mem[start + j + magic] = mem[i * old_alloc_size + j];
                }
                for (j = 0; j < magic; j++) {
                    mem[start + j] = 0;
                }
                magic_samples[i] = 0;

                if (filt_len > olen) {
                    for (j = 0; j < olen - 1; j++) {
                        mem[start + (filt_len - 2 - j)] = mem[start + (olen - 2 - j)];
                    }
                    for (; j < filt_len - 1; j++) {
                        mem[start + (filt_len - 2 - j)] = 0;
                    }
                    last_sample[i] += (int)((filt_len - olen) / 2);
                } else {
                    magic = (olen - filt_len) / 2;
                    for (j = 0; j < filt_len - 1 + magic; j++) {
                        mem[start + j] = mem[start + j + magic];
                    }
                    magic_samples[i] = magic;
                }
            }
        } else if (filt_len < old_length) {
            for (uint i = 0; i < nb_channels; i++) {
                uint j;
                uint old_magic = magic_samples[i];
                magic_samples[i] = (old_length - filt_len) / 2;

                for (j = 0; j < filt_len - 1 + magic_samples[i] + old_magic; j++) {
                    mem[i * mem_alloc_size + j] = mem[i * mem_alloc_size + j + magic_samples[i]];
                }
                magic_samples[i] += old_magic;
            }
        }

        return RESAMPLER_ERR_SUCCESS;

fail:
        resampler_ptr = resampler_basic_zero;
        filt_len = old_length;
        return RESAMPLER_ERR_ALLOC_FAILED;
    }

    /// <summary>
    /// Process native resampling - matches speex_resampler_process_native from resample.c
    /// </summary>
    private int speex_resampler_process_native(uint channel_index, ref uint in_len, float[] outBuf, int outOffset, ref uint out_len) {
        int N = (int)filt_len;
        int memOffset = (int)(channel_index * mem_alloc_size);
        
        started = 1;
        
        // Call the right resampler through the function ptr
        int out_sample = resampler_ptr(channel_index, mem, memOffset, ref in_len, outBuf, outOffset, ref out_len, out_stride);
        
        if (last_sample[channel_index] < (int)in_len) {
            in_len = (uint)last_sample[channel_index];
        }
        out_len = (uint)out_sample;
        last_sample[channel_index] -= (int)in_len;
        
        uint ilen = in_len;
        
        // Shift memory
        for (int j = 0; j < N - 1; j++) {
            mem[memOffset + j] = mem[memOffset + (int)ilen + j];
        }
        
        return RESAMPLER_ERR_SUCCESS;
    }

    /// <summary>
    /// Handle magic samples - matches speex_resampler_magic from resample.c
    /// </summary>
    private int speex_resampler_magic(uint channel_index, float[] outBuf, ref int outOffset, uint out_len) {
        uint tmp_in_len = magic_samples[channel_index];
        int memOffset = (int)(channel_index * mem_alloc_size);
        int N = (int)filt_len;
        
        uint out_len_temp = out_len;
        speex_resampler_process_native(channel_index, ref tmp_in_len, outBuf, outOffset, ref out_len_temp);
        
        magic_samples[channel_index] -= tmp_in_len;
        
        // If we couldn't process all "magic" input samples, save the rest for next time
        if (magic_samples[channel_index] != 0) {
            for (uint i = 0; i < magic_samples[channel_index]; i++) {
                mem[memOffset + N - 1 + (int)i] = mem[memOffset + N - 1 + (int)tmp_in_len + (int)i];
            }
        }
        
        outOffset += (int)out_len_temp * out_stride;
        return (int)out_len_temp;
    }

    /// <summary>
    /// Process float samples for a single channel - matches speex_resampler_process_float from resample.c
    /// </summary>
    public int ProcessFloat(uint channel_index, ReadOnlySpan<float> input, int inputOffset,
        ref uint in_len, Span<float> output, int outputOffset, ref uint out_len) {
        
        if (channel_index >= nb_channels) {
            return RESAMPLER_ERR_INVALID_ARG;
        }
        
        int memOffset = (int)(channel_index * mem_alloc_size);
        int filt_offs = (int)filt_len - 1;
        uint xlen = mem_alloc_size - (uint)filt_offs;
        int istride = in_stride;
        
        uint ilen = in_len;
        uint olen = out_len;
        
        int currentOutOffset = outputOffset;
        int currentInOffset = inputOffset;
        
        if (magic_samples[channel_index] != 0) {
            float[] tempOut = new float[output.Length];
            output.CopyTo(tempOut);
            olen -= (uint)speex_resampler_magic(channel_index, tempOut, ref currentOutOffset, olen);
            tempOut.AsSpan().CopyTo(output);
        }
        
        if (magic_samples[channel_index] == 0) {
            while (ilen > 0 && olen > 0) {
                uint ichunk = (ilen > xlen) ? xlen : ilen;
                uint ochunk = olen;
                
                // Copy input to memory buffer
                if (input.Length > 0) {
                    for (int j = 0; j < (int)ichunk; j++) {
                        int srcIdx = currentInOffset + j * istride;
                        if (srcIdx < input.Length) {
                            mem[memOffset + filt_offs + j] = input[srcIdx];
                        } else {
                            mem[memOffset + filt_offs + j] = 0;
                        }
                    }
                } else {
                    for (int j = 0; j < (int)ichunk; j++) {
                        mem[memOffset + filt_offs + j] = 0;
                    }
                }
                
                // Prepare temp output buffer at correct offset
                float[] tempOut = new float[output.Length];
                for (int i = 0; i < output.Length; i++) {
                    tempOut[i] = output[i];
                }
                
                speex_resampler_process_native(channel_index, ref ichunk, tempOut, currentOutOffset, ref ochunk);
                
                // Copy back to output
                for (int i = 0; i < tempOut.Length && i < output.Length; i++) {
                    output[i] = tempOut[i];
                }
                
                ilen -= ichunk;
                olen -= ochunk;
                currentOutOffset += (int)ochunk * out_stride;
                if (input.Length > 0) {
                    currentInOffset += (int)ichunk * istride;
                }
            }
        }
        
        in_len -= ilen;
        out_len -= olen;
        
        return resampler_ptr == resampler_basic_zero ? RESAMPLER_ERR_ALLOC_FAILED : RESAMPLER_ERR_SUCCESS;
    }

    /// <summary>
    /// Process interleaved float samples - matches speex_resampler_process_interleaved_float from resample.c
    /// </summary>
    public int ProcessInterleavedFloat(ReadOnlySpan<float> input, ref uint in_len,
        Span<float> output, ref uint out_len) {
        
        int istride_save = in_stride;
        int ostride_save = out_stride;
        uint bak_out_len = out_len;
        uint bak_in_len = in_len;
        
        in_stride = (int)nb_channels;
        out_stride = (int)nb_channels;
        
        for (uint i = 0; i < nb_channels; i++) {
            out_len = bak_out_len;
            in_len = bak_in_len;
            
            if (input.Length > 0) {
                ProcessFloat(i, input, (int)i, ref in_len, output, (int)i, ref out_len);
            } else {
                ProcessFloat(i, ReadOnlySpan<float>.Empty, 0, ref in_len, output, (int)i, ref out_len);
            }
        }
        
        in_stride = istride_save;
        out_stride = ostride_save;
        
        return resampler_ptr == resampler_basic_zero ? RESAMPLER_ERR_ALLOC_FAILED : RESAMPLER_ERR_SUCCESS;
    }

    /// <summary>
    /// Simplified interface for processing interleaved float data.
    /// Takes input/output spans and returns consumed/produced frame counts.
    /// </summary>
    public void ProcessInterleavedFloat(ReadOnlySpan<float> input, Span<float> output,
        out uint inputFramesConsumed, out uint outputFramesProduced) {
        
        if (nb_channels == 0) {
            inputFramesConsumed = 0;
            outputFramesProduced = 0;
            return;
        }

        uint inFrames = (uint)(input.Length / nb_channels);
        uint outFrames = (uint)(output.Length / nb_channels);
        
        if (inFrames == 0 || outFrames == 0) {
            inputFramesConsumed = 0;
            outputFramesProduced = 0;
            return;
        }

        uint in_len = inFrames;
        uint out_len = outFrames;
        
        ProcessInterleavedFloat(input, ref in_len, output, ref out_len);
        
        // in_len and out_len now contain the amount consumed/produced
        inputFramesConsumed = in_len;
        outputFramesProduced = out_len;
    }

    public void SetRate(uint in_rate_new, uint out_rate_new) {
        in_rate = in_rate_new;
        out_rate = out_rate_new;
        num_rate = in_rate_new;
        den_rate = out_rate_new;

        uint a = num_rate;
        uint b = den_rate;
        while (b != 0) {
            uint t = a % b;
            a = b;
            b = t;
        }
        num_rate /= a;
        den_rate /= a;

        update_filter();
    }

    public void Reset() {
        for (uint i = 0; i < nb_channels; i++) {
            last_sample[i] = 0;
            samp_frac_num[i] = 0;
            magic_samples[i] = 0;
        }
        for (uint i = 0; i < nb_channels * mem_alloc_size; i++) {
            mem[i] = 0;
        }
        started = 0;
    }

    public void SkipZeros() {
        for (uint i = 0; i < nb_channels; i++) {
            last_sample[i] = (int)(filt_len / 2);
        }
        started = 1;
    }

    public bool IsInitialized => true;

    public void GetRatio(out uint num, out uint den) {
        num = num_rate;
        den = den_rate;
    }

    public int GetInputLatency() {
        return (int)(filt_len / 2);
    }

    public int GetOutputLatency() {
        return (int)(((filt_len / 2) * den_rate + (num_rate >> 1)) / num_rate);
    }

    private const int sizeof_float = 4;
}
