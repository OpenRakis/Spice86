namespace Bufdio.Spice86;

using Bufdio.Spice86.Bindings.Speex;
using System;

/// <summary>
/// High-quality audio resampler using the Speex resampling library.
/// Mirrors DOSBox Staging's Speex resampler integration.
/// Reference: libspeexdsp resampler API
/// </summary>
public sealed class SpeexResampler : IDisposable {
    private IntPtr _resamplerState;
    private readonly uint _channels;
    private uint _inputRate;
    private uint _outputRate;
    private bool _disposed;

    /// <summary>
    /// Gets whether the Speex resampler is initialized and ready to use.
    /// </summary>
    public bool IsInitialized => _resamplerState != IntPtr.Zero;

    /// <summary>
    /// Gets the number of channels configured for this resampler.
    /// </summary>
    public uint Channels => _channels;

    /// <summary>
    /// Gets the current input sample rate in Hz.
    /// </summary>
    public uint InputRate => _inputRate;

    /// <summary>
    /// Gets the current output sample rate in Hz.
    /// </summary>
    public uint OutputRate => _outputRate;

    /// <summary>
    /// Initializes a new Speex resampler with the specified configuration.
    /// </summary>
    /// <param name="channels">Number of audio channels (1 for mono, 2 for stereo)</param>
    /// <param name="inputRate">Input sample rate in Hz</param>
    /// <param name="outputRate">Output sample rate in Hz</param>
    /// <param name="quality">Resampler quality setting (trade-off between speed and quality)</param>
    /// <exception cref="InvalidOperationException">Thrown when resampler initialization fails</exception>
    public SpeexResampler(uint channels, uint inputRate, uint outputRate, SpeexResamplerQuality quality = SpeexResamplerQuality.Medium) {
        _channels = channels;
        _inputRate = inputRate;
        _outputRate = outputRate;

        _resamplerState = NativeMethods.SpeexResamplerInit(channels, inputRate, outputRate, quality, out SpeexError error);

        if (error != SpeexError.Success) {
            throw new InvalidOperationException($"Failed to initialize Speex resampler: {error}");
        }

        if (_resamplerState == IntPtr.Zero) {
            throw new InvalidOperationException("Speex resampler initialization returned null pointer");
        }
    }

    /// <summary>
    /// Changes the input and output sample rates.
    /// Mirrors DOSBox SetRate() usage for dynamic rate changes.
    /// </summary>
    /// <param name="inputRate">New input sample rate in Hz</param>
    /// <param name="outputRate">New output sample rate in Hz</param>
    /// <exception cref="InvalidOperationException">Thrown when rate change fails or resampler is not initialized</exception>
    public void SetRate(uint inputRate, uint outputRate) {
        if (!IsInitialized) {
            throw new InvalidOperationException("Cannot set rate on uninitialized resampler");
        }

        SpeexError error = NativeMethods.SpeexResamplerSetRate(_resamplerState, inputRate, outputRate);
        if (error != SpeexError.Success) {
            throw new InvalidOperationException($"Failed to set Speex resampler rate: {error}");
        }

        _inputRate = inputRate;
        _outputRate = outputRate;
    }

    /// <summary>
    /// Gets the current resampling ratio as a fraction.
    /// </summary>
    /// <param name="ratioNumerator">Numerator of the resampling ratio</param>
    /// <param name="ratioDenominator">Denominator of the resampling ratio</param>
    public void GetRatio(out uint ratioNumerator, out uint ratioDenominator) {
        if (!IsInitialized) {
            throw new InvalidOperationException("Cannot get ratio from uninitialized resampler");
        }

        NativeMethods.SpeexResamplerGetRatio(_resamplerState, out ratioNumerator, out ratioDenominator);
    }

    /// <summary>
    /// Processes audio samples through the resampler.
    /// Mirrors DOSBox process_float() usage for stereo interleaved samples.
    /// </summary>
    /// <param name="channelIndex">Channel index (0 for mono/left, 1 for right in stereo)</param>
    /// <param name="input">Input audio samples</param>
    /// <param name="output">Output buffer for resampled audio</param>
    /// <param name="inputConsumed">Number of input samples consumed</param>
    /// <param name="outputGenerated">Number of output samples generated</param>
    /// <exception cref="InvalidOperationException">Thrown when resampling fails or resampler is not initialized</exception>
    public void ProcessFloat(uint channelIndex, ReadOnlySpan<float> input, Span<float> output, out uint inputConsumed, out uint outputGenerated) {
        if (!IsInitialized) {
            throw new InvalidOperationException("Cannot process samples with uninitialized resampler");
        }

        uint inputLength = (uint)input.Length;
        uint outputLength = (uint)output.Length;

        SpeexError error = NativeMethods.SpeexResamplerProcessFloat(
            _resamplerState,
            channelIndex,
            input,
            ref inputLength,
            output,
            ref outputLength);

        if (error != SpeexError.Success) {
            throw new InvalidOperationException($"Speex resampler processing failed: {error}");
        }

        inputConsumed = inputLength;
        outputGenerated = outputLength;
    }

    /// <summary>
    /// Skips zeros in the resampler buffer.
    /// Used to avoid processing silence at the beginning of a stream.
    /// </summary>
    public void SkipZeros() {
        if (!IsInitialized) {
            throw new InvalidOperationException("Cannot skip zeros on uninitialized resampler");
        }

        SpeexError error = NativeMethods.SpeexResamplerSkipZeros(_resamplerState);
        if (error != SpeexError.Success) {
            throw new InvalidOperationException($"Failed to skip zeros in Speex resampler: {error}");
        }
    }

    /// <summary>
    /// Resets the resampler's internal memory/state.
    /// Mirrors DOSBox reset usage when switching audio streams.
    /// </summary>
    public void Reset() {
        if (!IsInitialized) {
            throw new InvalidOperationException("Cannot reset uninitialized resampler");
        }

        SpeexError error = NativeMethods.SpeexResamplerReset(_resamplerState);
        if (error != SpeexError.Success) {
            throw new InvalidOperationException($"Failed to reset Speex resampler: {error}");
        }
    }

    /// <summary>
    /// Disposes the Speex resampler and frees native resources.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        if (IsInitialized) {
            NativeMethods.SpeexResamplerDestroy(_resamplerState);
            _resamplerState = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~SpeexResampler() {
        Dispose();
    }
}
