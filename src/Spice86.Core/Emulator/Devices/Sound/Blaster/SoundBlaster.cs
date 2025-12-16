// SPDX-License-Identifier: GPL-2.0-or-later
// Sound Blaster device implementation mirrored from DOSBox Staging
// Reference: src/hardware/audio/soundblaster.cpp
// SB PRO 2 support with DSP command handling and DAC emulation
// http://www.fysnet.net/detectsb.htm

namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Libs.Sound.Common;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
/// Sound Blaster SB PRO 2 emulation - mirrors DOSBox Staging soundblaster.cpp
/// </summary>
public class SoundBlaster : DefaultIOPortHandler, IRequestInterrupt, IBlasterEnvVarProvider {
    private const int DmaBufSize = 1024;
    private const int DspBufSize = 64;
    private const int SbShift = 14;
    private const ushort SbShiftMask = (1 << SbShift) - 1;
    private const byte MinAdaptiveStepSize = 0;
    private const byte DspInitialResetLimit = 4;
    private const int MinPlaybackRateHz = 5000;
    private const int NativeDacRateHz = 45454;
    private const ushort DefaultPlaybackRateHz = 22050;
    
    // Maximum frames to queue in channel buffer to prevent overflow
    // Mirrors DOSBox Staging fix from PR #3915 for issue #3913
    private const int MaxChannelFrames = 4096;

    private enum DspState { Reset, ResetWait, Normal, HighSpeed }
    private enum DmaMode { None, Adpcm2Bit, Adpcm3Bit, Adpcm4Bit, Pcm8Bit, Pcm16Bit, Pcm16BitAliased }
    private enum DspMode { None, Dac, Dma, DmaPause, DmaMasked }
    private enum EssType { None, Es1688 }
    private enum SbIrq { Irq8, Irq16, IrqMpu }
    private enum BlasterState { WaitingForCommand, ResetRequest, Resetting, ReadingCommand }

    private class Dac {
        private float _lastWriteMs;
        private int _currentRateHz = MinPlaybackRateHz;
        private int _sequentialChangesTally;
        private const float PercentDifferenceThreshold = 0.01f;
        private const int SequentialChangesThreshold = 10;

        public int? MeasureDacRateHz(double currentTimeMs) {
            float elapsedMs = (float)currentTimeMs - _lastWriteMs;
            _lastWriteMs = (float)currentTimeMs;

            if (elapsedMs <= 0) {
                return null;
            }

            float measuredRate = 1000.0f / elapsedMs;
            float changePct = Math.Abs(measuredRate - _currentRateHz) / _currentRateHz;

            _sequentialChangesTally = changePct > PercentDifferenceThreshold ? _sequentialChangesTally + 1 : 0;

            if (_sequentialChangesTally > SequentialChangesThreshold) {
                _sequentialChangesTally = 0;
                _currentRateHz = (int)Math.Round(measuredRate);
                return _currentRateHz;
            }

            return null;
        }

        public AudioFrame RenderFrame(byte sample, bool speakerEnabled) {
            float value = speakerEnabled ? LookupTables.U8To16[sample] : 0.0f;
            return new AudioFrame(value, value);
        }
    }

    private class SbInfo {
        public uint FreqHz { get; set; }
        public class DmaState {
            public bool Stereo { get; set; }
            public bool Sign { get; set; }
            public bool AutoInit { get; set; }
            public bool FirstTransfer { get; set; } = true;
            public DmaMode Mode { get; set; }
            public uint Rate { get; set; }
            public uint Mul { get; set; }
            public uint SingleSize { get; set; }
            public uint AutoSize { get; set; }
            public uint Left { get; set; }
            public uint Min { get; set; }
            public byte[] Buf8 { get; } = new byte[DmaBufSize];
            public short[] Buf16 { get; } = new short[DmaBufSize];
            public uint Bits { get; set; }
            public DmaChannel? Channel { get; set; }
            public uint RemainSize { get; set; }
            
            // Performance metrics for DMA operations
            public ulong TotalBytesRead { get; set; }
            public ulong TotalFramesGenerated { get; set; }
            public ulong BufferOverflowCount { get; set; }
            public ulong DmaCompletionCount { get; set; }
        }
        public DmaState Dma { get; } = new();
        public bool SpeakerEnabled { get; set; }
        public bool MidiEnabled { get; set; }
        public byte TimeConstant { get; set; }
        public DspMode Mode { get; set; }
        public SbType Type { get; set; }
        public EssType EssType { get; set; }
        public class IrqState {
            public bool Pending8Bit { get; set; }
            public bool Pending16Bit { get; set; }
        }
        public IrqState Irq { get; } = new();
        public class DspStateInfo {
            public DspState State { get; set; }
            public byte Cmd { get; set; }
            public byte CmdLen { get; set; }
            public byte CmdInPos { get; set; }
            public byte[] CmdIn { get; } = new byte[DspBufSize];
            public class FifoState {
                public byte LastVal { get; set; }
                public byte[] Data { get; } = new byte[DspBufSize];
                public byte Pos { get; set; }
                public byte Used { get; set; }
            }
            public FifoState In { get; } = new();
            public FifoState Out { get; } = new();
            public byte TestRegister { get; set; }
            public byte WriteStatusCounter { get; set; }
            public uint ResetTally { get; set; }
            public int ColdWarmupMs { get; set; }
            public int HotWarmupMs { get; set; }
            public int WarmupRemainingMs { get; set; }
        }
        public DspStateInfo Dsp { get; } = new();
        public Dac DacState { get; set; } = new();
        public class MixerState {
            public byte Index { get; set; } = 0;

            // If true, DOS software can programmatically change the Sound
            // Blaster mixer's volume levels on SB Pro 1 and later card for
            // the SB (DAC), OPL (FM) and CDAUDIO channels. These are called
            // "app levels". The final output level is the "user level" set
            // in the DOSBox mixer multiplied with the "app level" for these
            // channels.
            //
            // If the Sound Blaster mixer is disabled, the "app levels"
            // default to unity gain.
            //
            public bool Enabled { get; set; } = false;

            public byte[] Dac { get; } = new byte[2];
            public byte[] Fm { get; } = new byte[2];
            public byte[] Cda { get; } = new byte[2];

            public byte[] Master { get; } = new byte[2];
            public byte[] Lin { get; } = new byte[2];
            public byte Mic { get; set; } = 0;

            public bool StereoEnabled { get; set; } = false;

            public bool FilterEnabled { get; set; } = true;
            public bool FilterConfigured { get; set; } = false;
            public bool FilterAlwaysOn { get; set; } = false;

            public byte[] Unhandled { get; } = new byte[0x48];

            public byte[] EssIdStr { get; } = new byte[4];
            public byte EssIdStrPos { get; set; } = 0;
        }

        public MixerState Mixer { get; } = new MixerState();

        public class AdpcmState {
            public byte Reference { get; set; } = 0;
            public ushort Stepsize { get; set; } = 0;
            public bool HaveRef { get; set; } = false;
        }

        public AdpcmState Adpcm { get; } = new AdpcmState();

        public class HwState {
            public ushort Base { get; set; } = 0;
            public byte Irq { get; set; } = 0;
            public byte Dma8 { get; set; } = 0;
            public byte Dma16 { get; set; } = 0;
        }

        public HwState Hw { get; } = new HwState();

        public class E2State {
            public int Value { get; set; } = 0;
            public uint Count { get; set; } = 0;
        }

        public E2State E2 { get; } = new E2State();
    }

    // =============================================================================
    // ADPCM Decoders - ported from DOSBox Staging soundblaster.cpp lines 863-958
    // =============================================================================
    
    /// <summary>
    /// Decodes a single ADPCM portion using the specified mapping tables.
    /// Mirrors decode_adpcm_portion() from DOSBox.
    /// </summary>
    private static byte DecodeAdpcmPortion(
        int bitPortion,
        ReadOnlySpan<byte> adjustMap,
        ReadOnlySpan<sbyte> scaleMap,
        int lastIndex,
        ref byte reference,
        ref ushort stepsize) {
        
        int i = Math.Clamp(bitPortion + stepsize, 0, lastIndex);
        stepsize = (ushort)((stepsize + adjustMap[i]) & 0xFF);
        int newSample = reference + scaleMap[i];
        reference = (byte)Math.Clamp(newSample, 0, 255);
        return reference;
    }
    
    /// <summary>
    /// Decodes one byte of 2-bit ADPCM data into 4 samples.
    /// Mirrors decode_adpcm_2bit() from DOSBox.
    /// </summary>
    private static byte[] DecodeAdpcm2Bit(byte data, ref byte reference, ref ushort stepsize) {
        ReadOnlySpan<sbyte> scaleMap = stackalloc sbyte[] {
             0,  1,  0,  -1,  1,  3,  -1,  -3,
             2,  6, -2,  -6,  4, 12,  -4, -12,
             8, 24, -8, -24,  6, 48, -16, -48
        };
        ReadOnlySpan<byte> adjustMap = stackalloc byte[] {
              0,   4,   0,   4,
            252,   4, 252,   4, 252,   4, 252,   4,
            252,   4, 252,   4, 252,   4, 252,   4,
            252,   0, 252,   0
        };
        const int lastIndex = 23;
        
        byte[] samples = new byte[4];
        samples[0] = DecodeAdpcmPortion((data >> 6) & 0x3, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        samples[1] = DecodeAdpcmPortion((data >> 4) & 0x3, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        samples[2] = DecodeAdpcmPortion((data >> 2) & 0x3, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        samples[3] = DecodeAdpcmPortion((data >> 0) & 0x3, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        return samples;
    }
    
    /// <summary>
    /// Decodes one byte of 3-bit ADPCM data into 3 samples.
    /// Mirrors decode_adpcm_3bit() from DOSBox.
    /// </summary>
    private static byte[] DecodeAdpcm3Bit(byte data, ref byte reference, ref ushort stepsize) {
        ReadOnlySpan<sbyte> scaleMap = stackalloc sbyte[] {
             0,  1,  2,  3,  0,  -1,  -2,  -3,
             1,  3,  5,  7, -1,  -3,  -5,  -7,
             2,  6, 10, 14, -2,  -6, -10, -14,
             4, 12, 20, 28, -4, -12, -20, -28,
             5, 15, 25, 35, -5, -15, -25, -35
        };
        ReadOnlySpan<byte> adjustMap = stackalloc byte[] {
              0, 0, 0,   8,   0, 0, 0,   8,
            248, 0, 0,   8, 248, 0, 0,   8,
            248, 0, 0,   8, 248, 0, 0,   8,
            248, 0, 0,   8, 248, 0, 0,   8,
            248, 0, 0,   0, 248, 0, 0,   0
        };
        const int lastIndex = 39;
        
        byte[] samples = new byte[3];
        samples[0] = DecodeAdpcmPortion((data >> 5) & 0x7, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        samples[1] = DecodeAdpcmPortion((data >> 2) & 0x7, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        samples[2] = DecodeAdpcmPortion((data & 0x3) << 1, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        return samples;
    }
    
    /// <summary>
    /// Decodes one byte of 4-bit ADPCM data into 2 samples.
    /// Mirrors decode_adpcm_4bit() from DOSBox.
    /// </summary>
    private static byte[] DecodeAdpcm4Bit(byte data, ref byte reference, ref ushort stepsize) {
        ReadOnlySpan<sbyte> scaleMap = stackalloc sbyte[] {
             0,  1,  2,  3,  4,  5,  6,  7,  0,  -1,  -2,  -3,  -4,  -5,  -6,  -7,
             1,  3,  5,  7,  9, 11, 13, 15, -1,  -3,  -5,  -7,  -9, -11, -13, -15,
             2,  6, 10, 14, 18, 22, 26, 30, -2,  -6, -10, -14, -18, -22, -26, -30,
             4, 12, 20, 28, 36, 44, 52, 60, -4, -12, -20, -28, -36, -44, -52, -60
        };
        ReadOnlySpan<byte> adjustMap = stackalloc byte[] {
              0, 0, 0, 0, 0, 16, 16, 16,
              0, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0, 16, 16, 16,
            240, 0, 0, 0, 0,  0,  0,  0,
            240, 0, 0, 0, 0,  0,  0,  0
        };
        const int lastIndex = 63;
        
        byte[] samples = new byte[2];
        samples[0] = DecodeAdpcmPortion(data >> 4, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        samples[1] = DecodeAdpcmPortion(data & 0xF, adjustMap, scaleMap, lastIndex, ref reference, ref stepsize);
        return samples;
    }
    
    /// <summary>
    /// Wrapper for DecodeAdpcm2Bit that returns a tuple (for bulk transfer compatibility).
    /// </summary>
    private static (byte[], byte, ushort) DecodeAdpcm2Bit(byte data, byte reference, ushort stepsize) {
        byte refCopy = reference;
        ushort stepsizeCopy = stepsize;
        byte[] samples = DecodeAdpcm2Bit(data, ref refCopy, ref stepsizeCopy);
        return (samples, refCopy, stepsizeCopy);
    }
    
    /// <summary>
    /// Wrapper for DecodeAdpcm3Bit that returns a tuple (for bulk transfer compatibility).
    /// </summary>
    private static (byte[], byte, ushort) DecodeAdpcm3Bit(byte data, byte reference, ushort stepsize) {
        byte refCopy = reference;
        ushort stepsizeCopy = stepsize;
        byte[] samples = DecodeAdpcm3Bit(data, ref refCopy, ref stepsizeCopy);
        return (samples, refCopy, stepsizeCopy);
    }
    
    /// <summary>
    /// Wrapper for DecodeAdpcm4Bit that returns a tuple (for bulk transfer compatibility).
    /// </summary>
    private static (byte[], byte, ushort) DecodeAdpcm4Bit(byte data, byte reference, ushort stepsize) {
        byte refCopy = reference;
        ushort stepsizeCopy = stepsize;
        byte[] samples = DecodeAdpcm4Bit(data, ref refCopy, ref stepsizeCopy);
        return (samples, refCopy, stepsizeCopy);
    }
    
    // =============================================================================
    // Bulk DMA Reading - ported from DOSBox Staging soundblaster.cpp lines 1029-1113
    // =============================================================================
    
    /// <summary>
    /// Optimized 8-bit DMA read with boundary checking.
    /// Mirrors read_dma_8bit() from DOSBox.
    /// Reference: src/hardware/audio/soundblaster.cpp lines 1029-1059
    /// </summary>
    private uint ReadDma8Bit(uint bytesToRead, uint bufferIndex = 0) {
        if (bufferIndex >= DmaBufSize) {
            _loggerService.Error("SOUNDBLASTER: Read requested out of bounds of DMA buffer at index {Index}", bufferIndex);
            return 0;
        }
        
        if (_sb.Dma.Channel is null) {
            _loggerService.Warning("SOUNDBLASTER: Attempted to read DMA with null channel");
            return 0;
        }
        
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("SOUNDBLASTER: ReadDma8Bit - Requesting {BytesToRead} bytes from channel {Channel}, bufferIndex={BufferIndex}",
                bytesToRead, _sb.Dma.Channel.ChannelNumber, bufferIndex);
        }
        
        uint bytesAvailable = DmaBufSize - bufferIndex;
        uint clampedBytes = Math.Min(bytesToRead, bytesAvailable);
        
        try {
            Span<byte> buffer = _sb.Dma.Buf8.AsSpan((int)bufferIndex, (int)clampedBytes);
            int bytesRead = _sb.Dma.Channel.Read((int)clampedBytes, buffer);
            uint actualBytesRead = (uint)Math.Max(0, bytesRead);
            
            // Track performance metrics
            _sb.Dma.TotalBytesRead += actualBytesRead;
            
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("SOUNDBLASTER: ReadDma8Bit - Read {ActualBytes}/{RequestedBytes} bytes from channel {Channel}",
                    actualBytesRead, clampedBytes, _sb.Dma.Channel.ChannelNumber);
            }
            
            return actualBytesRead;
        } catch (Exception ex) {
            _loggerService.Error(ex, "SOUNDBLASTER: Exception during 8-bit DMA read");
            return 0;
        }
    }
    
    /// <summary>
    /// Optimized 16-bit DMA read with alignment handling.
    /// Mirrors read_dma_16bit() from DOSBox.
    /// Reference: src/hardware/audio/soundblaster.cpp lines 1060-1113
    /// </summary>
    private uint ReadDma16Bit(uint wordsToRead, uint bufferIndex = 0) {
        if (bufferIndex >= DmaBufSize) {
            _loggerService.Error("SOUNDBLASTER: Read requested out of bounds of DMA buffer at index {Index}", bufferIndex);
            return 0;
        }
        
        if (_sb.Dma.Channel is null) {
            _loggerService.Warning("SOUNDBLASTER: Attempted to read DMA with null channel");
            return 0;
        }
        
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("SOUNDBLASTER: ReadDma16Bit - Requesting {WordsToRead} words from channel {Channel}, bufferIndex={BufferIndex}",
                wordsToRead, _sb.Dma.Channel.ChannelNumber, bufferIndex);
        }
        
        // In DMA controller, if channel is 16-bit, we're dealing with 16-bit words.
        // Otherwise, we're dealing with 8-bit words (bytes).
        bool is16BitChannel = _sb.Dma.Channel.ChannelNumber >= 4 && _sb.Dma.Channel.ChannelNumber != 4;
        uint bytesRequested = wordsToRead;
        
        if (is16BitChannel) {
            bytesRequested *= 2;
            if (_sb.Dma.Mode != DmaMode.Pcm16Bit) {
                _loggerService.Warning("SOUNDBLASTER: Expected 16-bit mode but DMA mode is {Mode}", _sb.Dma.Mode);
            }
        } else {
            if (_sb.Dma.Mode != DmaMode.Pcm16BitAliased) {
                _loggerService.Warning("SOUNDBLASTER: Expected 16-bit aliased mode but DMA mode is {Mode}", _sb.Dma.Mode);
            }
        }
        
        // Clamp words to read so we don't overflow our buffer
        uint bytesAvailable = (DmaBufSize - bufferIndex) * 2;
        uint clampedWords = Math.Min(bytesRequested, bytesAvailable);
        if (is16BitChannel) {
            clampedWords /= 2;
        }
        
        try {
            // Read into the 16-bit buffer (reinterpret as byte span for DMA controller)
            int byteOffset = (int)bufferIndex * 2;
            Span<byte> byteBuffer = System.Runtime.InteropServices.MemoryMarshal.Cast<short, byte>(_sb.Dma.Buf16.AsSpan());
            Span<byte> targetBuffer = byteBuffer.Slice(byteOffset);
            int wordsRead = _sb.Dma.Channel.Read((int)clampedWords, targetBuffer);
            uint actualWordsRead = (uint)Math.Max(0, wordsRead);
            
            // Track performance metrics (count bytes, not words)
            uint bytesRead = actualWordsRead * (is16BitChannel ? 2u : 1u);
            _sb.Dma.TotalBytesRead += bytesRead;
            
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("SOUNDBLASTER: ReadDma16Bit - Read {ActualWords}/{RequestedWords} words ({BytesRead} bytes) from channel {Channel}",
                    actualWordsRead, clampedWords, bytesRead, _sb.Dma.Channel.ChannelNumber);
            }
            
            return actualWordsRead;
        } catch (Exception ex) {
            _loggerService.Error(ex, "SOUNDBLASTER: Exception during 16-bit DMA read");
            return 0;
        }
    }
    
    // =============================================================================
    // DSP Command Tables - ported from DOSBox Staging soundblaster.cpp lines 205-265
    // =============================================================================
    
    /// <summary>
    /// Number of parameter bytes for DSP commands on SB/SB Pro models.
    /// Mirrors dsp_cmd_len_sb[] from DOSBox.
    /// </summary>
    private static readonly byte[] DspCommandLengthsSb = new byte[256] {
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x00
        1, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x10 (Wari hack: 0x15-0x17 have 2 bytes)
        0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x20
        0, 0, 0, 0,  0, 0, 0, 0,  1, 0, 0, 0,  0, 0, 0, 0,  // 0x30
        1, 2, 2, 0,  0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  // 0x40
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x50
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x60
        0, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x70
        2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x80
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x90
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xA0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xB0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xC0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xD0
        1, 0, 1, 0,  1, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xE0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0   // 0xF0
    };
    
    /// <summary>
    /// Number of parameter bytes for DSP commands on SB16 model.
    /// Mirrors dsp_cmd_len_sb16[] from DOSBox.
    /// </summary>
    private static readonly byte[] DspCommandLengthsSb16 = new byte[256] {
        0, 0, 0, 0,  1, 2, 0, 0,  1, 0, 0, 0,  0, 0, 2, 1,  // 0x00
        1, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x10
        0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x20
        0, 0, 0, 0,  0, 0, 0, 0,  1, 0, 0, 0,  0, 0, 0, 0,  // 0x30
        1, 2, 2, 0,  0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  // 0x40
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x50
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x60
        0, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x70
        2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x80
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0x90
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xA0
        3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  // 0xB0
        3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  // 0xC0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xD0
        1, 0, 1, 0,  1, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  // 0xE0
        0, 0, 0, 0,  0, 0, 0, 0,  0, 1, 0, 0,  0, 0, 0, 0   // 0xF0
    };


    private static readonly int[][] E2IncrTable = new int[][] {
        new int[] { 0x01, -0x02, -0x04, 0x08, -0x10, 0x20, 0x40, -0x80, -106 },
        new int[] { -0x01, 0x02, -0x04, 0x08, 0x10, -0x20, 0x40, -0x80, 165 },
        new int[] { -0x01, 0x02, 0x04, -0x08, 0x10, -0x20, -0x40, 0x80, -151 },
        new int[] { 0x01, -0x02, 0x04, -0x08, -0x10, 0x20, -0x40, 0x80, 90 }
    };

    private readonly SbInfo _sb = new();
    private readonly SoundBlasterHardwareConfig _config;
    private readonly DualPic _dualPic;
    private readonly DmaChannel _primaryDmaChannel;
    private readonly DmaChannel? _secondaryDmaChannel;
    private readonly Mixer _mixer;
    private readonly MixerChannel _dacChannel;
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly HardwareMixer _hardwareMixer;

    private Queue<byte> _outputData = new Queue<byte>();
    private List<byte> _commandData = new List<byte>();
    private byte _currentCommand;
    private byte _commandDataLength;
    private BlasterState _blasterState = BlasterState.WaitingForCommand;
    
    // DMA callback timing tracking - mirrors DOSBox last_dma_callback
    // Tracks the last time DMA callback was invoked for timing measurements
    private double _lastDmaCallbackTime;

    public SoundBlaster(
        IOPortDispatcher ioPortDispatcher,
        State state,
        DmaBus dmaSystem,
        DualPic dualPic,
        Mixer mixer,
        MixerChannel oplMixerChannel,
        ILoggerService loggerService,
        EmulationLoopScheduler scheduler,
        IEmulatedClock clock,
        SoundBlasterHardwareConfig soundBlasterHardwareConfig) 
        : base(state, false, loggerService) {

        _config = soundBlasterHardwareConfig;
        _dualPic = dualPic;
        _mixer = mixer;
        _scheduler = scheduler;
        _clock = clock;

        _primaryDmaChannel = dmaSystem.GetChannel(_config.LowDma)
            ?? throw new InvalidOperationException($"DMA channel {_config.LowDma} unavailable for Sound Blaster.");

        _secondaryDmaChannel = ShouldUseHighDmaChannel()
            ? dmaSystem.GetChannel(_config.HighDma)
            : null;

        if (_primaryDmaChannel.ChannelNumber == 4 ||
            (_secondaryDmaChannel is not null && _secondaryDmaChannel.ChannelNumber == 4)) {
            throw new InvalidOperationException("Sound Blaster cannot attach to cascade DMA channel 4.");
        }
        
        // Reserve DMA channels for Sound Blaster - mirrors DOSBox channel reservation pattern
        // This prevents other devices from hijacking our channels
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SOUNDBLASTER: Reserving DMA channel {Channel} for Sound Blaster", 
                _primaryDmaChannel.ChannelNumber);
        }
        _primaryDmaChannel.ReserveFor("SoundBlaster", OnDmaChannelEvicted);
        
        if (_secondaryDmaChannel is not null) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("SOUNDBLASTER: Reserving secondary DMA channel {Channel} for Sound Blaster 16-bit", 
                    _secondaryDmaChannel.ChannelNumber);
            }
            _secondaryDmaChannel.ReserveFor("SoundBlaster16", OnDmaChannelEvicted);
        }

        _sb.Type = _config.SbType;
        _sb.Hw.Base = _config.BaseAddress;
        _sb.Hw.Irq = _config.Irq;
        _sb.Hw.Dma8 = _config.LowDma;
        _sb.Hw.Dma16 = _config.HighDma;

        const int ColdWarmupMs = 100;
        _sb.Dsp.ColdWarmupMs = ColdWarmupMs;
        _sb.Dsp.HotWarmupMs = ColdWarmupMs / 32;
        _sb.FreqHz = DefaultPlaybackRateHz;
        _sb.TimeConstant = 45;

        HashSet<ChannelFeature> dacFeatures = new HashSet<ChannelFeature> {
            ChannelFeature.ReverbSend,
            ChannelFeature.ChorusSend,
            ChannelFeature.DigitalAudio,
            ChannelFeature.Sleep
        };
        if (_config.SbType == SbType.SBPro1 || _config.SbType == SbType.SBPro2 || _config.SbType == SbType.Sb16) {
            dacFeatures.Add(ChannelFeature.Stereo);
        }

        _dacChannel = _mixer.AddChannel(GenerateFrames, (int)_sb.FreqHz, "SoundBlasterDAC", dacFeatures);
        _dacChannel.Enable(true);

        _hardwareMixer = new HardwareMixer(soundBlasterHardwareConfig, _dacChannel, oplMixerChannel, loggerService);
        _hardwareMixer.Reset();

        InitSpeakerState();
        InitPortHandlers(ioPortDispatcher);

        _dualPic.SetIrqMask(_config.Irq, false);

        _scheduler.AddEvent(MixerTickCallback, 1.0);

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            string highDmaSegment = ShouldUseHighDmaChannel() ? $", high DMA {_config.HighDma}" : string.Empty;
            _loggerService.Information(
                "SoundBlaster: Initialized {SbType} on port {Port:X3}, IRQ {Irq}, DMA {LowDma}{HighDmaSegment}",
                _sb.Type, _sb.Hw.Base, _sb.Hw.Irq, _sb.Hw.Dma8, highDmaSegment);
        }
    }

    private void GenerateFrames(int framesRequested) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("SB: GenerateFrames framesRequested={Frames} mode={Mode} dmaLeft={Left} bufferSize={BufferSize}", 
                framesRequested, _sb.Mode, _sb.Dma.Left, _dacChannel.AudioFrames.Count);
        }
        
        // Update DMA callback timestamp - mirrors DOSBox last_dma_callback
        // Reference: src/hardware/audio/soundblaster.cpp line 1139
        _lastDmaCallbackTime = _clock.CurrentTimeMs;
        
        // Callback from mixer requesting audio frames
        // Mirrors DOSBox's soundblaster.cpp GenerateFrames pattern
        
        // Prevent buffer overflow - mirrors DOSBox Staging fix from PR #3915
        // Don't generate more frames if buffer is already full
        int currentBufferSize = _dacChannel.AudioFrames.Count;
        if (currentBufferSize >= MaxChannelFrames) {
            _sb.Dma.BufferOverflowCount++;
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("SB: Channel buffer at max capacity ({Current}/{Max}), skipping frame generation (overflow #{Count})", 
                    currentBufferSize, MaxChannelFrames, _sb.Dma.BufferOverflowCount);
            }
            return;
        }
        
        // Limit frames to generate to prevent overflow
        int maxFramesToGenerate = Math.Min(framesRequested, MaxChannelFrames - currentBufferSize);
        
        int framesGenerated = 0;
        while (framesGenerated < maxFramesToGenerate) {
            AudioFrame frame = new(0.0f, 0.0f);
            
            switch (_sb.Mode) {
                case DspMode.None:
                case DspMode.DmaPause:
                case DspMode.DmaMasked:
                    // Generate silence
                    frame = new AudioFrame(0.0f, 0.0f);
                    break;

                case DspMode.Dac:
                    // DAC mode - render current DAC sample
                    if (_sb.Dsp.In.Data.Length > 0) {
                        byte dacSample = _sb.Dsp.In.Data[0];
                        frame = MaybeSilenceFrame(dacSample);
                    }
                    break;

                case DspMode.Dma:
                    // Keep channel awake during DMA processing - mirrors DOSBox soundblaster.cpp:3200
                    // This is a no-op if the channel is already running
                    MaybeWakeUp();
                    
                    // Use bulk DMA transfer if there's data to process
                    // Calculate bytes needed for requested frames
                    if (_sb.Dma.Left > 0 && _sb.Dma.Channel != null) {
                        // Calculate bytes per frame based on mode
                        uint bytesPerFrame = CalculateBytesPerFrame();
                        uint bytesRequested = (uint)maxFramesToGenerate * bytesPerFrame;
                        
                        // Perform bulk DMA transfer
                        PlayDmaTransfer(bytesRequested);
                        
                        // Frames were already enqueued by PlayDmaTransfer,
                        // so break out of the frame generation loop
                        framesGenerated = maxFramesToGenerate;
                    }
                    break;
            }

            _dacChannel.AudioFrames.Add(frame);
            framesGenerated++;
        }
        
        // Track performance metrics
        _sb.Dma.TotalFramesGenerated += (ulong)framesGenerated;
        
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("SB: Generated {Generated}/{Requested} frames, buffer now at {BufferSize} (total frames: {TotalFrames})", 
                framesGenerated, framesRequested, _dacChannel.AudioFrames.Count, _sb.Dma.TotalFramesGenerated);
        }
    }

    /// <summary>
    /// Wakes up the DAC channel if it has the sleep feature enabled.
    /// Mirrors DOSBox SoundBlaster::MaybeWakeUp() from soundblaster.cpp:1490-1494
    /// </summary>
    /// <returns>True if the channel was actually woken up, false if already awake</returns>
    private bool MaybeWakeUp() {
        return _dacChannel.WakeUp();
    }
    
    /// <summary>
    /// DMA channel callback handler - mirrors DOSBox dsp_dma_callback().
    /// Handles DMA channel state changes (masked/unmasked/terminal count).
    /// Reference: src/hardware/audio/soundblaster.cpp lines 772-874
    /// </summary>
    private void DspDmaCallback(DmaChannel channel, DmaChannel.DmaEvent dmaEvent) {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SOUNDBLASTER: DMA callback - Event={Event}, Mode={Mode}, DmaMode={DmaMode}, Left={Left}, Channel={Channel}, AutoInit={AutoInit}", 
                dmaEvent, _sb.Mode, _sb.Dma.Mode, _sb.Dma.Left, channel.ChannelNumber, _sb.Dma.AutoInit);
        }
        
        switch (dmaEvent) {
            case DmaChannel.DmaEvent.ReachedTerminalCount:
                // Terminal count reached - DMA transfer completed naturally
                // This is handled by OnDmaTransferComplete() in GenerateFrames
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SOUNDBLASTER: Terminal count reached on channel {Channel}, Left={Left}, AutoInit={AutoInit}",
                        channel.ChannelNumber, _sb.Dma.Left, _sb.Dma.AutoInit);
                }
                break;
                
            case DmaChannel.DmaEvent.IsMasked:
                // DMA channel has been masked (disabled) - mirrors DOSBox IsMasked case
                if (_sb.Mode == DspMode.Dma) {
                    // Catch up to current time but don't generate IRQ
                    // This fixes timing issues with later SCI games
                    double currentTime = _clock.CurrentTimeMs;
                    double elapsedTime = currentTime - _lastDmaCallbackTime;
                    
                    if (elapsedTime > 0 && _sb.Dma.Rate > 0) {
                        uint samplesToGenerate = (uint)(_sb.Dma.Rate * elapsedTime / 1000.0);
                        
                        // Limit to sb.dma.min to prevent runaway
                        if (samplesToGenerate > _sb.Dma.Min) {
                            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                                _loggerService.Debug("SOUNDBLASTER: Limiting masked amount to Min={Min} (was {Samples})", 
                                    _sb.Dma.Min, samplesToGenerate);
                            }
                            samplesToGenerate = _sb.Dma.Min;
                        }
                        
                        // Calculate minimum transfer size
                        uint minSize = _sb.Dma.Mul >> SbShift;
                        if (minSize == 0) {
                            minSize = 1;
                        }
                        minSize *= 2;
                        
                        // Only process if we have enough data left
                        if (_sb.Dma.Left > minSize) {
                            if (samplesToGenerate > (_sb.Dma.Left - minSize)) {
                                samplesToGenerate = _sb.Dma.Left - minSize;
                            }
                            
                            // Don't trigger IRQ if we're about to complete a non-autoinit transfer
                            if (!_sb.Dma.AutoInit && _sb.Dma.Left <= _sb.Dma.Min) {
                                samplesToGenerate = 0;
                            }
                            
                            // Process remaining DMA samples
                            // This would normally call ProcessDMATransfer(samplesToGenerate)
                            // but in our architecture, we let GenerateFrames handle it
                        }
                    }
                    
                    // Transition to masked state
                    _sb.Mode = DspMode.DmaMasked;
                    
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SOUNDBLASTER: DMA masked, stopping output. Left={Left}, CurrentCount={Count}",
                            _sb.Dma.Left, channel.CurrentCount);
                    }
                }
                break;
                
            case DmaChannel.DmaEvent.IsUnmasked:
                // DMA channel has been unmasked (enabled) - mirrors DOSBox IsUnmasked case
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SOUNDBLASTER: DMA unmasked event - Mode={Mode}, DmaMode={DmaMode}, Channel={Channel}",
                        _sb.Mode, _sb.Dma.Mode, channel.ChannelNumber);
                }
                
                if (_sb.Mode == DspMode.DmaMasked && _sb.Dma.Mode != DmaMode.None) {
                    // Transition back to active DMA mode
                    DspChangeMode(DspMode.Dma);
                    
                    // Wake up the mixer channel for playback
                    // Unmasking is when software has finished setup and is ready for playback
                    MaybeWakeUp();
                    
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SOUNDBLASTER: DMA unmasked, starting output. IsAutoiniting={IsAutoiniting}, BaseCount={BaseCount}, CurrentCount={CurrentCount}",
                            channel.IsAutoiniting, channel.BaseCount, channel.CurrentCount);
                    }
                } else {
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SOUNDBLASTER: DMA unmasked but not in DmaMasked mode, ignoring");
                    }
                }
                break;
        }
    }
    
    /// <summary>
    /// Performs a bulk DMA transfer, reading and processing multiple samples at once.
    /// Mirrors DOSBox play_dma_transfer() function for efficient batch processing.
    /// Reference: src/hardware/audio/soundblaster.cpp lines 1114-1365
    /// </summary>
    /// <param name="bytesRequested">Number of bytes requested for this transfer</param>
    private void PlayDmaTransfer(uint bytesRequested) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("SOUNDBLASTER: PlayDmaTransfer - BytesRequested={BytesRequested}, Left={Left}, Mode={Mode}, AutoInit={AutoInit}, Channel={Channel}",
                bytesRequested, _sb.Dma.Left, _sb.Dma.Mode, _sb.Dma.AutoInit, _sb.Dma.Channel?.ChannelNumber);
        }
        
        // How many bytes should we read from DMA?
        uint lowerBound = _sb.Dma.AutoInit ? bytesRequested : _sb.Dma.Min;
        uint bytesToRead = _sb.Dma.Left <= lowerBound ? _sb.Dma.Left : bytesRequested;
        
        // All three of these must be populated during the DMA sequence to
        // ensure the proper quantities and unit are being accounted for.
        // For example: use the channel count to convert from samples to frames.
        uint bytesRead = 0;
        uint samples = 0;
        ushort frames = 0;
        
        // In DmaMode.Pcm16BitAliased mode temporarily divide by 2 to get
        // number of 16-bit samples, because 8-bit DMA Read returns byte size,
        // while in DmaMode.Pcm16Bit mode 16-bit DMA Read returns word size.
        byte dma16ToSampleDivisor = _sb.Dma.Mode == DmaMode.Pcm16BitAliased ? (byte)2 : (byte)1;
        
        // Used to convert from samples to frames (which is what AddSamples unintuitively uses..)
        byte channels = _sb.Dma.Stereo ? (byte)2 : (byte)1;
        
        _lastDmaCallbackTime = _clock.CurrentTimeMs;
        
        // Read the actual data, process it and send it off to the mixer
        switch (_sb.Dma.Mode) {
            case DmaMode.Adpcm2Bit:
                (bytesRead, samples, frames) = DecodeAdpcmDma(bytesToRead, DecodeAdpcm2Bit);
                break;
                
            case DmaMode.Adpcm3Bit:
                (bytesRead, samples, frames) = DecodeAdpcmDma(bytesToRead, DecodeAdpcm3Bit);
                break;
                
            case DmaMode.Adpcm4Bit:
                (bytesRead, samples, frames) = DecodeAdpcmDma(bytesToRead, DecodeAdpcm4Bit);
                break;
                
            case DmaMode.Pcm8Bit:
                if (_sb.Dma.Stereo) {
                    bytesRead = ReadDma8Bit(bytesToRead, _sb.Dma.RemainSize);
                    samples = bytesRead + _sb.Dma.RemainSize;
                    frames = (ushort)(samples / channels);
                    
                    // Only add whole frames when in stereo DMA mode. The
                    // number of frames comes from the DMA request, and
                    // therefore user-space data.
                    if (frames > 0) {
                        if (_sb.Dma.Sign) {
                            EnqueueFramesStereo(_sb.Dma.Buf8, samples, true);
                        } else {
                            EnqueueFramesStereo(_sb.Dma.Buf8, samples, false);
                        }
                    }
                    // Otherwise there's an unhandled dangling sample from the last round
                    if ((samples & 1) != 0) {
                        _sb.Dma.RemainSize = 1;
                        _sb.Dma.Buf8[0] = _sb.Dma.Buf8[samples - 1];
                    } else {
                        _sb.Dma.RemainSize = 0;
                    }
                } else { // Mono
                    bytesRead = ReadDma8Bit(bytesToRead);
                    samples = bytesRead;
                    // mono sanity-check
                    if (channels != 1) {
                        _loggerService.Error("SOUNDBLASTER: Mono mode but channels={Channels}", channels);
                    }
                    if (_sb.Dma.Sign) {
                        EnqueueFramesMono(_sb.Dma.Buf8, samples, true);
                    } else {
                        EnqueueFramesMono(_sb.Dma.Buf8, samples, false);
                    }
                }
                break;
                
            case DmaMode.Pcm16BitAliased:
            case DmaMode.Pcm16Bit:
                if (_sb.Dma.Stereo) {
                    bytesRead = ReadDma16Bit(bytesToRead, _sb.Dma.RemainSize);
                    samples = (bytesRead + _sb.Dma.RemainSize) / dma16ToSampleDivisor;
                    frames = (ushort)(samples / channels);
                    
                    // Only add whole frames when in stereo DMA mode
                    if (frames > 0) {
                        if (_sb.Dma.Sign) {
                            EnqueueFramesStereo16(_sb.Dma.Buf16, samples, true);
                        } else {
                            EnqueueFramesStereo16(_sb.Dma.Buf16, samples, false);
                        }
                    }
                    if ((samples & 1) != 0) {
                        // Carry over the dangling sample into the next round, or...
                        _sb.Dma.RemainSize = 1;
                        _sb.Dma.Buf16[0] = _sb.Dma.Buf16[samples - 1];
                    } else {
                        // ...the DMA transfer is done
                        _sb.Dma.RemainSize = 0;
                    }
                } else { // 16-bit mono
                    bytesRead = ReadDma16Bit(bytesToRead);
                    samples = bytesRead / dma16ToSampleDivisor;
                    
                    // mono sanity check
                    if (channels != 1) {
                        _loggerService.Error("SOUNDBLASTER: Mono mode but channels={Channels}", channels);
                    }
                    
                    if (_sb.Dma.Sign) {
                        EnqueueFramesMono16(_sb.Dma.Buf16, samples, true);
                    } else {
                        EnqueueFramesMono16(_sb.Dma.Buf16, samples, false);
                    }
                }
                break;
                
            default:
                _loggerService.Warning("SOUNDBLASTER: Unhandled DMA mode {Mode}", _sb.Dma.Mode);
                _sb.Mode = DspMode.None;
                return;
        }
        
        // Sanity check
        if (frames > samples) {
            _loggerService.Error("SOUNDBLASTER: Frames {Frames} should never exceed samples {Samples}", frames, samples);
        }
        
        // If the first DMA transfer after a reset contains a single sample, it
        // should be ignored. Quake and SBTEST.EXE have this behavior. If not
        // ignored, the channels will be incorrectly reversed.
        // https://github.com/dosbox-staging/dosbox-staging/issues/2942
        // https://www.vogons.org/viewtopic.php?p=536104#p536104
        if (_sb.Dma.FirstTransfer && samples == 1) {
            // Forget any "dangling sample" that would otherwise be carried
            // over to the next transfer.
            _sb.Dma.RemainSize = 0;
        }
        _sb.Dma.FirstTransfer = false;
        
        // Deduct the DMA bytes read from the remaining to still read
        _sb.Dma.Left -= bytesRead;
        
        if (_sb.Dma.Left == 0) {
            if (_sb.Dma.Mode >= DmaMode.Pcm16Bit) {
                RaiseIrq(SbIrq.Irq16);
            } else {
                RaiseIrq(SbIrq.Irq8);
            }
            
            if (!_sb.Dma.AutoInit) {
                // Not new single cycle transfer waiting?
                if (_sb.Dma.SingleSize == 0) {
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SOUNDBLASTER: Single cycle transfer ended");
                    }
                    _sb.Mode = DspMode.None;
                    _sb.Dma.Mode = DmaMode.None;
                } else {
                    // A single size transfer is still waiting, handle that now
                    _sb.Dma.Left = _sb.Dma.SingleSize;
                    _sb.Dma.SingleSize = 0;
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SOUNDBLASTER: Switch to Single cycle transfer begun");
                    }
                }
            } else {
                if (_sb.Dma.AutoSize == 0) {
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SOUNDBLASTER: Auto-init transfer with 0 size");
                    }
                    _sb.Mode = DspMode.None;
                }
                // Continue with a new auto init transfer
                _sb.Dma.Left = _sb.Dma.AutoSize;
            }
        }
    }
    
    /// <summary>
    /// Decodes ADPCM DMA data in bulk using the provided decoder function.
    /// Mirrors the decode_adpcm_dma lambda from DOSBox.
    /// Reference: src/hardware/audio/soundblaster.cpp lines 1139-1173
    /// </summary>
    private (uint bytesRead, uint samples, ushort frames) DecodeAdpcmDma(
        uint bytesToRead,
        Func<byte, byte, ushort, (byte[], byte, ushort)> decodeAdpcmFn) {
        
        uint numBytes = ReadDma8Bit(bytesToRead);
        uint numSamples = 0;
        ushort numFrames = 0;
        
        // Parse the reference ADPCM byte, if provided
        uint i = 0;
        if (numBytes > 0 && _sb.Adpcm.HaveRef) {
            _sb.Adpcm.HaveRef = false;
            _sb.Adpcm.Reference = _sb.Dma.Buf8[0];
            _sb.Adpcm.Stepsize = MinAdaptiveStepSize;
            ++i;
        }
        
        // Decode the remaining DMA buffer into samples using the provided function
        while (i < numBytes) {
            (byte[] decoded, byte reference, ushort stepsize) = decodeAdpcmFn(
                _sb.Dma.Buf8[i], _sb.Adpcm.Reference, _sb.Adpcm.Stepsize);
            
            _sb.Adpcm.Reference = reference;
            _sb.Adpcm.Stepsize = stepsize;
            
            byte numDecoded = (byte)decoded.Length;
            EnqueueFramesMono(decoded, numDecoded, false);
            numSamples += numDecoded;
            i++;
        }
        
        // ADPCM is mono
        numFrames = (ushort)numSamples;
        return (numBytes, numSamples, numFrames);
    }
    
    /// <summary>
    /// Enqueues mono 8-bit frames, applying warmup and speaker state.
    /// Mirrors the maybe_silence + enqueue_frames pattern from DOSBox.
    /// Reference: src/hardware/audio/soundblaster.cpp lines 988-1030, 1107-1112
    /// </summary>
    private void EnqueueFramesMono(byte[] samples, uint numSamples, bool signed) {
        if (numSamples == 0) {
            return;
        }
        
        // Return silent frames if still in warmup
        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            for (uint i = 0; i < numSamples; i++) {
                _dacChannel.AudioFrames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return;
        }
        
        // Return silent frames if speaker is disabled
        if (!_sb.SpeakerEnabled) {
            for (uint i = 0; i < numSamples; i++) {
                _dacChannel.AudioFrames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return;
        }
        
        // Process samples into AudioFrames
        for (uint i = 0; i < numSamples; i++) {
            float value = signed ? LookupTables.S8To16[samples[i]] : LookupTables.U8To16[samples[i]];
            _dacChannel.AudioFrames.Add(new AudioFrame(value, value));
        }
    }
    
    /// <summary>
    /// Enqueues stereo 8-bit frames, applying warmup and speaker state.
    /// Mirrors the maybe_silence + enqueue_frames pattern from DOSBox.
    /// Reference: src/hardware/audio/soundblaster.cpp lines 988-1030, 1107-1112
    /// </summary>
    private void EnqueueFramesStereo(byte[] samples, uint numSamples, bool signed) {
        if (numSamples == 0) {
            return;
        }
        
        uint numFrames = numSamples / 2;
        
        // Return silent frames if still in warmup
        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            for (uint i = 0; i < numFrames; i++) {
                _dacChannel.AudioFrames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return;
        }
        
        // Return silent frames if speaker is disabled
        if (!_sb.SpeakerEnabled) {
            for (uint i = 0; i < numFrames; i++) {
                _dacChannel.AudioFrames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return;
        }
        
        // Process samples into AudioFrames
        // Note: SB Pro 1 and 2 swap left/right channels
        bool swapChannels = _sb.Type == SbType.SBPro1 || _sb.Type == SbType.SBPro2;
        
        for (uint i = 0; i < numFrames; i++) {
            float left = signed ? LookupTables.S8To16[samples[i * 2]] : LookupTables.U8To16[samples[i * 2]];
            float right = signed ? LookupTables.S8To16[samples[i * 2 + 1]] : LookupTables.U8To16[samples[i * 2 + 1]];
            
            if (swapChannels) {
                _dacChannel.AudioFrames.Add(new AudioFrame(right, left));
            } else {
                _dacChannel.AudioFrames.Add(new AudioFrame(left, right));
            }
        }
    }
    
    /// <summary>
    /// Enqueues mono 16-bit frames, applying warmup and speaker state.
    /// Mirrors the maybe_silence + enqueue_frames pattern from DOSBox.
    /// Reference: src/hardware/audio/soundblaster.cpp lines 988-1030, 1107-1112
    /// </summary>
    private void EnqueueFramesMono16(short[] samples, uint numSamples, bool signed) {
        if (numSamples == 0) {
            return;
        }
        
        // Return silent frames if still in warmup
        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            for (uint i = 0; i < numSamples; i++) {
                _dacChannel.AudioFrames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return;
        }
        
        // Return silent frames if speaker is disabled
        if (!_sb.SpeakerEnabled) {
            for (uint i = 0; i < numSamples; i++) {
                _dacChannel.AudioFrames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return;
        }
        
        // Process samples into AudioFrames
        for (uint i = 0; i < numSamples; i++) {
            float value;
            if (signed) {
                value = samples[i];
            } else {
                // Unsigned 16-bit: convert to signed by subtracting 32768
                value = (ushort)samples[i] - 32768;
            }
            _dacChannel.AudioFrames.Add(new AudioFrame(value, value));
        }
    }
    
    /// <summary>
    /// Enqueues stereo 16-bit frames, applying warmup and speaker state.
    /// Mirrors the maybe_silence + enqueue_frames pattern from DOSBox.
    /// Reference: src/hardware/audio/soundblaster.cpp lines 988-1030, 1107-1112
    /// </summary>
    private void EnqueueFramesStereo16(short[] samples, uint numSamples, bool signed) {
        if (numSamples == 0) {
            return;
        }
        
        uint numFrames = numSamples / 2;
        
        // Return silent frames if still in warmup
        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            for (uint i = 0; i < numFrames; i++) {
                _dacChannel.AudioFrames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return;
        }
        
        // Return silent frames if speaker is disabled
        if (!_sb.SpeakerEnabled) {
            for (uint i = 0; i < numFrames; i++) {
                _dacChannel.AudioFrames.Add(new AudioFrame(0.0f, 0.0f));
            }
            return;
        }
        
        // Process samples into AudioFrames
        // Note: SB Pro 1 and 2 swap left/right channels
        bool swapChannels = _sb.Type == SbType.SBPro1 || _sb.Type == SbType.SBPro2;
        
        for (uint i = 0; i < numFrames; i++) {
            float left;
            float right;
            
            if (signed) {
                left = samples[i * 2];
                right = samples[i * 2 + 1];
            } else {
                // Unsigned 16-bit: convert to signed by subtracting 32768
                left = (ushort)samples[i * 2] - 32768;
                right = (ushort)samples[i * 2 + 1] - 32768;
            }
            
            if (swapChannels) {
                _dacChannel.AudioFrames.Add(new AudioFrame(right, left));
            } else {
                _dacChannel.AudioFrames.Add(new AudioFrame(left, right));
            }
        }
    }
    
    /// <summary>
    /// Generates a single audio frame from the DMA buffer.
    /// Mirrors DOSBox play_dma_transfer() pattern for PCM8/ADPCM modes.
    /// </summary>
    private AudioFrame GenerateDmaFrame() {
        // If no DMA transfer is active, return silence
        if (_sb.Dma.Channel is null || _sb.Dma.Left == 0 || _sb.Dma.Mode == DmaMode.None) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("SB: GenerateDmaFrame idle; channelNull={Null} left={Left} mode={Mode}", _sb.Dma.Channel is null, _sb.Dma.Left, _sb.Dma.Mode);
            }
            return new AudioFrame(0.0f, 0.0f);
        }

        // Handle different DMA modes - mirrors DOSBox switch statement
        switch (_sb.Dma.Mode) {
            case DmaMode.Pcm8Bit:
                return GeneratePcm8Frame();
            
            case DmaMode.Adpcm2Bit:
                return GenerateAdpcm2Frame();
            
            case DmaMode.Adpcm3Bit:
                return GenerateAdpcm3Frame();
            
            case DmaMode.Adpcm4Bit:
                return GenerateAdpcm4Frame();
            
            case DmaMode.Pcm16Bit:
            case DmaMode.Pcm16BitAliased:
                return GeneratePcm16Frame();
            
            default:
                _loggerService.Warning("SOUNDBLASTER: Unsupported DMA mode {Mode}", _sb.Dma.Mode);
                return new AudioFrame(0.0f, 0.0f);
        }
    }
    
    /// <summary>
    /// Generates a frame for 8-bit PCM mode.
    /// Mirrors DOSBox Pcm8Bit case in play_dma_transfer().
    /// </summary>
    private AudioFrame GeneratePcm8Frame() {
        byte sample = 0;
        
        try {
            Span<byte> buffer = stackalloc byte[1];
            int wordsRead = _sb.Dma.Channel!.Read(1, buffer);
            
            if (wordsRead > 0) {
                sample = buffer[0];
                
                if (_sb.Dma.Left > 0) {
                    _sb.Dma.Left--;
                }
                
                if (_sb.Dma.FirstTransfer) {
                    _sb.Dma.FirstTransfer = false;
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SOUNDBLASTER: First DMA byte read: 0x{Sample:X2}", sample);
                    }
                }
            } else {
                if (_sb.Dma.Left > 0) {
                    _loggerService.Warning("SOUNDBLASTER: DMA read returned 0 words but DMA.Left={DmaLeft}", _sb.Dma.Left);
                }
                return new AudioFrame(0.0f, 0.0f);
            }
        } catch (Exception ex) {
            _loggerService.Error(ex, "SOUNDBLASTER: Exception reading from DMA channel");
            return new AudioFrame(0.0f, 0.0f);
        }

        // Apply warmup and speaker state - mirrors DOSBox maybe_silence()
        // Reference: src/hardware/audio/soundblaster.cpp lines 988-1030
        return MaybeSilenceFrame(sample);
    }
    
    /// <summary>
    /// Applies warmup and speaker state to a sample frame.
    /// Mirrors DOSBox maybe_silence() logic.
    /// Returns silence if still in warmup or speaker disabled.
    /// Reference: src/hardware/audio/soundblaster.cpp lines 988-1030
    /// </summary>
    private AudioFrame MaybeSilenceFrame(byte sample) {
        // Return silent frame if still in warmup
        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            return new AudioFrame(0.0f, 0.0f);
        }
        
        // Return silent frame if speaker is disabled
        if (!_sb.SpeakerEnabled) {
            return new AudioFrame(0.0f, 0.0f);
        }
        
        // Render actual sample
        return _sb.DacState.RenderFrame(sample, true);
    }
    
    /// <summary>
    /// Generates a frame for 2-bit ADPCM mode.
    /// Mirrors DOSBox Adpcm2Bit case with decode_adpcm_dma lambda.
    /// </summary>
    private AudioFrame GenerateAdpcm2Frame() {
        // Decode samples from buffer cache if available
        if (_sb.Dma.RemainSize > 0) {
            byte sample = _sb.Dma.Buf8[DmaBufSize - (int)_sb.Dma.RemainSize];
            _sb.Dma.RemainSize--;
            return MaybeSilenceFrame(sample);
        }
        
        // Read and decode new byte
        try {
            Span<byte> buffer = stackalloc byte[1];
            int wordsRead = _sb.Dma.Channel!.Read(1, buffer);
            
            if (wordsRead > 0 && _sb.Dma.Left > 0) {
                _sb.Dma.Left--;
                
                // Parse reference byte if provided (mirrors DOSBox haveref handling)
                if (_sb.Adpcm.HaveRef) {
                    _sb.Adpcm.HaveRef = false;
                    _sb.Adpcm.Reference = buffer[0];
                    _sb.Adpcm.Stepsize = MinAdaptiveStepSize;
                    return MaybeSilenceFrame(buffer[0]);
                }
                
                // Decode 1 byte → 4 samples
                byte reference = _sb.Adpcm.Reference;
                ushort stepsize = _sb.Adpcm.Stepsize;
                byte[] samples = DecodeAdpcm2Bit(buffer[0], ref reference, ref stepsize);
                _sb.Adpcm.Reference = reference;
                _sb.Adpcm.Stepsize = stepsize;
                
                // Cache samples in buffer
                for (int i = 0; i < samples.Length; i++) {
                    _sb.Dma.Buf8[i] = samples[i];
                }
                _sb.Dma.RemainSize = (uint)(samples.Length - 1);
                
                return MaybeSilenceFrame(samples[0]);
            }
        } catch (Exception ex) {
            _loggerService.Error(ex, "SOUNDBLASTER: Exception in ADPCM2 decode");
        }
        
        return new AudioFrame(0.0f, 0.0f);
    }
    
    /// <summary>
    /// Generates a frame for 3-bit ADPCM mode.
    /// Mirrors DOSBox Adpcm3Bit case.
    /// </summary>
    private AudioFrame GenerateAdpcm3Frame() {
        if (_sb.Dma.RemainSize > 0) {
            byte sample = _sb.Dma.Buf8[DmaBufSize - (int)_sb.Dma.RemainSize];
            _sb.Dma.RemainSize--;
            return MaybeSilenceFrame(sample);
        }
        
        try {
            Span<byte> buffer = stackalloc byte[1];
            int wordsRead = _sb.Dma.Channel!.Read(1, buffer);
            
            if (wordsRead > 0 && _sb.Dma.Left > 0) {
                _sb.Dma.Left--;
                
                if (_sb.Adpcm.HaveRef) {
                    _sb.Adpcm.HaveRef = false;
                    _sb.Adpcm.Reference = buffer[0];
                    _sb.Adpcm.Stepsize = MinAdaptiveStepSize;
                    return MaybeSilenceFrame(buffer[0]);
                }
                
                byte reference = _sb.Adpcm.Reference;
                ushort stepsize = _sb.Adpcm.Stepsize;
                byte[] samples = DecodeAdpcm3Bit(buffer[0], ref reference, ref stepsize);
                _sb.Adpcm.Reference = reference;
                _sb.Adpcm.Stepsize = stepsize;
                
                for (int i = 0; i < samples.Length; i++) {
                    _sb.Dma.Buf8[i] = samples[i];
                }
                _sb.Dma.RemainSize = (uint)(samples.Length - 1);
                
                return MaybeSilenceFrame(samples[0]);
            }
        } catch (Exception ex) {
            _loggerService.Error(ex, "SOUNDBLASTER: Exception in ADPCM3 decode");
        }
        
        return new AudioFrame(0.0f, 0.0f);
    }
    
    /// <summary>
    /// Generates a frame for 4-bit ADPCM mode.
    /// Mirrors DOSBox Adpcm4Bit case.
    /// </summary>
    private AudioFrame GenerateAdpcm4Frame() {
        if (_sb.Dma.RemainSize > 0) {
            byte sample = _sb.Dma.Buf8[DmaBufSize - (int)_sb.Dma.RemainSize];
            _sb.Dma.RemainSize--;
            return MaybeSilenceFrame(sample);
        }
        
        try {
            Span<byte> buffer = stackalloc byte[1];
            int wordsRead = _sb.Dma.Channel!.Read(1, buffer);
            
            if (wordsRead > 0 && _sb.Dma.Left > 0) {
                _sb.Dma.Left--;
                
                if (_sb.Adpcm.HaveRef) {
                    _sb.Adpcm.HaveRef = false;
                    _sb.Adpcm.Reference = buffer[0];
                    _sb.Adpcm.Stepsize = MinAdaptiveStepSize;
                    return MaybeSilenceFrame(buffer[0]);
                }
                
                byte reference = _sb.Adpcm.Reference;
                ushort stepsize = _sb.Adpcm.Stepsize;
                byte[] samples = DecodeAdpcm4Bit(buffer[0], ref reference, ref stepsize);
                _sb.Adpcm.Reference = reference;
                _sb.Adpcm.Stepsize = stepsize;
                
                for (int i = 0; i < samples.Length; i++) {
                    _sb.Dma.Buf8[i] = samples[i];
                }
                _sb.Dma.RemainSize = (uint)(samples.Length - 1);
                
                return MaybeSilenceFrame(samples[0]);
            }
        } catch (Exception ex) {
            _loggerService.Error(ex, "SOUNDBLASTER: Exception in ADPCM4 decode");
        }
        
        return new AudioFrame(0.0f, 0.0f);
    }
    
    /// <summary>
    /// Generates a frame for 16-bit PCM mode (both true 16-bit and aliased 8-bit).
    /// Mirrors DOSBox Pcm16Bit/Pcm16BitAliased case in play_dma_transfer().
    /// Reference: src/hardware/audio/soundblaster.cpp lines 1240-1285
    /// </summary>
    private AudioFrame GeneratePcm16Frame() {
        // Decode samples from buffer cache if available
        if (_sb.Dma.RemainSize > 0) {
            short sample = _sb.Dma.Buf16[DmaBufSize - (int)_sb.Dma.RemainSize];
            _sb.Dma.RemainSize--;
            
            // Convert 16-bit sample to float (-32768 to 32767 range)
            float value = _sb.SpeakerEnabled ? sample : 0.0f;
            return new AudioFrame(value, value);
        }
        
        // Read new word from DMA
        try {
            uint wordsRead = ReadDma16Bit(1);
            
            if (wordsRead > 0 && _sb.Dma.Left > 0) {
                _sb.Dma.Left--;
                
                if (_sb.Dma.FirstTransfer) {
                    _sb.Dma.FirstTransfer = false;
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SOUNDBLASTER: First 16-bit DMA word read: 0x{Sample:X4}", _sb.Dma.Buf16[0]);
                    }
                }
                
                short sample = _sb.Dma.Buf16[0];
                
                // Handle signed/unsigned conversion
                if (!_sb.Dma.Sign) {
                    // Unsigned 16-bit: convert to signed by subtracting 32768
                    sample = (short)((ushort)sample - 32768);
                }
                
                float value = _sb.SpeakerEnabled ? sample : 0.0f;
                return new AudioFrame(value, value);
            }
        } catch (Exception ex) {
            _loggerService.Error(ex, "SOUNDBLASTER: Exception in 16-bit PCM decode");
        }
        
        return new AudioFrame(0.0f, 0.0f);
    }

    /// <summary>
    /// Calculates the number of DMA bytes needed per audio frame based on the current mode.
    /// </summary>
    private uint CalculateBytesPerFrame() {
        byte channels = _sb.Dma.Stereo ? (byte)2 : (byte)1;
        
        switch (_sb.Dma.Mode) {
            case DmaMode.Pcm8Bit:
                // 8-bit PCM: 1 byte per sample, samples = channels
                return channels;
                
            case DmaMode.Pcm16Bit:
            case DmaMode.Pcm16BitAliased:
                // 16-bit PCM: 2 bytes per sample, samples = channels
                return (uint)(channels * 2);
                
            case DmaMode.Adpcm2Bit:
                // 2-bit ADPCM: 1 byte decodes to 4 samples (mono only)
                // Average: 0.25 bytes per sample
                return channels == 1 ? 1u : channels;
                
            case DmaMode.Adpcm3Bit:
                // 3-bit ADPCM: 1 byte decodes to 3 samples (mono only)
                // Average: 0.33 bytes per sample
                return channels == 1 ? 1u : channels;
                
            case DmaMode.Adpcm4Bit:
                // 4-bit ADPCM: 1 byte decodes to 2 samples (mono only)
                // Average: 0.5 bytes per sample
                return channels == 1 ? 1u : channels;
                
            default:
                return 1;
        }
    }
    
    /// <summary>
    /// Raises the specified Sound Blaster IRQ and sets the appropriate pending flag.
    /// Mirrors DOSBox sb_raise_irq() function.
    /// Reference: src/hardware/audio/soundblaster.cpp lines 411-442
    /// </summary>
    private void RaiseIrq(SbIrq irqType) {
        switch (irqType) {
            case SbIrq.Irq8:
                _sb.Irq.Pending8Bit = true;
                _dualPic.ActivateIrq(_sb.Hw.Irq);
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("SOUNDBLASTER: Raised 8-bit IRQ {Irq}", _sb.Hw.Irq);
                }
                break;
                
            case SbIrq.Irq16:
                _sb.Irq.Pending16Bit = true;
                _dualPic.ActivateIrq(_sb.Hw.Irq);
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("SOUNDBLASTER: Raised 16-bit IRQ {Irq}", _sb.Hw.Irq);
                }
                break;
                
            case SbIrq.IrqMpu:
                // MPU-401 IRQ handling not implemented yet
                _loggerService.Warning("SOUNDBLASTER: MPU-401 IRQ not yet implemented");
                break;
        }
    }
    
    /// <summary>
    /// Called when a DMA transfer completes (all bytes read).
    /// This signals the interrupt to wake the DOS driver.
    /// </summary>
    private void OnDmaTransferComplete() {
        // Track DMA completion metrics
        _sb.Dma.DmaCompletionCount++;
        
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SOUNDBLASTER: DMA transfer complete #{Count}, signaling IRQ {Irq}; autoInit={AutoInit}", 
                _sb.Dma.DmaCompletionCount, _sb.Hw.Irq, _sb.Dma.AutoInit);
        }

        // Check if auto-init is enabled (continuous looping transfers)
        if (_sb.Dma.AutoInit) {
            // Reload DMA parameters for next transfer
            _sb.Dma.Left = _sb.Dma.AutoSize;
            _sb.Dma.FirstTransfer = true;
            
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("SOUNDBLASTER: Auto-init reload size={Size} channel={Channel} addr={Addr}", _sb.Dma.AutoSize, _sb.Dma.Channel?.ChannelNumber, _sb.Dma.Channel?.CurrentAddress);
            }
        } else {
            // Single transfer mode - stop after this transfer completes
            _sb.Mode = DspMode.None;
            _sb.Dma.Mode = DmaMode.None;
        }

        // Signal interrupt to wake the DOS driver
        // The driver responds by reading the status register and potentially
        // starting the next DMA transfer
        _dualPic.ActivateIrq(_sb.Hw.Irq);

        // Set pending interrupt flag (for status register readback)
        _sb.Irq.Pending8Bit = true;
    }

    private void MixerTickCallback(uint unusedTick) {
        if (!_dacChannel.IsEnabled) {
            _scheduler.AddEvent(MixerTickCallback, 1.0);
            return;
        }

        // Frame counter reset is handled by mixer blocks, not per-tick
        _scheduler.AddEvent(MixerTickCallback, 1.0);
    }

    // Command length tables - mirrors dsp_cmd_len_sb and dsp_cmd_len_sb16 from DOSBox
    // ReadByte, WriteByte, Reset, and other existing methods...
    public override byte ReadByte(ushort port) {
        switch (port - _config.BaseAddress) {
            case 0x0A:
                if (_outputData.Count > 0) {
                    return _outputData.Dequeue();
                }
                return 0;

            case 0x0E:
                if (_sb.Irq.Pending8Bit) {
                    _sb.Irq.Pending8Bit = false;
                    _dualPic.DeactivateIrq(_config.Irq);
                }
                return (byte)(_outputData.Count > 0 ? 0x80 : 0x00);

            case 0x0F:
                _sb.Irq.Pending16Bit = false;
                return 0xFF;

            case 0x04:
                return (byte)_hardwareMixer.CurrentAddress;

            case 0x05:
                return _hardwareMixer.ReadData();

            case 0x06:
            case 0x0C:
            default:
                return 0xFF;
        }
    }

    public override void WriteByte(ushort port, byte value) {
        switch (port - _config.BaseAddress) {
            case 0x06:
                DspDoReset(value);
                break;

            case 0x0C:
                DspDoWrite(value);
                break;

            case 0x04:
                _hardwareMixer.CurrentAddress = value;
                break;

            case 0x05:
                _hardwareMixer.Write(value);
                break;

            case 0x07:
                break;

            default:
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SoundBlaster: Unhandled port write {Port:X4}", port);
                }
                break;
        }
    }

    private void DspDoReset(byte value) {
        if (((value & 1) != 0) && (_sb.Dsp.State != DspState.Reset)) {
            DspReset();
            _sb.Dsp.State = DspState.Reset;
        } else if (((value & 1) == 0) && (_sb.Dsp.State == DspState.Reset)) {
            _sb.Dsp.State = DspState.ResetWait;
            _sb.Dsp.ResetTally++;
            DspFinishReset();
        }
    }

    private void DspFinishReset() {
        DspFlushData();
        DspAddData(0xaa);
        _sb.Dsp.State = DspState.Normal;
    }

    private void DspReset() {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SoundBlaster: DSP Reset");
        }

        _dualPic.DeactivateIrq(_config.Irq);
        DspChangeMode(DspMode.None);
        DspFlushData();

        _sb.Dsp.Cmd = 0;
        _sb.Dsp.CmdLen = 0;
        _sb.Dsp.CmdInPos = 0;
        _sb.Dsp.WriteStatusCounter = 0;

        _sb.Dma.Left = 0;
        _sb.Dma.SingleSize = 0;
        _sb.Dma.AutoSize = 0;
        _sb.Dma.Stereo = false;
        _sb.Dma.Sign = false;
        _sb.Dma.AutoInit = false;
        _sb.Dma.FirstTransfer = true;
        _sb.Dma.Mode = DmaMode.None;
        _sb.Dma.RemainSize = 0;

        _sb.Adpcm.Reference = 0;
        _sb.Adpcm.Stepsize = 0;
        _sb.Adpcm.HaveRef = false;
        _sb.DacState = new Dac();
        _sb.FreqHz = DefaultPlaybackRateHz;
        _sb.TimeConstant = 45;
        _sb.E2.Value = 0xaa;
        _sb.E2.Count = 0;

        _sb.Irq.Pending8Bit = false;
        _sb.Irq.Pending16Bit = false;
    }

    private void DspDoWrite(byte value) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("SB: DSP write value=0x{Value:X2} state={State}", value, _blasterState);
        }
        switch (_blasterState) {
            case BlasterState.WaitingForCommand:
                _currentCommand = value;
                _blasterState = BlasterState.ReadingCommand;
                _commandData.Clear();
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SB: Command 0x{Cmd:X2} received; expecting {Len} bytes", _currentCommand, _commandDataLength);
                }

                if (_config.SbType == SbType.Sb16) {
                    _commandDataLength = DspCommandLengthsSb16[value];
                } else {
                    _commandDataLength = DspCommandLengthsSb[value];
                }

                if (_commandDataLength == 0) {
                    ProcessCommand();
                }
                break;

            case BlasterState.ReadingCommand:
                _commandData.Add(value);
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("SB: Command 0x{Cmd:X2} param[{Count}] = 0x{Val:X2}", _currentCommand, _commandData.Count - 1, value);
                }
                if (_commandData.Count >= _commandDataLength) {
                    ProcessCommand();
                }
                break;
        }
    }

    private bool ProcessCommand() {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            string paramsHex = _commandData.Count > 0 ? string.Join(" ", _commandData.ConvertAll(b => b.ToString("X2"))) : string.Empty;
            _loggerService.Debug("SB: Processing command 0x{Cmd:X2} params={Params}", _currentCommand, paramsHex);
        }
        switch (_currentCommand) {
            case 0x04:
                // Sb16 ASP set mode register or DSP Status
                if (_config.SbType == SbType.Sb16) {
                    if ((_commandData[0] & 0xf1) == 0xf1) {
                        // asp_init_in_progress = true
                    }
                } else {
                    DspFlushData();
                    if (_config.SbType == SbType.SB2) {
                        DspAddData(0x88);
                    } else if (_config.SbType == SbType.SBPro1 || _config.SbType == SbType.SBPro2) {
                        DspAddData(0x7b);
                    } else {
                        DspAddData(0xff);
                    }
                }
                break;

            case 0x05: // SB16 ASP set codec parameter
                // Mirrors DOSBox soundblaster.cpp case 0x05
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("DSP Unhandled SB16ASP command 0x{Cmd:X2} (set codec parameter)", _currentCommand);
                }
                // No specific action needed - ASP commands are mostly unimplemented
                break;

            case 0x08: // SB16 ASP get version
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("DSP Unhandled SB16ASP command {Cmd:X} sub {Sub:X}",
                                        _currentCommand,
                                        _commandData.Count > 0 ? _commandData[0] : 0);
                }

                if (_config.SbType == SbType.Sb16 && _commandData.Count >= 1) {
                    switch (_commandData[0]) {
                        case 0x03:
                            DspAddData(0x18); // version ID (??)
                            break;

                        default:
                            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                                _loggerService.Debug("DSP Unhandled SB16ASP command {Cmd:X} sub {Sub:X}",
                                                    _currentCommand,
                                                    _commandData[0]);
                            }
                            break;
                    }
                }
                break;

            case 0x0e:
                // Sb16 ASP set register
                if (_config.SbType == SbType.Sb16) {
                    // asp_regs[_commandData[0]] = _commandData[1]
                }
                break;

            case 0x0f:
                // Sb16 ASP get register
                if (_config.SbType == SbType.Sb16) {
                    DspAddData(0x00);
                }
                break;

            case 0x10:
                // Direct DAC
                DspChangeMode(DspMode.Dac);
                
                // Wake up channel for Direct DAC writes - mirrors DOSBox soundblaster.cpp:1956
                if (MaybeWakeUp()) {
                    // If we're waking up, the DAC hasn't been running, so start with fresh DAC state
                    _sb.DacState = new Dac();
                }
                break;

            case 0x14:
            case 0x15:
            case 0x91:
                // Single Cycle 8-Bit DMA DAC
                // Size is in _commandData (command expects 2 bytes: size low, size high)
                if (_commandData.Count >= 2) {
                    _sb.Dma.Left = (uint)(1 + _commandData[0] + (_commandData[1] << 8));
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SB: Single-cycle 8-bit DMA size={Size}", _sb.Dma.Left);
                    }
                    DspPrepareDmaOld(DmaMode.Pcm8Bit, false, false);
                } else {
                    _loggerService.Warning("SOUNDBLASTER: Command 0x{Command:X2} missing size parameters", _currentCommand);
                }
                break;

            case 0x1c:
            case 0x90:
                // Auto Init 8-bit DMA
                // Uses AutoSize previously set by command 0x48
                if (_config.SbType > SbType.SB1) {
                    DspPrepareDmaOld(DmaMode.Pcm8Bit, true, false);
                } else {
                    _loggerService.Warning("SOUNDBLASTER: Auto-init DMA not supported on {SbType}", _config.SbType);
                }
                break;

            case 0x7f:
            case 0x1f:
                if (_config.SbType > SbType.SB1) {
                    if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                        _loggerService.Error("DSP:Unimplemented auto-init DMA ADPCM command {Cmd:X2}",
                                            _currentCommand);
                    }
                }
                break;

            case 0x20:
                DspAddData(0x7f); // Fake silent input for Creative parrot
                break;

            case 0x24:
                // Single Cycle 8-Bit DMA ADC
                _sb.Dma.Left = (uint)(1 + _commandData[0] + (_commandData[1] << 8));
                _sb.Dma.Sign = false;
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SB: Single-cycle 8-bit ADC size={Size}", _sb.Dma.Left);
                }
                DspPrepareDmaOld(DmaMode.Pcm8Bit, false, true);
                break;

            case 0x30:
            case 0x31:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("DSP:Unimplemented MIDI I/O command {Cmd:X2}",
                                        _currentCommand);
                }
                break;

            case 0x34:
            case 0x35:
            case 0x36:
            case 0x37:
                if (_config.SbType > SbType.SB1) {
                    if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                        _loggerService.Error("DSP:Unimplemented MIDI UART command {Cmd:X2}",
                                            _currentCommand);
                    }
                }
                break;

            case 0x38:
                // Write to SB MIDI Output
                if (_sb.MidiEnabled && _commandData.Count >= 1) {
                    if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                        _loggerService.Verbose("SB: MIDI output byte 0x{Byte:X2}", _commandData[0]);
                    }
                    // TODO: Forward to MIDI subsystem
                }
                break;

            case 0x40:
                // Set Timeconstant
                if (_commandData.Count >= 1) {
                    _sb.FreqHz = (uint)(1000000 / (256 - _commandData[0]));
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SB: Timeconstant set tc=0x{Tc:X2} rate={Rate}Hz", _commandData[0], _sb.FreqHz);
                    }
                }
                break;

            case 0x41:
            case 0x42:
                // Set Output/Input Samplerate (Sb16)
                if (_config.SbType == SbType.Sb16) {
                    if (_commandData.Count >= 2) {
                        _sb.FreqHz = (uint)((_commandData[0] << 8) | _commandData[1]);
                    }
                }
                break;

            case 0x48:
                // Set DMA Block Size
                if (_config.SbType > SbType.SB1) {
                    _sb.Dma.AutoSize = (uint)(1 + _commandData[0] + (_commandData[1] << 8));
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SB: DMA AutoSize set to {AutoSize}", _sb.Dma.AutoSize);
                    }
                }
                break;

            case 0x16:
            case 0x17:
                // Single Cycle 2-bit ADPCM
                // 0x17 includes reference byte
                if (_currentCommand == 0x17) {
                    _sb.Adpcm.HaveRef = true;
                }
                _sb.Dma.Left = (uint)(1 + _commandData[0] + (_commandData[1] << 8));
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SB: 2-bit ADPCM size={Size} haveRef={HaveRef}", _sb.Dma.Left, _sb.Adpcm.HaveRef);
                }
                DspPrepareDmaOld(DmaMode.Adpcm2Bit, false, false);
                break;

            case 0x74:
            case 0x75:
                // Single Cycle 4-bit ADPCM
                if (_currentCommand == 0x75) {
                    _sb.Adpcm.HaveRef = true;
                }
                DspPrepareDmaOld(DmaMode.Adpcm4Bit, false, false);
                break;

            case 0x76:
            case 0x77:
                // Single Cycle 3-bit ADPCM
                if (_currentCommand == 0x77) {
                    _sb.Adpcm.HaveRef = true;
                }
                DspPrepareDmaOld(DmaMode.Adpcm3Bit, false, false);
                break;

            case 0x7d:
                // Auto Init 4-bit ADPCM
                if (_config.SbType > SbType.SB1) {
                    _sb.Adpcm.HaveRef = true;
                    DspPrepareDmaOld(DmaMode.Adpcm4Bit, true, false);
                }
                break;

            case 0x80:
                // Silence DAC
                break;

            case 0x98:
            case 0x99: // Documented only for DSP 2.x and 3.x
            case 0xa0:
            case 0xa8: // Documented only for DSP 3.x
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("DSP:Unimplemented input command {Cmd:X2}",
                                        _currentCommand);
                }
                break;

            // Generic 8/16-bit DMA commands (SB16 only) - 0xB0-0xCF
            // Reference: DOSBox soundblaster.cpp lines 2068-2097
            case 0xb0: case 0xb1: case 0xb2: case 0xb3:
            case 0xb4: case 0xb5: case 0xb6: case 0xb7:
            case 0xb8: case 0xb9: case 0xba: case 0xbb:
            case 0xbc: case 0xbd: case 0xbe: case 0xbf:
            case 0xc0: case 0xc1: case 0xc2: case 0xc3:
            case 0xc4: case 0xc5: case 0xc6: case 0xc7:
            case 0xc8: case 0xc9: case 0xca: case 0xcb:
            case 0xcc: case 0xcd: case 0xce: case 0xcf:
                if (_config.SbType == SbType.Sb16 && _commandData.Count >= 3) {
                    // Parse command byte and mode byte
                    // Command bit 4 (0x10): 0=8-bit, 1=16-bit
                    // Mode byte bit 4 (0x10): signed data
                    // Mode byte bit 5 (0x20): stereo
                    // Command bit 2 (0x04): FIFO enable (we don't emulate FIFO delay)
                    
                    _sb.Dma.Sign = (_commandData[0] & 0x10) != 0;
                    bool is16Bit = (_currentCommand & 0x10) != 0;
                    bool autoInit = (_currentCommand & 0x04) != 0;
                    bool stereo = (_commandData[0] & 0x20) != 0;
                    
                    // Length is in bytes (for 8-bit) or words (for 16-bit)
                    uint length = (uint)(1 + _commandData[1] + (_commandData[2] << 8));
                    
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SB16: Generic DMA cmd=0x{Cmd:X2} mode=0x{Mode:X2} " +
                            "16bit={Is16Bit} autoInit={AutoInit} stereo={Stereo} sign={Sign} len={Length}",
                            _currentCommand, _commandData[0], is16Bit, autoInit, stereo, _sb.Dma.Sign, length);
                    }
                    
                    DspPrepareDmaNew(is16Bit ? DmaMode.Pcm16Bit : DmaMode.Pcm8Bit, length, autoInit, stereo);
                } else if (_config.SbType != SbType.Sb16) {
                    _loggerService.Warning("SOUNDBLASTER: Generic DMA commands (0xB0-0xCF) require SB16");
                }
                break;

            case 0xd0:
                // Halt 8-bit DMA
                _sb.Mode = DspMode.DmaPause;
                break;

            case 0xd1:
                // Enable Speaker
                SetSpeakerEnabled(true);
                break;

            case 0xd3:
                // Disable Speaker
                SetSpeakerEnabled(false);
                break;

            case 0xd4:
                // Continue DMA 8-bit
                if (_sb.Mode == DspMode.DmaPause) {
                    _sb.Mode = DspMode.DmaMasked;
                }
                break;

            case 0xd5:
                // Halt 16-bit DMA
                if (_config.SbType == SbType.Sb16) {
                    _sb.Mode = DspMode.DmaPause;
                }
                break;

            case 0xd6:
                // Continue DMA 16-bit
                if (_config.SbType == SbType.Sb16) {
                    if (_sb.Mode == DspMode.DmaPause) {
                        _sb.Mode = DspMode.DmaMasked;
                    }
                }
                break;

            case 0xd8:
                // Speaker status
                if (_config.SbType > SbType.SB1) {
                    DspFlushData();
                    if (_sb.SpeakerEnabled) {
                        DspAddData(0xff);
                        _sb.Dsp.WarmupRemainingMs = 0;
                    } else {
                        DspAddData(0x00);
                    }
                }
                break;

            case 0xd9:
                // Exit Autoinitialize 16-bit
                if (_config.SbType == SbType.Sb16) {
                    _sb.Dma.AutoInit = false;
                }
                break;

            case 0xda:
                // Exit Autoinitialize 8-bit
                if (_config.SbType > SbType.SB1) {
                    _sb.Dma.AutoInit = false;
                }
                break;

            case 0xe0:
                // DSP Identification
                DspFlushData();
                DspAddData((byte)~_commandData[0]);
                break;

            case 0xe1:
                // Get DSP Version
                DspFlushData();
                switch (_config.SbType) {
                    case SbType.SB1:
                        DspAddData(0x01);
                        DspAddData(0x05);
                        break;
                    case SbType.SB2:
                        DspAddData(0x02);
                        DspAddData(0x01);
                        break;
                    case SbType.SBPro1:
                        DspAddData(0x03);
                        DspAddData(0x00);
                        break;
                    case SbType.SBPro2:
                        if (_sb.EssType != EssType.None) {
                            DspAddData(0x03);
                            DspAddData(0x01);
                        } else {
                            DspAddData(0x03);
                            DspAddData(0x02);
                        }
                        break;
                    case SbType.Sb16:
                        DspAddData(0x04);
                        DspAddData(0x05);
                        break;
                }
                break;

            case 0xe2:
                // Weird DMA identification write routine
                for (int i = 0; i < 8; i++) {
                    if (((_commandData[0] >> i) & 0x01) != 0) {
                        _sb.E2.Value += E2IncrTable[_sb.E2.Count % 4][i];
                    }
                }
                _sb.E2.Value += E2IncrTable[_sb.E2.Count % 4][8];
                _sb.E2.Count++;
                break;

            case 0xe3:
                // DSP Copyright
                DspFlushData();
                if (_sb.EssType != EssType.None) {
                    DspAddData(0);
                } else {
                    string copyright = "COPYRIGHT (C) CREATIVE TECHNOLOGY LTD, 1992.";
                    foreach (char c in copyright) {
                        DspAddData((byte)c);
                    }
                }
                break;

            case 0xe4:
                // Write Test Register
                _sb.Dsp.TestRegister = _commandData[0];
                break;

            case 0xe7: // ESS detect/read config
                switch (_sb.EssType) {
                    case EssType.None:
                        break;

                    case EssType.Es1688:
                        DspFlushData();
                        // Determined via Windows driver debugging.
                        DspAddData(0x68);
                        DspAddData(0x80 | 0x09);
                        break;
                }
                break;

            case 0xe8:
                // Read Test Register
                DspFlushData();
                DspAddData(_sb.Dsp.TestRegister);
                break;

            case 0xf2:
                // Trigger 8bit IRQ
                RaiseInterruptRequest();
                break;

            case 0xf3:
                // Trigger 16bit IRQ
                if (_config.SbType == SbType.Sb16) {
                    _sb.Irq.Pending16Bit = true;
                    _dualPic.ActivateIrq(_config.Irq);
                }
                break;

            case 0xf8:
                // Undocumented, pre-Sb16 only
                DspFlushData();
                DspAddData(0);
                break;

            case 0xf9:
                // Sb16 ASP unknown function
                if (_config.SbType == SbType.Sb16) {
                    switch (_commandData[0]) {
                        case 0x0b: DspAddData(0x00); break;
                        case 0x0e: DspAddData(0xff); break;
                        case 0x0f: DspAddData(0x07); break;
                        case 0x23: DspAddData(0x00); break;
                        case 0x24: DspAddData(0x00); break;
                        case 0x2b: DspAddData(0x00); break;
                        case 0x2c: DspAddData(0x00); break;
                        case 0x2d: DspAddData(0x00); break;
                        case 0x37: DspAddData(0x38); break;
                        default: DspAddData(0x00); break;
                    }
                }
                break;

            default:
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("SoundBlaster: Unimplemented DSP command {Command:X2}", _currentCommand);
                }
                _blasterState = BlasterState.WaitingForCommand;
                return false;
        }

        _blasterState = BlasterState.WaitingForCommand;
        return true;
    }

    private void DspChangeMode(DspMode mode) {
        if (_sb.Mode == mode) {
            return;
        }
        if (mode == DspMode.Dac) {
            _sb.DacState = new Dac();
        }
        _sb.Mode = mode;
    }

    private void DspPrepareDmaOld(DmaMode mode, bool autoInit, bool recordMode) {
        // Setup DMA transfer with given mode and parameters
        // Called when a DMA command (0x14, 0x1c, 0x24, etc) is executed
        
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SOUNDBLASTER: DspPrepareDmaOld - Mode={Mode}, AutoInit={AutoInit}, RecordMode={RecordMode}, Left={Left}, AutoSize={AutoSize}",
                mode, autoInit, recordMode, _sb.Dma.Left, _sb.Dma.AutoSize);
        }
        
        _sb.Dma.Mode = mode;
        _sb.Dma.AutoInit = autoInit;
        
        // Determine bit depth based on DMA mode
        _sb.Dma.Bits = mode switch {
            DmaMode.Pcm8Bit => 8,
            DmaMode.Pcm16Bit or DmaMode.Pcm16BitAliased => 16,
            DmaMode.Adpcm2Bit => 2,
            DmaMode.Adpcm3Bit => 3,
            DmaMode.Adpcm4Bit => 4,
            _ => 8
        };
        
        // For single-cycle DMA, Left should already be set by the DMA command
        // For auto-init, use AutoSize
        if (autoInit) {
            _sb.Dma.Left = _sb.Dma.AutoSize;
        }
        
        // Validate that we have a transfer size
        if (_sb.Dma.Left == 0) {
            _loggerService.Warning("SOUNDBLASTER: DMA prepared with zero transfer size (AutoInit={AutoInit}, Mode={Mode}). DMA will not start.",
                autoInit, mode);
            _sb.Dma.Mode = DmaMode.None;
            return;
        }
        
        // Select appropriate DMA channel based on bit depth
        // 16-bit modes use the secondary (high) DMA channel, 8-bit modes use primary (low)
        DmaChannel? dmaChannel = null;
        if (_sb.Dma.Bits == 16) {
            dmaChannel = _secondaryDmaChannel;
        } else {
            dmaChannel = _primaryDmaChannel;
        }
        
        if (dmaChannel is null) {
            _loggerService.Warning("SOUNDBLASTER: DMA channel not available for {Bits}-bit mode", _sb.Dma.Bits);
            _sb.Dma.Mode = DmaMode.None;
            return;
        }
        
        _sb.Dma.Channel = dmaChannel;
        _sb.Dma.FirstTransfer = true;
        
        // Register DMA callback - mirrors DOSBox RegisterCallback(dsp_dma_callback)
        // Reference: src/hardware/audio/soundblaster.cpp line 1618
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SOUNDBLASTER: Registering DMA callback on channel {Channel}", dmaChannel.ChannelNumber);
        }
        dmaChannel.RegisterCallback(DspDmaCallback);
        
        // Update channel sample rate to match DMA rate
        // This ensures the mixer can properly resample if needed
        int dmaRateHz = (int)_sb.FreqHz;
        if (dmaRateHz > 0) {
            // Validate DMA rate is reasonable
            ValidateDmaRate(dmaRateHz);
            
            if (dmaRateHz != _dacChannel.GetSampleRate()) {
                _dacChannel.SetSampleRate(dmaRateHz);
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SOUNDBLASTER: Updated DAC channel sample rate to {Rate} Hz", dmaRateHz);
                }
            }
        }
        
        // Transition DSP mode to DMA if not already in DMA mode
        if (_sb.Mode != DspMode.Dma) {
            DspChangeMode(DspMode.Dma);
        }
        
        // Wake up the channel now that DMA is ready to play
        // Mirrors DOSBox soundblaster.cpp:840
        MaybeWakeUp();
        
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SOUNDBLASTER: DMA prepared - Mode={Mode}, AutoInit={AutoInit}, Bits={Bits}, Left={Left}, Channel={Channel}, Rate={Rate}Hz",
                mode, autoInit, _sb.Dma.Bits, _sb.Dma.Left, dmaChannel.ChannelNumber, dmaRateHz);
        }
    }
    
    /// <summary>
    /// Setup DMA transfer using new-style SB16 commands (0xB0-0xCF).
    /// Mirrors dsp_prepare_dma_new() from DOSBox.
    /// Reference: src/hardware/audio/soundblaster.cpp lines 1646-1690
    /// </summary>
    private void DspPrepareDmaNew(DmaMode mode, uint length, bool autoInit, bool stereo) {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SOUNDBLASTER: DspPrepareDmaNew - Mode={Mode}, Length={Length}, AutoInit={AutoInit}, Stereo={Stereo}",
                mode, length, autoInit, stereo);
        }
        
        DmaMode newMode = mode;
        uint newLength = length;
        
        // Select appropriate DMA channel and adjust mode/length if needed
        if (mode == DmaMode.Pcm16Bit) {
            // Try to use 16-bit DMA channel first
            if (_secondaryDmaChannel is not null) {
                _sb.Dma.Channel = _secondaryDmaChannel;
            } else {
                // Fall back to 8-bit DMA channel in aliased mode
                _sb.Dma.Channel = _primaryDmaChannel;
                newMode = DmaMode.Pcm16BitAliased;
                // In aliased mode, sample length is specified as number of 16-bit samples
                // but we need to double the 8-bit DMA buffer length
                newLength *= 2;
            }
        } else {
            // 8-bit modes always use primary DMA channel
            _sb.Dma.Channel = _primaryDmaChannel;
        }
        
        if (_sb.Dma.Channel is null) {
            _loggerService.Warning("SOUNDBLASTER: No DMA channel available for mode {Mode}", mode);
            return;
        }
        
        // Set stereo flag
        _sb.Dma.Stereo = stereo;
        
        // Set the length to the correct register depending on mode
        if (autoInit) {
            _sb.Dma.AutoSize = newLength;
            _sb.Dma.Left = newLength;
        } else {
            _sb.Dma.SingleSize = newLength;
            _sb.Dma.Left = newLength;
        }
        
        // Setup the DMA transfer
        _sb.Dma.Mode = newMode;
        _sb.Dma.AutoInit = autoInit;
        _sb.Dma.FirstTransfer = true;
        
        // Register DMA callback - mirrors DOSBox RegisterCallback(dsp_dma_callback)
        // Reference: src/hardware/audio/soundblaster.cpp line 2156
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SOUNDBLASTER: Registering DMA callback on channel {Channel} (new-style)", _sb.Dma.Channel.ChannelNumber);
        }
        _sb.Dma.Channel.RegisterCallback(DspDmaCallback);
        
        // Determine bit depth
        _sb.Dma.Bits = newMode switch {
            DmaMode.Pcm8Bit => 8,
            DmaMode.Pcm16Bit or DmaMode.Pcm16BitAliased => 16,
            _ => 8
        };
        
        // Update channel sample rate to match DMA rate
        // This ensures the mixer can properly resample if needed
        int dmaRateHz = (int)_sb.FreqHz;
        if (dmaRateHz > 0) {
            // Validate DMA rate is reasonable
            ValidateDmaRate(dmaRateHz);
            
            if (dmaRateHz != _dacChannel.GetSampleRate()) {
                _dacChannel.SetSampleRate(dmaRateHz);
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SOUNDBLASTER: Updated DAC channel sample rate to {Rate} Hz (stereo={Stereo})", dmaRateHz, stereo);
                }
            }
        }
        
        // Transition to DMA mode
        if (_sb.Mode != DspMode.Dma) {
            DspChangeMode(DspMode.Dma);
        }
        
        // Wake up the channel now that DMA is ready to play
        // Mirrors DOSBox soundblaster.cpp:840
        MaybeWakeUp();
        
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SOUNDBLASTER: DMA prepared (new) - Mode={Mode}, AutoInit={AutoInit}, " +
                "Stereo={Stereo}, Bits={Bits}, Left={Left}, Channel={Channel}, Rate={Rate}Hz",
                newMode, autoInit, stereo, _sb.Dma.Bits, _sb.Dma.Left, _sb.Dma.Channel.ChannelNumber, dmaRateHz);
        }
    }

    /// <summary>
    /// Validates DMA transfer rate against mixer capabilities.
    /// Mirrors DOSBox Staging rate validation logic.
    /// </summary>
    private void ValidateDmaRate(int dmaRateHz) {
        int mixerRateHz = _mixer.SampleRateHz;
        
        // Check if DMA rate is reasonable
        if (dmaRateHz < MinPlaybackRateHz) {
            _loggerService.Warning("SOUNDBLASTER: DMA rate {DmaRate} Hz is below minimum {MinRate} Hz", 
                dmaRateHz, MinPlaybackRateHz);
        }
        
        // Warn if DMA rate significantly exceeds mixer rate
        // High upsampling ratios can cause performance issues
        if (dmaRateHz > mixerRateHz * 2) {
            _loggerService.Warning("SOUNDBLASTER: DMA rate {DmaRate} Hz is more than 2x mixer rate {MixerRate} Hz. " +
                "This may cause performance issues and audio glitches.", 
                dmaRateHz, mixerRateHz);
        }
        
        // Log rate relationship for debugging
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            double ratio = (double)dmaRateHz / mixerRateHz;
            string relationship = ratio > 1.0 ? "upsampling" : ratio < 1.0 ? "downsampling" : "matching";
            _loggerService.Debug("SOUNDBLASTER: DMA rate {DmaRate} Hz vs mixer rate {MixerRate} Hz (ratio: {Ratio:F2}x, {Relationship})",
                dmaRateHz, mixerRateHz, ratio, relationship);
        }
    }
    
    private void SetSpeakerEnabled(bool enabled) {
        if (_sb.Type == SbType.Sb16) {
            return;
        }
        if (_sb.SpeakerEnabled == enabled) {
            return;
        }
        _sb.SpeakerEnabled = enabled;
    }

    private bool ShouldUseHighDmaChannel() {
        return _config.SbType == SbType.Sb16 &&
               _config.HighDma >= 5 &&
               _config.HighDma != _config.LowDma;
    }

    private void InitSpeakerState() {
        if (_sb.Type == SbType.Sb16) {
            bool isColdStart = _sb.Dsp.ResetTally <= DspInitialResetLimit;
            _sb.Dsp.WarmupRemainingMs = isColdStart ? _sb.Dsp.ColdWarmupMs : _sb.Dsp.HotWarmupMs;
            _sb.SpeakerEnabled = true;
        } else {
            _sb.SpeakerEnabled = false;
        }
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        int basePort = _config.BaseAddress;
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x06), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x0A), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x0C), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x0E), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x04), this);
        ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x05), this);
        if (_sb.Type == SbType.Sb16) {
            ioPortDispatcher.AddIOPortHandler((ushort)(basePort + 0x0F), this);
        }
    }
    
    /// <summary>
    /// Called when the Sound Blaster loses control of its DMA channel.
    /// Mirrors DOSBox eviction callback pattern for DMA channel reservation.
    /// </summary>
    private void OnDmaChannelEvicted() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("SOUNDBLASTER: DMA channel evicted - stopping audio");
        }
        
        // Stop any active DMA transfer
        _sb.Mode = DspMode.None;
        _sb.Dma.Mode = DmaMode.None;
        _sb.Dma.Left = 0;
        _sb.Dma.Channel = null;
        
        // Clear pending IRQs
        _sb.Irq.Pending8Bit = false;
        _sb.Irq.Pending16Bit = false;
        _dualPic.DeactivateIrq(_config.Irq);
    }

    private void DspFlushData() {
        _outputData.Clear();
    }

    private void DspAddData(byte value) {
        _outputData.Enqueue(value);
    }

    public void RaiseInterruptRequest() {
        _sb.Irq.Pending8Bit = true;
        _dualPic.ActivateIrq(_config.Irq);
    }

    public SbType SbTypeProperty => _config.SbType;
    public byte IRQ => _config.Irq;

    public string BlasterString {
        get {
            string highChannelSegment = ShouldUseHighDmaChannel() ? $" H{_config.HighDma}" : string.Empty;
            return $"A{_config.BaseAddress:X3} I{_config.Irq} D{_config.LowDma}{highChannelSegment} T{(int)_config.SbType}";
        }
    }



}
