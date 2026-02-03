namespace Spice86.Core.Emulator.Devices.Sound;

using System;

/// <summary>
/// Dual chorus engine for TAL-Chorus effect.
/// Manages two independent chorus lines (left/right) for stereo chorus processing.
/// </summary>
/// <remarks>
/// Ported from DOSBox Staging: /src/libs/tal-chorus/ChorusEngine.h
/// 
/// Part of TAL-NoiseMaker by Patrick Kunz
/// Copyright (c) 2005-2010 Patrick Kunz, TAL - Togu Audio Line, Inc.
/// Licensed under GNU General Public License v2.0
/// http://kunz.corrupt.ch
/// 
/// DOSBox Configuration:
/// - Chorus1 enabled (left/right pair)
/// - Chorus2 disabled (left/right pair)
/// 
/// Each chorus pair processes stereo signals independently with different LFO phases
/// to create a wider, more natural stereo chorus effect.
/// </remarks>
public sealed class ChorusEngine {
    private Chorus? _chorus1L;
    private Chorus? _chorus1R;
    private Chorus? _chorus2L;
    private Chorus? _chorus2R;

    private readonly DCBlock _dcBlock1L = new();
    private readonly DCBlock _dcBlock1R = new();
    private readonly DCBlock _dcBlock2L = new();
    private readonly DCBlock _dcBlock2R = new();

    private bool _isChorus1Enabled;
    private bool _isChorus2Enabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChorusEngine"/> class.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    public ChorusEngine(float sampleRate) {
        SetUpChorus(sampleRate);
    }

    /// <summary>
    /// Changes the sample rate and reinitializes chorus lines.
    /// </summary>
    /// <param name="sampleRate">New sample rate in Hz.</param>
    public void SetSampleRate(float sampleRate) {
        SetUpChorus(sampleRate);
        SetEnablesChorus(false, false);
    }

    /// <summary>
    /// Enables or disables chorus lines.
    /// </summary>
    /// <param name="isChorus1Enabled">Enable Chorus1 (left/right pair).</param>
    /// <param name="isChorus2Enabled">Enable Chorus2 (left/right pair).</param>
    /// <remarks>
    /// DOSBox Staging configuration: Chorus1 enabled, Chorus2 disabled.
    /// See mixer.cpp:146-147.
    /// </remarks>
    public void SetEnablesChorus(bool isChorus1Enabled, bool isChorus2Enabled) {
        _isChorus1Enabled = isChorus1Enabled;
        _isChorus2Enabled = isChorus2Enabled;
    }

    /// <summary>
    /// Initializes or reinitializes all chorus lines with given sample rate.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <remarks>
    /// Chorus parameters from DOSBox ChorusEngine.h:74-77:
    /// - Chorus1L: phase=1.0, rate=0.5Hz, delayTime=7.0ms
    /// - Chorus1R: phase=0.0, rate=0.5Hz, delayTime=7.0ms
    /// - Chorus2L: phase=0.0, rate=0.83Hz, delayTime=7.0ms
    /// - Chorus2R: phase=1.0, rate=0.83Hz, delayTime=7.0ms
    /// </remarks>
    private void SetUpChorus(float sampleRate) {
        // Create new chorus instances with DOSBox parameters
        _chorus1L = new Chorus(sampleRate, phase: 1.0f, rate: 0.5f, delayTime: 7.0f);
        _chorus1R = new Chorus(sampleRate, phase: 0.0f, rate: 0.5f, delayTime: 7.0f);
        _chorus2L = new Chorus(sampleRate, phase: 0.0f, rate: 0.83f, delayTime: 7.0f);
        _chorus2R = new Chorus(sampleRate, phase: 1.0f, rate: 0.83f, delayTime: 7.0f);
    }

    /// <summary>
    /// Processes a stereo sample pair through the chorus effect.
    /// Updates samples in-place.
    /// </summary>
    /// <param name="sampleL">Left channel sample (modified in-place).</param>
    /// <param name="sampleR">Right channel sample (modified in-place).</param>
    /// <remarks>
    /// 1. Process enabled chorus lines
    /// 2. Apply DC blocking to each output
    /// 3. Mix wet signal back with dry input (1.4x wet gain)
    /// 4. Update input samples in-place with wet+dry mix
    /// </remarks>
    public void Process(ref float sampleL, ref float sampleR) {
        float resultL = 0.0f;
        float resultR = 0.0f;

        // Process Chorus1 if enabled
        if (_isChorus1Enabled && _chorus1L != null && _chorus1R != null) {
            resultL += _chorus1L.Process(sampleL);
            resultR += _chorus1R.Process(sampleR);
            _dcBlock1L.Tick(ref resultL, 0.01f);
            _dcBlock1R.Tick(ref resultR, 0.01f);
        }

        // Process Chorus2 if enabled
        if (_isChorus2Enabled && _chorus2L != null && _chorus2R != null) {
            resultL += _chorus2L.Process(sampleL);
            resultR += _chorus2R.Process(sampleR);
            _dcBlock2L.Tick(ref resultL, 0.01f);
            _dcBlock2R.Tick(ref resultR, 0.01f);
        }

        // Mix wet (chorus output) with dry (original input)
        // 1.4x gain on wet signal for prominence
        sampleL += resultL * 1.4f;
        sampleR += resultR * 1.4f;
    }
}
