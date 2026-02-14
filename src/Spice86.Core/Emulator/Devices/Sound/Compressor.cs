// Port of Thomas Scott Stillwell's "Master Tom Compressor" JSFX effect
// Original license: GPL-2.0-or-later
//
// This is a simplified port of Thomas Scott Stillwell's "Master Tom
// Compressor" JSFX effect bundled with REAPER (just the RMS & feedforward path).
//
// Copyright 2006, Thomas Scott Stillwell
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this
// list of conditions and the following disclaimer.
//
// Redistributions in binary form must reproduce the above copyright notice,
// this list of conditions and the following disclaimer in the documentation
// and/or other materials provided with the distribution.
//
// The name of Thomas Scott Stillwell may not be used to endorse or promote
// products derived from this software without specific prior written
// permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
// POSSIBILITY OF SUCH DAMAGE.

namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Libs.Sound.Common;

using System;

/// <summary>
/// Dynamic-range reducing audio signal compressor to reduce the volume of loud sounds above a given threshold.
/// Implements RMS-based detection with feedforward compression path.
/// </summary>
public sealed class Compressor {
    // Conversion constants
    private const float LogToDb = 8.685889638065035f;  // 20.0 / log(10.0)
    private const float DbToLog = 0.1151292546497022f; // log(10.0) / 20.0
    private const float MillisInSecondF = 1000.0f;

    // Configuration parameters
    private float _sampleRateHz;
    private float _scaleIn;
    private float _scaleOut;
    private float _thresholdValue;
    private float _ratio;
    private float _attackCoeff;
    private float _releaseCoeff;
    private float _rmsCoeff;

    private float _compRatio;
    private float _runDb;
    private float _runSumSquares;
    private float _overDb;
    private float _runMaxDb;
    private float _maxOverDb;

    /// <summary>
    /// Initializes a new instance of the Compressor class.
    /// </summary>
    public Compressor() {
        Reset();
    }

    /// <summary>
    /// Configures the compressor with the specified parameters.
    /// </summary>
    /// <param name="sampleRateHz">Sample rate in Hz (must be positive)</param>
    /// <param name="zeroDbfsSampleValue">Sample value representing 0 dBFS (must be positive)</param>
    /// <param name="thresholdDb">Threshold in dB (e.g., -6.0)</param>
    /// <param name="ratio">Compression ratio (must be positive, e.g., 3.0 for 3:1)</param>
    /// <param name="attackTimeMs">Attack time in milliseconds (must be positive)</param>
    /// <param name="releaseTimeMs">Release time in milliseconds (must be positive)</param>
    /// <param name="rmsWindowMs">RMS window size in milliseconds (must be positive)</param>
    public void Configure(int sampleRateHz, float zeroDbfsSampleValue, float thresholdDb,
                          float ratio, float attackTimeMs, float releaseTimeMs, float rmsWindowMs) {
        if (sampleRateHz <= 0) {
            throw new ArgumentException("Sample rate must be positive", nameof(sampleRateHz));
        }
        if (zeroDbfsSampleValue <= 0.0f) {
            throw new ArgumentException("0 dBFS sample value must be positive", nameof(zeroDbfsSampleValue));
        }
        if (ratio <= 0.0f) {
            throw new ArgumentException("Ratio must be positive", nameof(ratio));
        }
        if (attackTimeMs <= 0.0f) {
            throw new ArgumentException("Attack time must be positive", nameof(attackTimeMs));
        }
        if (releaseTimeMs <= 0.0f) {
            throw new ArgumentException("Release time must be positive", nameof(releaseTimeMs));
        }
        if (rmsWindowMs <= 0.0f) {
            throw new ArgumentException("RMS window must be positive", nameof(rmsWindowMs));
        }

        _sampleRateHz = sampleRateHz;

        // Input scaling to normalize, output scaling to denormalize
        _scaleIn = 1.0f / zeroDbfsSampleValue;
        _scaleOut = zeroDbfsSampleValue;

        // Threshold in linear domain
        _thresholdValue = MathF.Exp(thresholdDb * DbToLog);
        _ratio = ratio;

        _attackCoeff = MathF.Exp(-1.0f / (attackTimeMs * _sampleRateHz));

        _releaseCoeff = MathF.Exp(-MillisInSecondF / (releaseTimeMs * _sampleRateHz));

        _rmsCoeff = MathF.Exp(-MillisInSecondF / (rmsWindowMs * _sampleRateHz));

        Reset();
    }

    /// <summary>
    /// Resets the compressor state to initial values.
    /// </summary>
    public void Reset() {
        _compRatio = 0.0f;
        _runDb = 0.0f;
        _runSumSquares = 0.0f;
        _overDb = 0.0f;
        _runMaxDb = 0.0f;
        _maxOverDb = 0.0f;
    }

    /// <summary>
    /// Processes a single audio frame through the compressor.
    /// </summary>
    /// <param name="input">Input audio frame</param>
    /// <returns>Compressed audio frame</returns>
    public AudioFrame Process(AudioFrame input) {
        // Scale input to normalized range
        float left = input.Left * _scaleIn;
        float right = input.Right * _scaleIn;

        // Calculate RMS using sum of squares with exponential averaging
        float sumSquares = (left * left) + (right * right);
        _runSumSquares = sumSquares + _rmsCoeff * (_runSumSquares - sumSquares);
        float det = MathF.Sqrt(Math.Max(0.0f, _runSumSquares));

        // Calculate how much signal exceeds threshold in dB
        _overDb = 2.08136898f * MathF.Log(det / _thresholdValue) * LogToDb;

        // Track maximum overshoot
        if (_overDb > _maxOverDb) {
            _maxOverDb = _overDb;
        }

        // Clamp to positive values only
        _overDb = Math.Max(0.0f, _overDb);

        // Apply attack/release envelope to overshoot
        _runDb = _overDb + (_runDb - _overDb) * (_overDb > _runDb ? _attackCoeff : _releaseCoeff);

        // Use the envelope-smoothed overshoot
        _overDb = _runDb;

        // Calculate compression ratio with knee (soft transition around threshold)
        const float RatioThresholdDb = 6.0f;
        _compRatio = 1.0f + _ratio * Math.Min(_overDb, RatioThresholdDb) / RatioThresholdDb;

        // Calculate gain reduction in dB
        float gainReductionDb = -_overDb * (_compRatio - 1.0f) / _compRatio;
        float gainReductionFactor = MathF.Exp(gainReductionDb * DbToLog);

        // Update running maximum with release
        _runMaxDb = _maxOverDb + _releaseCoeff * (_runMaxDb - _maxOverDb);
        _maxOverDb = _runMaxDb;

        // Apply gain reduction and scale back to output range
        float gainScalar = gainReductionFactor * _scaleOut;

        return new AudioFrame(
            left * gainScalar,
            right * gainScalar
        );
    }
}
