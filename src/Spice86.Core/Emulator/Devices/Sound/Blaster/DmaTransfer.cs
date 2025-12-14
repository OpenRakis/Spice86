// SPDX-License-Identifier: GPL-2.0-or-later
// DMA transfer logic ported from DOSBox Staging
// Reference: src/hardware/audio/soundblaster.cpp play_dma_transfer() and helpers
// This file mirrors DOSBox structure for side-by-side debugging

namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Spice86.Libs.Sound.Common;
using System;
using System.Collections.Generic;

/// <summary>
/// Helper class for DMA transfer operations.
/// Mirrors DOSBox Staging's play_dma_transfer and related functions.
/// </summary>
public static class DmaTransferHelpers {
    /// <summary>
    /// Converts 8-bit unsigned sample to float (mirrors lut_u8to16 lookup in DOSBox).
    /// </summary>
    public static float ToFloatU8(byte sample) {
        return LookupTables.U8To16[sample];
    }
    
    /// <summary>
    /// Converts 8-bit signed sample to float (mirrors lut_s8to16 lookup in DOSBox).
    /// </summary>
    public static float ToFloatS8(sbyte sample) {
        // Lookup table is indexed by unsigned byte (0-255), convert signed to unsigned index
        return LookupTables.S8To16[(byte)sample];
    }
    
    /// <summary>
    /// Converts 16-bit signed sample to float.
    /// </summary>
    public static float ToFloatS16(short sample) {
        return sample;
    }
    
    /// <summary>
    /// Converts 16-bit unsigned sample to float (with DOS silent sample adjustment).
    /// </summary>
    public static float ToFloatU16(ushort sample) {
        // DOS silent sample for unsigned 16-bit is 0x8000
        return (short)(sample - 0x8000);
    }
    
    /// <summary>
    /// Processes samples into AudioFrames for mono playback.
    /// Mirrors DOSBox maybe_silence&lt;FrameType::Mono&gt; template.
    /// Returns silent frames if speaker is off or during warmup period.
    /// </summary>
    /// <param name="samples">Pointer to sample data</param>
    /// <param name="numSamples">Number of samples to process</param>
    /// <param name="speakerEnabled">Whether speaker output is enabled</param>
    /// <param name="warmupRemainingMs">Remaining warmup time in milliseconds</param>
    /// <param name="swapChannels">Whether to swap left/right (for SB Pro1/Pro2)</param>
    /// <param name="signed">Whether samples are signed</param>
    /// <returns>List of audio frames</returns>
    public static List<AudioFrame> MaybeSilenceMono(
        byte[] samples,
        uint numSamples,
        bool speakerEnabled,
        ref int warmupRemainingMs,
        bool swapChannels,
        bool signed) {
        
        List<AudioFrame> frames = new List<AudioFrame>((int)numSamples);
        
        // Return silent frames if still in warmup
        if (warmupRemainingMs > 0) {
            warmupRemainingMs--;
            for (int i = 0; i < numSamples; i++) {
                frames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return frames;
        }
        
        // Return silent frames if speaker disabled
        if (!speakerEnabled) {
            for (int i = 0; i < numSamples; i++) {
                frames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return frames;
        }
        
        // Process samples into AudioFrames (mono = same value for left and right)
        for (int i = 0; i < numSamples; i++) {
            float value = signed ? ToFloatS8((sbyte)samples[i]) : ToFloatU8(samples[i]);
            frames.Add(new AudioFrame(value, value));
        }
        
        return frames;
    }
    
    /// <summary>
    /// Processes samples into AudioFrames for stereo playback.
    /// Mirrors DOSBox maybe_silence&lt;FrameType::Stereo&gt; template.
    /// </summary>
    public static List<AudioFrame> MaybeSilenceStereo(
        byte[] samples,
        uint numSamples,
        bool speakerEnabled,
        ref int warmupRemainingMs,
        bool swapChannels,
        bool signed) {
        
        const int samplesPerFrame = 2;
        int numFrames = (int)(numSamples / samplesPerFrame);
        List<AudioFrame> frames = new List<AudioFrame>(numFrames);
        
        // Return silent frames if still in warmup
        if (warmupRemainingMs > 0) {
            warmupRemainingMs--;
            for (int i = 0; i < numFrames; i++) {
                frames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return frames;
        }
        
        // Return silent frames if speaker disabled
        if (!speakerEnabled) {
            for (int i = 0; i < numFrames; i++) {
                frames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return frames;
        }
        
        // Process sample pairs into stereo AudioFrames
        for (int i = 0; i < numFrames; i++) {
            float left = signed ? ToFloatS8((sbyte)samples[i * 2]) : ToFloatU8(samples[i * 2]);
            float right = signed ? ToFloatS8((sbyte)samples[i * 2 + 1]) : ToFloatU8(samples[i * 2 + 1]);
            
            // SB Pro1 and Pro2 swap left/right channels
            if (swapChannels) {
                frames.Add(new AudioFrame(right, left));
            } else {
                frames.Add(new AudioFrame(left, right));
            }
        }
        
        return frames;
    }
    
    /// <summary>
    /// Similar to MaybeSilenceStereo but for 16-bit samples.
    /// </summary>
    public static List<AudioFrame> MaybeSilenceStereo16(
        short[] samples,
        uint numSamples,
        bool speakerEnabled,
        ref int warmupRemainingMs,
        bool swapChannels,
        bool signed) {
        
        const int samplesPerFrame = 2;
        int numFrames = (int)(numSamples / samplesPerFrame);
        List<AudioFrame> frames = new List<AudioFrame>(numFrames);
        
        // Return silent frames if still in warmup
        if (warmupRemainingMs > 0) {
            warmupRemainingMs--;
            for (int i = 0; i < numFrames; i++) {
                frames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return frames;
        }
        
        // Return silent frames if speaker disabled
        if (!speakerEnabled) {
            for (int i = 0; i < numFrames; i++) {
                frames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return frames;
        }
        
        // Process sample pairs into stereo AudioFrames
        for (int i = 0; i < numFrames; i++) {
            float left = signed ? ToFloatS16(samples[i * 2]) : ToFloatU16((ushort)samples[i * 2]);
            float right = signed ? ToFloatS16(samples[i * 2 + 1]) : ToFloatU16((ushort)samples[i * 2 + 1]);
            
            // SB Pro1 and Pro2 swap left/right channels
            if (swapChannels) {
                frames.Add(new AudioFrame(right, left));
            } else {
                frames.Add(new AudioFrame(left, right));
            }
        }
        
        return frames;
    }
}
