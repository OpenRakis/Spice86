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
using Spice86.Shared.Utils;

using System;
using System.Collections.Generic;
using System.Linq;

public class SoundBlaster : DefaultIOPortHandler, IRequestInterrupt, IBlasterEnvVarProvider {
    private const int DmaBufSize = 1024;
    private const int DspBufSize = 64;
    private const int MinPlaybackRateHz = 5000;
    private const ushort DefaultPlaybackRateHz = 22050;
    private const int SbShift = 14;
    private const ushort SbShiftMask = (1 << SbShift) - 1;
    private const byte MinAdaptiveStepSize = 0;
    private const byte DspInitialResetLimit = 4;

    private enum DspState { Reset, ResetWait, Normal, HighSpeed }
    private enum DmaMode { None, Adpcm2Bit, Adpcm3Bit, Adpcm4Bit, Pcm8Bit, Pcm16Bit, Pcm16BitAliased }
    private enum DspMode { None, Dac, Dma, DmaPause, DmaMasked }
    private enum EssType { None, Es1688 }
    private enum SbIrq { Irq8, Irq16, IrqMpu }
    private enum BlasterState { WaitingForCommand, ResetRequest, Resetting, ReadingCommand }

    /// <summary>
    /// Callback timing type for audio frame generation.
    /// Reference: src/hardware/audio/soundblaster.cpp enum class TimingType and class CallbackType
    /// </summary>
    private enum TimingType { None, PerTick, PerFrame }

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

        /// <summary>
        /// Reference: src/hardware/audio/soundblaster.cpp class Dac
        /// </summary>
        public class DacState {
            private readonly SbInfo _sb;

            public DacState(SbInfo sb) {
                _sb = sb;
            }

            /// <summary>
            /// Reference: src/hardware/audio/soundblaster.cpp Dac::RenderFrame()
            /// </summary>
            public AudioFrame RenderFrame() {
                // return sb.speaker_enabled ? lut_u8to16[sb.dsp.in.data[0]] : 0.0f;
                if (_sb.SpeakerEnabled && _sb.Dsp.In.Data.Length > 0) {
                    float sample = LookupTables.ToUnsigned8(_sb.Dsp.In.Data[0]);
                    return new AudioFrame(sample, sample);
                }
                return new AudioFrame(0.0f, 0.0f);
            }
        }

        private DacState? _dac;
        public DacState Dac => _dac ??= new DacState(this);
    }

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

    private static (byte[], byte, ushort) DecodeAdpcm2Bit(byte data, byte reference, ushort stepsize) {
        byte refCopy = reference;
        ushort stepsizeCopy = stepsize;
        byte[] samples = DecodeAdpcm2Bit(data, ref refCopy, ref stepsizeCopy);
        return (samples, refCopy, stepsizeCopy);
    }

    private static (byte[], byte, ushort) DecodeAdpcm3Bit(byte data, byte reference, ushort stepsize) {
        byte refCopy = reference;
        ushort stepsizeCopy = stepsize;
        byte[] samples = DecodeAdpcm3Bit(data, ref refCopy, ref stepsizeCopy);
        return (samples, refCopy, stepsizeCopy);
    }

    private static (byte[], byte, ushort) DecodeAdpcm4Bit(byte data, byte reference, ushort stepsize) {
        byte refCopy = reference;
        ushort stepsizeCopy = stepsize;
        byte[] samples = DecodeAdpcm4Bit(data, ref refCopy, ref stepsizeCopy);
        return (samples, refCopy, stepsizeCopy);
    }

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

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("SOUNDBLASTER: ReadDma8Bit - Read {ActualBytes}/{RequestedBytes} bytes from channel {Channel}",
                    actualBytesRead, clampedBytes, _sb.Dma.Channel.ChannelNumber);
            }

            return actualBytesRead;
        } catch (ArgumentOutOfRangeException ex) {
            _loggerService.Error(ex, "SOUNDBLASTER: ArgumentOutOfRangeException during 8-bit DMA read");
            return 0;
        } catch (ArgumentException ex) {
            _loggerService.Error(ex, "SOUNDBLASTER: ArgumentException during 8-bit DMA read");
            return 0;
        }
    }

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

            // Calculate bytes read for DMA
            uint bytesRead = actualWordsRead * (is16BitChannel ? 2u : 1u);

            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose("SOUNDBLASTER: ReadDma16Bit - Read {ActualWords}/{RequestedWords} words ({BytesRead} bytes) from channel {Channel}",
                    actualWordsRead, clampedWords, bytesRead, _sb.Dma.Channel.ChannelNumber);
            }

            return actualWordsRead;
        } catch (ArgumentOutOfRangeException ex) {
            _loggerService.Error(ex, "SOUNDBLASTER: Exception during 16-bit DMA read");
            return 0;
        } catch (ArgumentException ex) {
            _loggerService.Error(ex, "SOUNDBLASTER: Exception during 16-bit DMA read");
            return 0;
        }
    }

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
    private readonly Opl _opl;
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly HardwareMixer _hardwareMixer;

    private readonly RWQueue<AudioFrame> _outputQueue = new(4096);

    private readonly AudioFrame[] _enqueueBatch = new AudioFrame[4096];
    private int _enqueueBatchCount;

    private readonly AudioFrame[] _dequeueBatch = new AudioFrame[4096];

    private BlasterState _blasterState = BlasterState.WaitingForCommand;

    /// <summary>
    /// ASP/CSP register storage for SB16 Advanced Signal Processing.
    /// Reference: src/hardware/audio/soundblaster.cpp static uint8_t asp_regs[256]
    /// </summary>
    private readonly byte[] _aspRegs = new byte[256];

    /// <summary>
    /// Tracks whether ASP initialization is in progress (toggling register 0x83).
    /// Reference: src/hardware/audio/soundblaster.cpp static bool asp_init_in_progress
    /// </summary>
    private bool _aspInitInProgress;

    private double _lastDmaCallbackTime;

    private float _frameCounter = 0.0f;

    private int _framesNeeded = 0;

    // Tracks frames added during current tick to prevent over-generation
    // Reference: src/hardware/audio/soundblaster.cpp line 291 (static int frames_added_this_tick)
    private int _framesAddedThisTick = 0;

    /// <summary>
    /// Current callback timing type for audio frame generation.
    /// Reference: src/hardware/audio/soundblaster.cpp CallbackType class
    /// </summary>
    private TimingType _timingType = TimingType.None;

    /// <summary>
    /// Maximum DMA base count for using per-frame callback.
    /// If base_count is less than or equal to this value, use SetPerFrame() for fine-grained timing.
    /// Reference: src/hardware/audio/soundblaster.cpp constexpr MaxSingleFrameBaseCount
    /// Value is 3 (sizeof(short) * 2 - 1)
    /// </summary>
    private const int MaxSingleFrameBaseCount = sizeof(short) * 2 - 1;

    public SoundBlaster(
        IOPortDispatcher ioPortDispatcher,
        State state,
        DmaBus dmaBus,
        DualPic dualPic,
        Mixer mixer,
        Opl opl,
        ILoggerService loggerService,
        EmulationLoopScheduler scheduler,
        IEmulatedClock clock,
        SoundBlasterHardwareConfig soundBlasterHardwareConfig)
        : base(state, false, loggerService) {

        // Lock mixer thread during construction to prevent concurrent modifications
        // Reference: src/hardware/audio/soundblaster.cpp:3858
        mixer.LockMixerThread();

        _config = soundBlasterHardwareConfig;
        _dualPic = dualPic;
        _mixer = mixer;
        _opl = opl;
        _scheduler = scheduler;
        _clock = clock;

        _primaryDmaChannel = dmaBus.GetChannel(_config.LowDma)
            ?? throw new InvalidOperationException($"DMA channel {_config.LowDma} unavailable for Sound Blaster.");

        _secondaryDmaChannel = ShouldUseHighDmaChannel()
            ? dmaBus.GetChannel(_config.HighDma)
            : null;

        if (_primaryDmaChannel.ChannelNumber == 4 ||
            (_secondaryDmaChannel is not null && _secondaryDmaChannel.ChannelNumber == 4)) {
            throw new InvalidOperationException("Sound Blaster cannot attach to cascade DMA channel 4.");
        }

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

        // Reference: channel = MIXER_AddChannel(&SoundBlaster::MixerCallback, ...)
        _dacChannel = _mixer.AddChannel(MixerCallback, (int)_sb.FreqHz, "SoundBlasterDAC", dacFeatures);

        // Configure Zero-Order-Hold upsampler and resample method for SB Pro 2 only
        // ZOH upsampler provides vintage DAC sound characteristic
        // Native DAC rate is 49716 Hz, then resampled to host rate (typically 48000 Hz)
        if (_config.SbType == SbType.SBPro2) {
            const int NativeDacRateHz = 49716;
            _dacChannel.SetZeroOrderHoldUpsamplerTargetRate(NativeDacRateHz);
            _dacChannel.SetResampleMethod(ResampleMethod.ZeroOrderHoldAndResample);
        }

        _hardwareMixer = new HardwareMixer(soundBlasterHardwareConfig, _dacChannel, opl.MixerChannel, loggerService);

        // Reference: src/hardware/audio/soundblaster.cpp SoundBlaster constructor lines 3660-3664
        // Must set Normal state BEFORE dsp_reset() so first game reset triggers properly.
        // DspState defaults to Reset (enum value 0) in C#, but DOSBox explicitly sets Normal.
        _sb.Dsp.State = DspState.Normal;
        _sb.Dsp.Out.LastVal = 0xAA;

        InitPortHandlers(ioPortDispatcher);

        // Reference: src/hardware/audio/soundblaster.cpp SoundBlaster constructor lines 3671-3673
        // Initialize ASP registers before dsp_reset()
        _aspRegs[5] = 0x01;
        _aspRegs[9] = 0xF8;

        // Reference: src/hardware/audio/soundblaster.cpp SoundBlaster constructor line 3682
        // Full DSP reset (includes InitSpeakerState at the end)
        DspReset();

        // Reference: src/hardware/audio/soundblaster.cpp SoundBlaster constructor line 3684
        _hardwareMixer.Reset();

        _dualPic.SetIrqMask(_config.Irq, false);

        // Note: Unlike DOSBox which registers the tick handler at startup, we dynamically
        // switch between per-tick and per-frame callbacks based on DMA base count.
        // The callback is activated when DMA is unmasked in DspDmaCallback.
        // Reference: src/hardware/audio/soundblaster.cpp lines 850-856

        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            string highDmaSegment = ShouldUseHighDmaChannel() ? $", high DMA {_config.HighDma}" : string.Empty;
            _loggerService.Information(
                "SoundBlaster: Initialized {SbType} on port {Port:X3}, IRQ {Irq}, DMA {LowDma}{HighDmaSegment}",
                _sb.Type, _sb.Hw.Base, _sb.Hw.Irq, _sb.Hw.Dma8, highDmaSegment);
        }

        // Unlock mixer thread after construction completes
        // Reference: src/hardware/audio/soundblaster.cpp:3860
        mixer.UnlockMixerThread();
    }

    /// <summary>
    /// Mixer callback - called by mixer when it needs audio frames.
    /// Reference: src/hardware/audio/soundblaster.cpp SoundBlaster::MixerCallback()
    /// </summary>
    private void MixerCallback(int frames_requested) {
        int queueSize = _outputQueue.Size;
        int shortage = Math.Max(frames_requested - queueSize, 0);
        System.Threading.Interlocked.Exchange(ref _framesNeeded, shortage);

        int maxFrames = Math.Min(frames_requested, _dequeueBatch.Length);
        int frames_received = _outputQueue.BulkDequeue(_dequeueBatch, maxFrames);

        if (frames_received > 0) {
            _dacChannel.AddAudioFrames(_dequeueBatch.AsSpan(0, frames_received));
        }

        if (frames_received < frames_requested) {
            _dacChannel.AddSilence();
        }
    }

    private bool MaybeWakeUp() {
        return _dacChannel.WakeUp();
    }

    private void DspDmaCallback(DmaChannel channel, DmaChannel.DmaEvent dmaEvent) {
        switch (dmaEvent) {
            case DmaChannel.DmaEvent.ReachedTerminalCount:
                break;

            case DmaChannel.DmaEvent.IsMasked:
                if (_sb.Mode == DspMode.Dma) {
                    double currentTime = _clock.FullIndex;
                    double elapsedTime = currentTime - _lastDmaCallbackTime;

                    if (elapsedTime > 0 && _sb.Dma.Rate > 0) {
                        uint samplesToGenerate = (uint)(_sb.Dma.Rate * elapsedTime / 1000.0);

                        if (samplesToGenerate > _sb.Dma.Min) {
                            samplesToGenerate = _sb.Dma.Min;
                        }

                        uint minSize = _sb.Dma.Mul >> SbShift;
                        if (minSize == 0) {
                            minSize = 1;
                        }
                        minSize *= 2;

                        if (_sb.Dma.Left > minSize) {
                            if (samplesToGenerate > (_sb.Dma.Left - minSize)) {
                                samplesToGenerate = _sb.Dma.Left - minSize;
                            }

                            if (!_sb.Dma.AutoInit && _sb.Dma.Left <= _sb.Dma.Min) {
                                samplesToGenerate = 0;
                            }

                            if (samplesToGenerate > 0) {
                                PlayDmaTransfer(samplesToGenerate);
                            }
                        }
                    }

                    _sb.Mode = DspMode.DmaMasked;
                }
                break;

            case DmaChannel.DmaEvent.IsUnmasked:
                if (_sb.Mode == DspMode.DmaMasked && _sb.Dma.Mode != DmaMode.None) {
                    DspChangeMode(DspMode.Dma);
                    FlushRemainingDmaTransfer();
                    MaybeWakeUp();

                    if (channel.BaseCount <= MaxSingleFrameBaseCount) {
                        SetCallbackPerFrame();
                    } else {
                        SetCallbackPerTick();
                    }
                }
                break;
        }
    }

    /// <summary>
    /// DMA callback for E2 identification write routine.
    /// Reference: src/hardware/audio/soundblaster.cpp dsp_e2_dma_callback()
    /// </summary>
    private void DspE2DmaCallback(DmaChannel channel, DmaChannel.DmaEvent dmaEvent) {
        if (dmaEvent == DmaChannel.DmaEvent.IsUnmasked) {
            byte val = (byte)(_sb.E2.Value & 0xff);

            // Unregister callback and write the E2 value
            channel.RegisterCallback(null);
            Span<byte> buffer = stackalloc byte[1];
            buffer[0] = val;
            channel.Write(1, buffer);
        }
    }

    /// <summary>
    /// DMA callback for ADC (analog-to-digital converter) - fakes input by writing silence.
    /// Reference: src/hardware/audio/soundblaster.cpp dsp_adc_callback()
    /// </summary>
    private void DspAdcCallback(DmaChannel channel, DmaChannel.DmaEvent dmaEvent) {
        if (dmaEvent != DmaChannel.DmaEvent.IsUnmasked) {
            return;
        }

        // Write silence (128 = center for 8-bit unsigned audio) to the DMA buffer
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = 128;

        while (_sb.Dma.Left > 0) {
            channel.Write(1, buffer);
            _sb.Dma.Left--;
        }

        RaiseIrq(SbIrq.Irq8);
        channel.RegisterCallback(null);
    }

    private void PlayDmaTransfer(uint bytesRequested) {
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

        // Use FullIndex (emulated time) for timing
        _lastDmaCallbackTime = _clock.FullIndex;

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
            // Remove any pending ProcessDMATransfer events
            // Reference: src/hardware/audio/soundblaster.cpp play_dma_transfer() line 1318
            _scheduler.RemoveEvents(ProcessDmaTransferEvent);

            if (_sb.Dma.Mode >= DmaMode.Pcm16Bit) {
                RaiseIrq(SbIrq.Irq16);
            } else {
                RaiseIrq(SbIrq.Irq8);
            }

            if (!_sb.Dma.AutoInit) {
                if (_sb.Dma.SingleSize == 0) {
                    _sb.Mode = DspMode.None;
                    _sb.Dma.Mode = DmaMode.None;
                } else {
                    _sb.Dma.Left = _sb.Dma.SingleSize;
                    _sb.Dma.SingleSize = 0;
                }
            } else {
                if (_sb.Dma.AutoSize == 0) {
                    _sb.Mode = DspMode.None;
                }
                _sb.Dma.Left = _sb.Dma.AutoSize;
            }
        }
    }

    private (uint bytesRead, uint samples, ushort frames) DecodeAdpcmDma(
        uint bytesToRead,
        Func<byte, byte, ushort, (byte[], byte, ushort)> decodeAdpcmFn) {

        uint numBytes = ReadDma8Bit(bytesToRead);
        uint numSamples = 0;

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
        ushort numFrames = (ushort)numSamples;
        return (numBytes, numSamples, numFrames);
    }

    internal void EnqueueFramesMono(byte[] samples, uint numSamples, bool signed) {
        if (numSamples == 0) {
            return;
        }

        // Return silent frames if still in warmup
        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            EnqueueSilentFrames(numSamples);
            return;
        }

        // Return silent frames if speaker is disabled
        if (!_sb.SpeakerEnabled) {
            EnqueueSilentFrames(numSamples);
            return;
        }

        // Batch process samples into AudioFrames and enqueue to output_queue
        _enqueueBatchCount = 0;
        for (uint i = 0; i < numSamples; i++) {
            float value = signed
                ? LookupTables.ToSigned8(unchecked((sbyte)samples[i]))
                : LookupTables.ToUnsigned8(samples[i]);
            _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(value, value);
        }
        FlushEnqueueBatch();
        // Reference: src/hardware/audio/soundblaster.cpp enqueue_frames()
        _framesAddedThisTick += (int)numSamples;
    }

    /// <summary>
    /// Enqueues silent frames in batch. Used during warmup and when speaker is disabled.
    /// </summary>
    private void EnqueueSilentFrames(uint count) {
        _enqueueBatchCount = 0;
        AudioFrame silence = new AudioFrame(0.0f, 0.0f);
        for (uint i = 0; i < count; i++) {
            _enqueueBatch[_enqueueBatchCount++] = silence;
        }
        FlushEnqueueBatch();
        _framesAddedThisTick += (int)count;
    }

    /// <summary>
    /// Flushes the enqueue batch to the output queue.
    /// </summary>
    private void FlushEnqueueBatch() {
        if (_enqueueBatchCount == 0) {
            return;
        }
        _outputQueue.NonblockingBulkEnqueue(_enqueueBatch.AsSpan(0, _enqueueBatchCount));
        _enqueueBatchCount = 0;
    }

    internal void EnqueueFramesStereo(byte[] samples, uint numSamples, bool signed) {
        if (numSamples == 0) {
            return;
        }

        uint numFrames = numSamples / 2;

        // Return silent frames if still in warmup
        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            EnqueueSilentFrames(numFrames);
            return;
        }

        // Return silent frames if speaker is disabled
        if (!_sb.SpeakerEnabled) {
            EnqueueSilentFrames(numFrames);
            return;
        }

        // Batch process samples into AudioFrames
        // Note: SB Pro 1 and 2 swap left/right channels
        bool swapChannels = _sb.Type == SbType.SBPro1 || _sb.Type == SbType.SBPro2;

        _enqueueBatchCount = 0;
        for (uint i = 0; i < numFrames; i++) {
            float left = signed
                ? LookupTables.ToSigned8(unchecked((sbyte)samples[i * 2]))
                : LookupTables.ToUnsigned8(samples[i * 2]);

            float right = signed
                ? LookupTables.ToSigned8(unchecked((sbyte)samples[i * 2 + 1]))
                : LookupTables.ToUnsigned8(samples[i * 2 + 1]);

            if (swapChannels) {
                _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(right, left);
            } else {
                _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(left, right);
            }
        }
        FlushEnqueueBatch();
        // Reference: src/hardware/audio/soundblaster.cpp enqueue_frames()
        _framesAddedThisTick += (int)numFrames;
    }

    internal void EnqueueFramesMono16(short[] samples, uint numSamples, bool signed) {
        if (numSamples == 0) {
            return;
        }

        // Return silent frames if still in warmup
        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            EnqueueSilentFrames(numSamples);
            return;
        }

        // Return silent frames if speaker is disabled
        if (!_sb.SpeakerEnabled) {
            EnqueueSilentFrames(numSamples);
            return;
        }

        // Batch process samples into AudioFrames
        _enqueueBatchCount = 0;
        for (uint i = 0; i < numSamples; i++) {
            float value = signed
                ? LookupTables.ToSigned16(samples[i])
                : LookupTables.ToUnsigned16((ushort)samples[i]);
            _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(value, value);
        }
        FlushEnqueueBatch();
        _framesAddedThisTick += (int)numSamples;
    }

    internal void EnqueueFramesStereo16(short[] samples, uint numSamples, bool signed) {
        if (numSamples == 0) {
            return;
        }

        uint numFrames = numSamples / 2;

        // Return silent frames if still in warmup
        if (_sb.Dsp.WarmupRemainingMs > 0) {
            _sb.Dsp.WarmupRemainingMs--;
            EnqueueSilentFrames(numFrames);
            return;
        }

        // Return silent frames if speaker is disabled
        if (!_sb.SpeakerEnabled) {
            EnqueueSilentFrames(numFrames);
            return;
        }

        // Batch process samples into AudioFrames
        // Note: SB Pro 1 and 2 swap left/right channels
        bool swapChannels = _sb.Type == SbType.SBPro1 || _sb.Type == SbType.SBPro2;

        _enqueueBatchCount = 0;
        for (uint i = 0; i < numFrames; i++) {
            float left = signed
                ? LookupTables.ToSigned16(samples[i * 2])
                : LookupTables.ToUnsigned16((ushort)samples[i * 2]);

            float right = signed
                ? LookupTables.ToSigned16(samples[i * 2 + 1])
                : LookupTables.ToUnsigned16((ushort)samples[i * 2 + 1]);

            if (swapChannels) {
                _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(right, left);
            } else {
                _enqueueBatch[_enqueueBatchCount++] = new AudioFrame(left, right);
            }
        }
        FlushEnqueueBatch();
        _framesAddedThisTick += (int)numFrames;
    }

    /// <summary>
    /// Raises an IRQ for the Sound Blaster.
    /// Reference: src/hardware/audio/soundblaster.cpp sb_raise_irq()
    /// </summary>
    private void RaiseIrq(SbIrq irqType) {
        switch (irqType) {
            case SbIrq.Irq8:
                // Don't raise if already pending
                if (_sb.Irq.Pending8Bit) {
                    return;
                }
                _sb.Irq.Pending8Bit = true;
                _dualPic.ActivateIrq(_sb.Hw.Irq);
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("SOUNDBLASTER: Raised 8-bit IRQ {Irq}", _sb.Hw.Irq);
                }
                break;

            case SbIrq.Irq16:
                // Don't raise if already pending
                if (_sb.Irq.Pending16Bit) {
                    return;
                }
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
    /// Reference: src/hardware/audio/soundblaster.cpp per_tick_callback()
    /// This callback is run once per emulator tick (every 1ms), so it generates a
    /// batch of frames covering each 1ms time period. For example, if the Sound
    /// Blaster's running at 8 kHz, then that's 8 frames per call. Many rates aren't
    /// evenly divisible by 1000 (For example, 22050 Hz is 22.05 frames/millisecond),
    /// so this function keeps track of exact fractional frames and uses rounding to
    /// ensure partial frames are accounted for and generated across N calls.
    /// Reference: DOSBox staging calls this via TIMER_AddTickHandler (no parameters)
    /// </summary>
    private void per_tick_callback() {
        // assert(sblaster);
        // assert(sblaster->channel);

        // if (!sblaster->channel->is_enabled) {
        //     callback_type.SetNone();
        //     return;
        // }
        if (!_dacChannel.IsEnabled) {
            SetCallbackNone();
            return;
        }

        // Reference: src/hardware/audio/soundblaster.cpp per_tick_callback() lines 3234-3248
        // static float frame_counter = 0.0f;
        // frame_counter += std::max(static_cast<float>(sblaster->frames_needed.exchange(0)), 
        //                           sblaster->channel->GetFramesPerTick());
        int frames_needed_val = System.Threading.Interlocked.Exchange(ref _framesNeeded, 0);
        float frames_per_tick = _dacChannel.GetFramesPerTick();
        _frameCounter += Math.Max(frames_needed_val, frames_per_tick);

        // const int total_frames = ifloor(frame_counter);
        // frame_counter -= static_cast<float>(total_frames);
        int total_frames = (int)Math.Floor(_frameCounter);
        _frameCounter -= total_frames;

        // while (frames_added_this_tick < total_frames) {
        //     generate_frames(total_frames - frames_added_this_tick);
        // }
        while (_framesAddedThisTick < total_frames) {
            generate_frames(total_frames - _framesAddedThisTick);
        }

        // frames_added_this_tick -= total_frames;
        _framesAddedThisTick -= total_frames;
    }

    /// <summary>
    /// Per-frame callback for fine-grained audio generation.
    /// Used for very short DMA transfers where per-tick callbacks would be too infrequent.
    /// Reference: src/hardware/audio/soundblaster.cpp per_frame_callback()
    /// </summary>
    private void per_frame_callback(uint _) {
        if (!_dacChannel.IsEnabled) {
            SetCallbackNone();
            return;
        }

        // Reference: src/hardware/audio/soundblaster.cpp per_frame_callback() lines 3270-3280
        int mixer_needs = Math.Max(System.Threading.Interlocked.Exchange(ref _framesNeeded, 0), 1);

        // Frames added this tick is only useful when we're in an underflow
        // situation with the mixer. generate_frames() may not give us
        // everything we need in a single call. We're not concerned about
        // over-filling while in this mode so just zero it out.
        _framesAddedThisTick = 0;
        while (_framesAddedThisTick < mixer_needs) {
            generate_frames(mixer_needs - _framesAddedThisTick);
        }

        AddNextFrameCallback();
    }

    /// <summary>
    /// Schedules the next per-frame callback.
    /// Reference: src/hardware/audio/soundblaster.cpp add_next_frame_callback()
    /// </summary>
    private void AddNextFrameCallback() {
        double millisPerFrame = _dacChannel.GetMillisPerFrame();
        _scheduler.AddEvent(per_frame_callback, millisPerFrame, 0);
    }

    /// <summary>
    /// Stops the current callback type.
    /// Reference: src/hardware/audio/soundblaster.cpp CallbackType::SetNone()
    /// </summary>
    private void SetCallbackNone() {
        if (_timingType != TimingType.None) {
            if (_timingType == TimingType.PerTick) {
                _scheduler.DelTickHandler(per_tick_callback);
            } else {
                _scheduler.RemoveEvents(per_frame_callback);
            }

            _timingType = TimingType.None;
        }
    }

    /// <summary>
    /// Switches to per-tick callback mode (every 1ms).
    /// Reference: src/hardware/audio/soundblaster.cpp CallbackType::SetPerTick()
    /// </summary>
    private void SetCallbackPerTick() {
        if (_timingType != TimingType.PerTick) {
            SetCallbackNone();

            _framesAddedThisTick = 0;

            _scheduler.AddTickHandler(per_tick_callback);

            _timingType = TimingType.PerTick;
        }
    }

    /// <summary>
    /// Switches to per-frame callback mode (at sample rate frequency).
    /// Used for very short DMA transfers.
    /// Reference: src/hardware/audio/soundblaster.cpp CallbackType::SetPerFrame()
    /// </summary>
    private void SetCallbackPerFrame() {
        if (_timingType != TimingType.PerFrame) {
            SetCallbackNone();

            AddNextFrameCallback();

            _timingType = TimingType.PerFrame;
        }
    }

    /// <summary>
    /// Reference: src/hardware/audio/soundblaster.cpp generate_frames()
    /// </summary>
    private void generate_frames(int frames_requested) {
        switch (_sb.Mode) {
            case DspMode.None:
            case DspMode.DmaPause:
            case DspMode.DmaMasked: {
                    // Reference: static std::vector<AudioFrame> empty_frames = {};
                    // empty_frames.resize(frames_requested);
                    // enqueue_frames(empty_frames);
                    EnqueueSilentFrames((uint)frames_requested);
                    break;
                }

            case DspMode.Dac:
                // DAC mode typically renders one frame at a time because the
                // DOS program will be writing to the DAC register at the
                // playback rate. In a mixer underflow situation, we render the
                // current frame multiple times.
                _enqueueBatchCount = 0;
                for (int i = 0; i < frames_requested; i++) {
                    _enqueueBatch[_enqueueBatchCount++] = _sb.Dac.RenderFrame();
                    if (_enqueueBatchCount == _enqueueBatch.Length) {
                        FlushEnqueueBatch();
                    }
                }
                FlushEnqueueBatch();
                _framesAddedThisTick += frames_requested;
                break;

            case DspMode.Dma: {
                    // This is a no-op if the channel is already running. DMA
                    // processing can go for some time using auto-init mode without
                    // having to send IO calls to the card; so we keep it awake when
                    // DMA is still running.
                    MaybeWakeUp();

                    uint len = (uint)frames_requested;
                    len *= _sb.Dma.Mul;
                    if ((len & SbShiftMask) != 0) {
                        len += 1 << SbShift;
                    }
                    len >>= SbShift;

                    if (len > _sb.Dma.Left) {
                        len = _sb.Dma.Left;
                    }

                    // ProcessDMATransfer(len);
                    PlayDmaTransfer(len);
                    break;
                }
        }
    }

    /// <summary>
    /// Reference: src/hardware/audio/soundblaster.cpp enqueue_frames()
    /// </summary>
    private void EnqueueFrames(ReadOnlySpan<AudioFrame> frames) {
        if (frames.Length == 0) {
            return;
        }
        _framesAddedThisTick += frames.Length;
        _enqueueBatchCount = 0;
        for (int i = 0; i < frames.Length; i++) {
            _enqueueBatch[_enqueueBatchCount++] = frames[i];
            if (_enqueueBatchCount == _enqueueBatch.Length) {
                FlushEnqueueBatch();
            }
        }
        FlushEnqueueBatch();
    }

    public bool PendingIrq8Bit {
        get => _sb.Irq.Pending8Bit;
        set => _sb.Irq.Pending8Bit = value;
    }

    public bool PendingIrq16Bit {
        get => _sb.Irq.Pending16Bit;
        set => _sb.Irq.Pending16Bit = value;
    }

    public bool IsSpeakerEnabled => _sb.SpeakerEnabled;

    public uint DspFrequencyHz => _sb.FreqHz;

    public byte DspTestRegister => _sb.Dsp.TestRegister;

    public MixerChannel DacChannel => _dacChannel;

    // ReadByte and WriteByte have ZERO logging, matching DOSBox's read_sb()/write_sb().
    // Reference: src/hardware/audio/soundblaster.cpp read_sb() / write_sb()
    /// <summary>
    /// Reads from a Sound Blaster I/O port.
    /// Reference: src/hardware/audio/soundblaster.cpp read_sb()
    /// </summary>
    public override byte ReadByte(ushort port) {
        switch (port - _config.BaseAddress) {
            case 0x04: // MixerIndex
                return (byte)_hardwareMixer.CurrentAddress;

            case 0x05: // MixerData
                return _hardwareMixer.ReadData();

            case 0x0A: // DspReadData
                return DspReadData();

            case 0x0C: { // DspWriteStatus
                    // Bit 7 = 1 means buffer at capacity (not ready to receive).
                    // Lower 7 bits are always 1.
                    // Reference: src/hardware/audio/soundblaster.cpp read_sb() DspWriteStatus
                    byte writeStatus = 0x7F; // lower 7 bits always set
                    if (WriteBufferAtCapacity()) {
                        writeStatus |= 0x80;
                    }
                    return writeStatus;
                }

            case 0x0E: { // DspReadStatus
                    // Acknowledges 8-bit IRQ. Bit 7 = 1 if output FIFO has data.
                    // Lower 7 bits are always 1.
                    // Reference: src/hardware/audio/soundblaster.cpp read_sb() DspReadStatus
                    if (_sb.Irq.Pending8Bit) {
                        _sb.Irq.Pending8Bit = false;
                        _dualPic.DeactivateIrq(_config.Irq);
                    }
                    byte readStatus = 0x7F; // lower 7 bits always set
                    if (_sb.Dsp.Out.Used != 0) {
                        readStatus |= 0x80;
                    }
                    return readStatus;
                }

            case 0x0F: // DspAck16Bit
                _sb.Irq.Pending16Bit = false;
                return 0xFF;

            case 0x06: // DspReset read
                return 0xFF;

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
                break;
        }
    }

    private void DspDoReset(byte value) {
        if (((value & 1) != 0) && (_sb.Dsp.State != DspState.Reset)) {
            DspReset();
            _sb.Dsp.State = DspState.Reset;
        } else if (((value & 1) == 0) && (_sb.Dsp.State == DspState.Reset)) {
            // reset off
            _sb.Dsp.State = DspState.ResetWait;

            // Reference: src/hardware/audio/soundblaster.cpp dsp_do_reset()
            // PIC_RemoveEvents(dsp_finish_reset);
            // PIC_AddEvent(dsp_finish_reset, 20.0 / 1000.0, 0);  // 20 microseconds
            _scheduler.RemoveEvents(DspFinishResetEvent);
            _scheduler.AddEvent(DspFinishResetEvent, 20.0 / 1000.0, 0);  // 20 microseconds = 0.020 ms
        }
    }

    /// <summary>
    /// Event callback for delayed DSP reset completion.
    /// Reference: src/hardware/audio/soundblaster.cpp dsp_finish_reset()
    /// </summary>
    private void DspFinishResetEvent(uint val) {
        DspFinishReset();
    }

    private void DspFinishReset() {
        DspFlushData();
        DspAddData(0xaa);
        _sb.Dsp.State = DspState.Normal;
    }

    private void DspReset() {
        // Stop any active callback before resetting state
        SetCallbackNone();

        _dualPic.DeactivateIrq(_config.Irq);
        DspChangeMode(DspMode.None);
        DspFlushData();

        // Reference: src/hardware/audio/soundblaster.cpp dsp_reset() lines 1718-1722
        // sb.dsp.cmd     = DspNoCommand;
        // sb.dsp.cmd_len = 0;
        // sb.dsp.in.pos  = 0;
        _sb.Dsp.Cmd = 0;
        _sb.Dsp.CmdLen = 0;
        _sb.Dsp.In.Pos = 0;

        _sb.Dsp.WriteStatusCounter = 0;
        _sb.Dsp.ResetTally++;

        // Ensure command state is properly reset (DOSBox uses sb.dsp.cmd == 0 implicitly)
        _blasterState = BlasterState.WaitingForCommand;

        // Remove any pending finish reset events
        // Reference: src/hardware/audio/soundblaster.cpp dsp_reset() line 1723
        _scheduler.RemoveEvents(DspFinishResetEvent);

        _sb.Dma.Left = 0;
        _sb.Dma.SingleSize = 0;
        _sb.Dma.AutoSize = 0;
        _sb.Dma.Stereo = false;
        _sb.Dma.Sign = false;
        _sb.Dma.AutoInit = false;
        _sb.Dma.FirstTransfer = true;
        _sb.Dma.Mode = DmaMode.None;
        _sb.Dma.RemainSize = 0;

        // Clear any pending DMA requests
        // Reference: src/hardware/audio/soundblaster.cpp dsp_reset() line 1735
        _sb.Dma.Channel?.ClearRequest();

        _sb.Adpcm.Reference = 0;
        _sb.Adpcm.Stepsize = 0;
        _sb.Adpcm.HaveRef = false;

        // DAC state is reset implicitly - it reads from _sb.Dsp.In.Data which is cleared below
        // Reference: src/hardware/audio/soundblaster.cpp dsp_reset() line 1741 - sb.dac = {};

        _sb.FreqHz = DefaultPlaybackRateHz;
        _sb.TimeConstant = 45;
        _sb.E2.Value = 0xaa;
        _sb.E2.Count = 0;

        _sb.Irq.Pending8Bit = false;
        _sb.Irq.Pending16Bit = false;

        // Update channel sample rate to default
        _dacChannel.SetSampleRate(DefaultPlaybackRateHz);

        // Re-initialize speaker state
        // Reference: src/hardware/audio/soundblaster.cpp dsp_reset() line 1751
        InitSpeakerState();

        // Remove any pending DMA transfer events
        // Reference: src/hardware/audio/soundblaster.cpp dsp_reset() line 1755
        _scheduler.RemoveEvents(ProcessDmaTransferEvent);
    }

    private void DspDoWrite(byte value) {
        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_write()
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("SB: DSP write value=0x{Value:X2} state={State}", value, _blasterState);
        }
        switch (_blasterState) {
            case BlasterState.WaitingForCommand:
                _sb.Dsp.Cmd = value;
                if (_config.SbType == SbType.Sb16) {
                    _sb.Dsp.CmdLen = DspCommandLengthsSb16[value];
                } else {
                    _sb.Dsp.CmdLen = DspCommandLengthsSb[value];
                }
                _sb.Dsp.In.Pos = 0;
                _blasterState = BlasterState.ReadingCommand;
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SB: Command 0x{Cmd:X2} received; expecting {Len} bytes", _sb.Dsp.Cmd, _sb.Dsp.CmdLen);
                }
                if (_sb.Dsp.CmdLen == 0) {
                    ProcessCommand();
                }
                break;

            case BlasterState.ReadingCommand:
                // Reference: sb.dsp.in.data[sb.dsp.in.pos] = val; sb.dsp.in.pos++;
                _sb.Dsp.In.Data[_sb.Dsp.In.Pos] = value;
                _sb.Dsp.In.Pos++;
                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("SB: Command 0x{Cmd:X2} param[{Count}] = 0x{Val:X2}", _sb.Dsp.Cmd, _sb.Dsp.In.Pos - 1, value);
                }
                if (_sb.Dsp.In.Pos >= _sb.Dsp.CmdLen) {
                    ProcessCommand();
                }
                break;
        }
    }

    private bool ProcessCommand() {
        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_command()
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            string paramsHex = _sb.Dsp.In.Pos > 0 ? string.Join(" ", _sb.Dsp.In.Data.Take(_sb.Dsp.In.Pos).Select(b => b.ToString("X2"))) : string.Empty;
            _loggerService.Debug("SB: Processing command 0x{Cmd:X2} params={Params}", _sb.Dsp.Cmd, paramsHex);
        }
        switch (_sb.Dsp.Cmd) {
            case 0x04:
                // Sb16 ASP set mode register or DSP Status
                if (_config.SbType == SbType.Sb16) {
                    if ((_sb.Dsp.In.Data[0] & 0xf1) == 0xf1) {
                        _aspInitInProgress = true;
                    } else {
                        _aspInitInProgress = false;
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
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("DSP Unhandled SB16ASP command 0x{Cmd:X2} (set codec parameter)", _sb.Dsp.Cmd);
                }
                // No specific action needed - ASP commands are mostly unimplemented
                break;

            case 0x08: // SB16 ASP get version
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("DSP Unhandled SB16ASP command {Cmd:X} sub {Sub:X}",
                                        _sb.Dsp.Cmd,
                                        _sb.Dsp.In.Pos > 0 ? _sb.Dsp.In.Data[0] : 0);
                }

                if (_config.SbType == SbType.Sb16 && _sb.Dsp.In.Pos >= 1) {
                    switch (_sb.Dsp.In.Data[0]) {
                        case 0x03:
                            DspAddData(0x18); // version ID (??)
                            break;

                        default:
                            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                                _loggerService.Debug("DSP Unhandled SB16ASP command {Cmd:X} sub {Sub:X}",
                                                    _sb.Dsp.Cmd,
                                                    _sb.Dsp.In.Data[0]);
                            }
                            break;
                    }
                }
                break;

            case 0x0e:
                // Sb16 ASP set register
                // Reference: src/hardware/audio/soundblaster.cpp case 0x0e
                if (_config.SbType == SbType.Sb16) {
                    _aspRegs[_sb.Dsp.In.Data[0]] = _sb.Dsp.In.Data[1];
                }
                break;

            case 0x0f:
                // Sb16 ASP get register
                // Reference: src/hardware/audio/soundblaster.cpp case 0x0f
                if (_config.SbType == SbType.Sb16) {
                    if (_aspInitInProgress && (_sb.Dsp.In.Data[0] == 0x83)) {
                        _aspRegs[0x83] = (byte)~_aspRegs[0x83];
                    }
                    DspAddData(_aspRegs[_sb.Dsp.In.Data[0]]);
                }
                break;

            case 0x10:
                // Direct DAC
                // Reference: src/hardware/audio/soundblaster.cpp DSP command 0x10
                DspChangeMode(DspMode.Dac);

                MaybeWakeUp();

                // Ensure we're using per-frame callback timing because DAC samples
                // are sent one after another with sub-millisecond timing.
                SetCallbackPerFrame();
                break;

            case 0x14:
            case 0x15:
            case 0x91:
                // Single Cycle 8-Bit DMA DAC
                // Reference: src/hardware/audio/soundblaster.cpp case 0x14/0x15/0x91
                // DOSBox calls dsp_prepare_dma_old() directly which reads sb.dsp.in.data[]
                DspPrepareDmaOld(DmaMode.Pcm8Bit, false, false);
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
                                            _sb.Dsp.Cmd);
                    }
                }
                break;

            case 0x20:
                DspAddData(0x7f); // Fake silent input for Creative parrot
                break;

            case 0x24:
                // Single Cycle 8-Bit DMA ADC
                // Reference: src/hardware/audio/soundblaster.cpp case 0x24
                // Note: ADC is faked - writes silence (128) to DMA buffer
                _sb.Dma.Left = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
                _sb.Dma.Sign = false;
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SB: Faked ADC for {Size} bytes", _sb.Dma.Left);
                }
                _primaryDmaChannel.RegisterCallback(DspAdcCallback);
                break;

            case 0x30:
            case 0x31:
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("DSP:Unimplemented MIDI I/O command {Cmd:X2}",
                                        _sb.Dsp.Cmd);
                }
                break;

            case 0x34:
            case 0x35:
            case 0x36:
            case 0x37:
                if (_config.SbType > SbType.SB1) {
                    if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                        _loggerService.Error("DSP:Unimplemented MIDI UART command {Cmd:X2}",
                                            _sb.Dsp.Cmd);
                    }
                }
                break;

            case 0x38:
                // Write to SB MIDI Output
                // Reference: src/hardware/audio/soundblaster.cpp case 0x38
                if (_sb.MidiEnabled) {
                    if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                        _loggerService.Verbose("SB: MIDI output byte 0x{Byte:X2}", _sb.Dsp.In.Data[0]);
                    }
                    // TODO: Forward to MIDI subsystem
                }
                break;

            case 0x40:
                // Set Timeconstant
                // Reference: src/hardware/audio/soundblaster.cpp case 0x40
                DspChangeRate((uint)(1000000 / (256 - _sb.Dsp.In.Data[0])));
                if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                    _loggerService.Debug("SB: Timeconstant set tc=0x{Tc:X2} rate={Rate}Hz", _sb.Dsp.In.Data[0], _sb.FreqHz);
                }
                break;

            case 0x41:
            case 0x42:
                // Set Output/Input Samplerate (Sb16)
                // Reference: src/hardware/audio/soundblaster.cpp case 0x41/0x42
                // Note: 0x42 is handled like 0x41, needed by Fasttracker II
                if (_config.SbType == SbType.Sb16) {
                    DspChangeRate((uint)((_sb.Dsp.In.Data[0] << 8) | _sb.Dsp.In.Data[1]));
                }
                break;

            case 0x48:
                // Set DMA Block Size
                // Reference: src/hardware/audio/soundblaster.cpp case 0x48
                if (_config.SbType > SbType.SB1) {
                    _sb.Dma.AutoSize = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SB: DMA AutoSize set to {AutoSize}", _sb.Dma.AutoSize);
                    }
                }
                break;

            case 0x16:
            case 0x17:
                // Single Cycle 2-bit ADPCM
                // 0x17 includes reference byte
                // Reference: src/hardware/audio/soundblaster.cpp case 0x16/0x17
                if (_sb.Dsp.Cmd == 0x17) {
                    _sb.Adpcm.HaveRef = true;
                }
                DspPrepareDmaOld(DmaMode.Adpcm2Bit, false, false);
                break;

            case 0x74:
            case 0x75:
                // Single Cycle 4-bit ADPCM
                // Reference: src/hardware/audio/soundblaster.cpp case 0x74/0x75
                if (_sb.Dsp.Cmd == 0x75) {
                    _sb.Adpcm.HaveRef = true;
                }
                DspPrepareDmaOld(DmaMode.Adpcm4Bit, false, false);
                break;

            case 0x76:
            case 0x77:
                // Single Cycle 3-bit ADPCM
                // Reference: src/hardware/audio/soundblaster.cpp case 0x76/0x77
                if (_sb.Dsp.Cmd == 0x77) {
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
                // Silence DAC - schedule IRQ after specified duration
                // Reference: src/hardware/audio/soundblaster.cpp case 0x80
                {
                    uint samples = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
                    double delayMs = (1000.0 * samples) / _sb.FreqHz;
                    _scheduler.AddEvent(DspRaiseIrqEvent, delayMs, 0);
                }
                break;

            case 0x98:
            case 0x99: // Documented only for DSP 2.x and 3.x
            case 0xa0:
            case 0xa8: // Documented only for DSP 3.x
                if (_loggerService.IsEnabled(LogEventLevel.Error)) {
                    _loggerService.Error("DSP:Unimplemented input command {Cmd:X2}",
                                        _sb.Dsp.Cmd);
                }
                break;

            // Generic 8/16-bit DMA commands (SB16 only) - 0xB0-0xCF
            // Reference: DOSBox soundblaster.cpp lines 2068-2097
            case 0xb0:
            case 0xb1:
            case 0xb2:
            case 0xb3:
            case 0xb4:
            case 0xb5:
            case 0xb6:
            case 0xb7:
            case 0xb8:
            case 0xb9:
            case 0xba:
            case 0xbb:
            case 0xbc:
            case 0xbd:
            case 0xbe:
            case 0xbf:
            case 0xc0:
            case 0xc1:
            case 0xc2:
            case 0xc3:
            case 0xc4:
            case 0xc5:
            case 0xc6:
            case 0xc7:
            case 0xc8:
            case 0xc9:
            case 0xca:
            case 0xcb:
            case 0xcc:
            case 0xcd:
            case 0xce:
            case 0xcf:
                // Generic DMA commands (SB16 only)
                // Reference: src/hardware/audio/soundblaster.cpp case 0xb0-0xcf
                if (_config.SbType == SbType.Sb16) {
                    // Parse command byte and mode byte
                    // Command bit 4 (0x10): 0=8-bit, 1=16-bit
                    // Mode byte bit 4 (0x10): signed data
                    // Mode byte bit 5 (0x20): stereo
                    // Command bit 2 (0x04): FIFO enable (we don't emulate FIFO delay)

                    _sb.Dma.Sign = (_sb.Dsp.In.Data[0] & 0x10) != 0;
                    bool is16Bit = (_sb.Dsp.Cmd & 0x10) != 0;
                    bool autoInit = (_sb.Dsp.Cmd & 0x04) != 0;
                    bool stereo = (_sb.Dsp.In.Data[0] & 0x20) != 0;

                    // Length is in bytes (for 8-bit) or words (for 16-bit)
                    uint length = (uint)(1 + _sb.Dsp.In.Data[1] + (_sb.Dsp.In.Data[2] << 8));

                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("SB16: Generic DMA cmd=0x{Cmd:X2} mode=0x{Mode:X2} " +
                            "16bit={Is16Bit} autoInit={AutoInit} stereo={Stereo} sign={Sign} len={Length}",
                            _sb.Dsp.Cmd, _sb.Dsp.In.Data[0], is16Bit, autoInit, stereo, _sb.Dma.Sign, length);
                    }

                    DspPrepareDmaNew(is16Bit ? DmaMode.Pcm16Bit : DmaMode.Pcm8Bit, length, autoInit, stereo);
                } else {
                    _loggerService.Warning("SOUNDBLASTER: Generic DMA commands (0xB0-0xCF) require SB16");
                }
                break;

            case 0xd0:
                // Halt 8-bit DMA
                // Reference: src/hardware/audio/soundblaster.cpp case 0xd0
                _sb.Mode = DspMode.DmaPause;
                _scheduler.RemoveEvents(ProcessDmaTransferEvent);
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
                // Reference: src/hardware/audio/soundblaster.cpp case 0xd4
                if (_sb.Mode == DspMode.DmaPause) {
                    _sb.Mode = DspMode.DmaMasked;
                    _sb.Dma.Channel?.RegisterCallback(DspDmaCallback);
                }
                break;

            case 0xd5:
                // Halt 16-bit DMA
                // Reference: src/hardware/audio/soundblaster.cpp case 0xd5
                if (_config.SbType != SbType.Sb16) {
                    break;
                }
                _sb.Mode = DspMode.DmaPause;
                _scheduler.RemoveEvents(ProcessDmaTransferEvent);
                break;

            case 0xd6:
                // Continue DMA 16-bit
                // Reference: src/hardware/audio/soundblaster.cpp case 0xd6
                if (_config.SbType != SbType.Sb16) {
                    break;
                }
                if (_sb.Mode == DspMode.DmaPause) {
                    _sb.Mode = DspMode.DmaMasked;
                    _sb.Dma.Channel?.RegisterCallback(DspDmaCallback);
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
                // Reference: src/hardware/audio/soundblaster.cpp case 0xe0
                DspFlushData();
                DspAddData((byte)~_sb.Dsp.In.Data[0]);
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
                // Reference: src/hardware/audio/soundblaster.cpp case 0xe2
                for (int i = 0; i < 8; i++) {
                    if (((_sb.Dsp.In.Data[0] >> i) & 0x01) != 0) {
                        _sb.E2.Value += E2IncrTable[_sb.E2.Count % 4][i];
                    }
                }
                _sb.E2.Value += E2IncrTable[_sb.E2.Count % 4][8];
                _sb.E2.Count++;
                // Register callback to write E2 value when DMA is unmasked
                _primaryDmaChannel.RegisterCallback(DspE2DmaCallback);
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
                // Reference: src/hardware/audio/soundblaster.cpp case 0xe4
                _sb.Dsp.TestRegister = _sb.Dsp.In.Data[0];
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
                // Small delay to emulate DSP slowness, fixes Llamatron 2012 and Lemmings 3D
                // Reference: src/hardware/audio/soundblaster.cpp case 0xf2
                _scheduler.AddEvent(DspRaiseIrqEvent, 0.01, 0);
                break;

            case 0xf3:
                // Trigger 16bit IRQ
                // Reference: src/hardware/audio/soundblaster.cpp case 0xf3
                if (_config.SbType == SbType.Sb16) {
                    RaiseIrq(SbIrq.Irq16);
                }
                break;

            case 0xf8:
                // Undocumented, pre-Sb16 only
                DspFlushData();
                DspAddData(0);
                break;

            case 0xf9:
                // Sb16 ASP unknown function
                // Reference: src/hardware/audio/soundblaster.cpp case 0xf9
                if (_config.SbType == SbType.Sb16) {
                    switch (_sb.Dsp.In.Data[0]) {
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
                    _loggerService.Warning("SoundBlaster: Unimplemented DSP command {Command:X2}", _sb.Dsp.Cmd);
                }
                // Reference: src/hardware/audio/soundblaster.cpp dsp_do_command() end
                _sb.Dsp.Cmd = 0;
                _sb.Dsp.CmdLen = 0;
                _sb.Dsp.In.Pos = 0;
                _blasterState = BlasterState.WaitingForCommand;
                return false;
        }

        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_command() end
        // sb.dsp.cmd     = DspNoCommand;
        // sb.dsp.cmd_len = 0;
        // sb.dsp.in.pos  = 0;
        _sb.Dsp.Cmd = 0;
        _sb.Dsp.CmdLen = 0;
        _sb.Dsp.In.Pos = 0;
        _blasterState = BlasterState.WaitingForCommand;
        return true;
    }

    private void DspChangeMode(DspMode mode) {
        if (_sb.Mode == mode) {
            return;
        }
        _sb.Mode = mode;
    }

    /// <summary>
    /// Updates the sample rate during playback.
    /// Reference: src/hardware/audio/soundblaster.cpp dsp_change_rate()
    /// </summary>
    private void DspChangeRate(uint freqHz) {
        // If rate changes during active DMA, update the DMA-related timing values
        if (_sb.FreqHz != freqHz && _sb.Dma.Mode != DmaMode.None) {
            uint effectiveFreq = _sb.Mixer.StereoEnabled ? freqHz / 2 : freqHz;
            _dacChannel.SetSampleRate((int)effectiveFreq);

            _sb.Dma.Rate = (freqHz * _sb.Dma.Mul) >> SbShift;
            _sb.Dma.Min = (_sb.Dma.Rate * 3) / 1000;
        }
        _sb.FreqHz = freqHz;
    }

    /// <summary>
    /// Flushes any remaining DMA transfer that's shorter than the minimum threshold.
    /// This handles edge cases where the DMA transfer is so short it wouldn't be processed
    /// by the normal per-tick callback before the next one fires.
    /// Reference: src/hardware/audio/soundblaster.cpp flush_remaining_dma_transfer()
    /// </summary>
    private void FlushRemainingDmaTransfer() {
        if (_sb.Dma.Left == 0) {
            return;
        }

        if (!_sb.SpeakerEnabled && _config.SbType != SbType.Sb16) {
            uint numBytes = Math.Min(_sb.Dma.Min, _sb.Dma.Left);
            double delayMs = (numBytes * 1000.0) / _sb.Dma.Rate;

            _scheduler.AddEvent(SuppressDmaTransfer, delayMs, numBytes);

            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("SOUNDBLASTER: Silent DMA Transfer scheduling IRQ in {Delay:F3} milliseconds", delayMs);
            }
        } else if (_sb.Dma.Left < _sb.Dma.Min) {
            double delayMs = (_sb.Dma.Left * 1000.0) / _sb.Dma.Rate;

            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("SOUNDBLASTER: Short transfer scheduling IRQ in {Delay:F3} milliseconds", delayMs);
            }

            _scheduler.AddEvent(ProcessDmaTransferEvent, delayMs, _sb.Dma.Left);
        }
    }

    /// <summary>
    /// Event callback to process DMA transfer for flush_remaining_dma_transfer.
    /// Reference: src/hardware/audio/soundblaster.cpp ProcessDMATransfer()
    /// </summary>
    private void ProcessDmaTransferEvent(uint bytesToProcess) {
        if (_sb.Dma.Left > 0) {
            uint toProcess = Math.Min(bytesToProcess, _sb.Dma.Left);
            PlayDmaTransfer(toProcess);
        }
    }

    /// <summary>
    /// Suppresses DMA transfer silently (reads and discards data, raises IRQs).
    /// Used when speaker output is disabled.
    /// Reference: src/hardware/audio/soundblaster.cpp suppress_dma_transfer()
    /// </summary>
    private void SuppressDmaTransfer(uint bytesToRead) {
        uint numBytes = Math.Min(bytesToRead, _sb.Dma.Left);

        // Read and discard the DMA data silently
        DmaChannel? dmaChannel = _sb.Dma.Channel;
        if (dmaChannel is not null && numBytes > 0) {
            Span<byte> discardBuffer = stackalloc byte[(int)Math.Min(numBytes, 4096)];
            uint remaining = numBytes;
            while (remaining > 0) {
                int toRead = (int)Math.Min(remaining, (uint)discardBuffer.Length);
                int read = dmaChannel.Read(toRead, discardBuffer[..toRead]);
                if (read == 0) {
                    break;
                }
                remaining -= (uint)read;
            }
        }

        _sb.Dma.Left -= numBytes;

        if (_sb.Dma.Left == 0) {
            // Raise appropriate IRQ
            if (_sb.Dma.Mode >= DmaMode.Pcm16Bit) {
                RaiseIrq(SbIrq.Irq16);
            } else {
                RaiseIrq(SbIrq.Irq8);
            }

            // Handle auto-init vs single-cycle
            if (_sb.Dma.AutoInit) {
                _sb.Dma.Left = _sb.Dma.AutoSize;
            } else {
                _sb.Mode = DspMode.None;
                _sb.Dma.Mode = DmaMode.None;
            }
        }

        // If more data remains, schedule another suppress
        if (_sb.Dma.Left > 0) {
            uint nextBytes = Math.Min(_sb.Dma.Min, _sb.Dma.Left);
            double delayMs = (nextBytes * 1000.0) / _sb.Dma.Rate;
            _scheduler.AddEvent(SuppressDmaTransfer, delayMs, nextBytes);
        }
    }

    /// <summary>
    /// Core DMA transfer setup - matches DOSBox's dsp_do_dma_transfer().
    /// Reference: src/hardware/audio/soundblaster.cpp dsp_do_dma_transfer()
    /// </summary>
    private void DspDoDmaTransfer(DmaMode mode, uint freqHz, bool autoInit, bool stereo) {
        // Starting a new transfer will clear any active irqs
        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_dma_transfer() lines 1571-1573
        _sb.Irq.Pending8Bit = false;
        _sb.Irq.Pending16Bit = false;
        _dualPic.DeactivateIrq(_config.Irq);

        // Set up the multiplier based on DMA mode
        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_dma_transfer() lines 1575-1588
        _sb.Dma.Mul = mode switch {
            DmaMode.Adpcm2Bit => (1 << SbShift) / 4,
            DmaMode.Adpcm3Bit => (1 << SbShift) / 3,
            DmaMode.Adpcm4Bit => (1 << SbShift) / 2,
            DmaMode.Pcm8Bit => 1 << SbShift,
            DmaMode.Pcm16Bit => 1 << SbShift,
            DmaMode.Pcm16BitAliased => (1 << SbShift) * 2,
            _ => 1 << SbShift
        };

        // Going from an active autoinit into a single cycle
        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_dma_transfer() lines 1590-1601
        if (_sb.Mode >= DspMode.Dma && _sb.Dma.AutoInit && !autoInit) {
            // Don't do anything, the total will flip over on the next transfer
        } else if (!autoInit) {
            // Just a normal single cycle transfer
            _sb.Dma.Left = _sb.Dma.SingleSize;
            _sb.Dma.SingleSize = 0;
        } else {
            // Going into an autoinit transfer - transfer full cycle again
            _sb.Dma.Left = _sb.Dma.AutoSize;
        }

        _sb.Dma.AutoInit = autoInit;
        _sb.Dma.Mode = mode;
        _sb.Dma.Stereo = stereo;

        // Double the reading speed for stereo mode
        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_dma_transfer() lines 1606-1608
        if (stereo) {
            _sb.Dma.Mul *= 2;
        }

        // Calculate rate and minimum transfer size
        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_dma_transfer() lines 1609-1610
        _sb.Dma.Rate = (freqHz * _sb.Dma.Mul) >> SbShift;
        _sb.Dma.Min = (_sb.Dma.Rate * 3) / 1000;

        // Update channel sample rate
        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_dma_transfer() lines 1612-1613
        _dacChannel.SetSampleRate((int)freqHz);

        // Remove any pending DMA transfer events
        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_dma_transfer() line 1615
        _scheduler.RemoveEvents(ProcessDmaTransferEvent);

        // Set to be masked, the dma call can change this again
        // Reference: src/hardware/audio/soundblaster.cpp dsp_do_dma_transfer() lines 1616-1618
        _sb.Mode = DspMode.DmaMasked;
        _sb.Dma.Channel?.RegisterCallback(DspDmaCallback);

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SOUNDBLASTER: DMA Transfer - Mode={Mode}, Stereo={Stereo}, AutoInit={AutoInit}, FreqHz={FreqHz}, Rate={Rate}, Left={Left}",
                mode, stereo, autoInit, freqHz, _sb.Dma.Rate, _sb.Dma.Left);
        }
    }

    /// <summary>
    /// Prepare DMA transfer for old-style (SB 1.x/2.x) commands.
    /// Reference: src/hardware/audio/soundblaster.cpp dsp_prepare_dma_old()
    /// </summary>
    private void DspPrepareDmaOld(DmaMode mode, bool autoInit, bool sign) {
        _sb.Dma.Sign = sign;

        // For single-cycle transfers, set up the size from the DSP input buffer
        // Reference: src/hardware/audio/soundblaster.cpp dsp_prepare_dma_old() lines 1635-1637
        // DOSBox: sb.dma.singlesize = 1 + sb.dsp.in.data[0] + (sb.dsp.in.data[1] << 8);
        if (!autoInit) {
            _sb.Dma.SingleSize = (uint)(1 + _sb.Dsp.In.Data[0] + (_sb.Dsp.In.Data[1] << 8));
        }

        // Always use 8-bit DMA channel for old-style commands
        _sb.Dma.Channel = _primaryDmaChannel;

        // Calculate frequency - divide by 2 for stereo
        // Reference: src/hardware/audio/soundblaster.cpp dsp_prepare_dma_old() lines 1640-1643
        uint freqHz = _sb.FreqHz;
        if (_sb.Mixer.StereoEnabled) {
            freqHz /= 2;
        }

        DspDoDmaTransfer(mode, freqHz, autoInit, _sb.Mixer.StereoEnabled);
    }

    /// <summary>
    /// Prepare DMA transfer for new-style (SB16) commands.
    /// Reference: src/hardware/audio/soundblaster.cpp dsp_prepare_dma_new()
    /// </summary>
    private void DspPrepareDmaNew(DmaMode mode, uint length, bool autoInit, bool stereo) {
        uint freqHz = _sb.FreqHz;
        DmaMode newMode = mode;
        uint newLength = length;

        // Equal length if data format and dma channel are both 16-bit or 8-bit
        // Reference: src/hardware/audio/soundblaster.cpp dsp_prepare_dma_new() lines 1651-1677
        if (mode == DmaMode.Pcm16Bit) {
            if (_secondaryDmaChannel is not null) {
                _sb.Dma.Channel = _secondaryDmaChannel;
            } else {
                _sb.Dma.Channel = _primaryDmaChannel;
                newMode = DmaMode.Pcm16BitAliased;
                // UNDOCUMENTED: In aliased mode sample length is written to DSP as
                // number of 16-bit samples so we need double 8-bit DMA buffer length
                newLength *= 2;
            }
        } else {
            _sb.Dma.Channel = _primaryDmaChannel;
        }

        // Set the length to the correct register depending on mode
        // Reference: src/hardware/audio/soundblaster.cpp dsp_prepare_dma_new() lines 1679-1683
        if (autoInit) {
            _sb.Dma.AutoSize = newLength;
        } else {
            _sb.Dma.SingleSize = newLength;
        }

        DspDoDmaTransfer(newMode, freqHz, autoInit, stereo);
    }

    private void SetSpeakerEnabled(bool enabled) {
        // Speaker output is always enabled on the SB16 and ESS cards; speaker
        // enable/disable commands are simply ignored. Only the SB Pro and
        // earlier models can toggle the speaker-output.
        // Reference: src/hardware/audio/soundblaster.cpp dsp_enable_speaker()
        if (_config.SbType == SbType.Sb16 || _sb.EssType != EssType.None) {
            return;
        }
        if (_sb.SpeakerEnabled == enabled) {
            return;
        }

        // If the speaker's being turned on, then flush old
        // content before releasing the channel for playback.
        // Reference: src/hardware/audio/soundblaster.cpp set_speaker_enabled()
        if (enabled) {
            _scheduler.RemoveEvents(SuppressDmaTransfer);
            FlushRemainingDmaTransfer();

            // Speaker powered-on after cold-state, give it warmup time
            _sb.Dsp.WarmupRemainingMs = _sb.Dsp.ColdWarmupMs;
        }

        _sb.SpeakerEnabled = enabled;
    }

    private bool ShouldUseHighDmaChannel() {
        return _config.SbType == SbType.Sb16 &&
               _config.HighDma >= 5 &&
               _config.HighDma != _config.LowDma;
    }

    private void InitSpeakerState() {
        // Reference: src/hardware/audio/soundblaster.cpp init_speaker_state()
        if (_config.SbType == SbType.Sb16 || _sb.EssType != EssType.None) {
            // Speaker output (DAC output) is always enabled on the SB16 and
            // ESS cards. Because the channel is active, we treat this as a
            // startup event.
            bool isColdStart = _sb.Dsp.ResetTally <= DspInitialResetLimit;
            _sb.Dsp.WarmupRemainingMs = isColdStart ? _sb.Dsp.ColdWarmupMs : _sb.Dsp.HotWarmupMs;
            _sb.SpeakerEnabled = true;
        } else {
            // SB Pro and earlier models have the speaker-output disabled by default.
            _sb.SpeakerEnabled = false;
        }
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        // Don't register any ports when Sound Blaster is disabled or has no base address
        if (_config.SbType == SbType.None || _config.BaseAddress == 0) {
            return;
        }

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

    /// <summary>
    /// Flushes the DSP output FIFO.
    /// Reference: src/hardware/audio/soundblaster.cpp dsp_flush_data()
    /// </summary>
    private void DspFlushData() {
        _sb.Dsp.Out.Used = 0;
        _sb.Dsp.Out.Pos = 0;
    }

    /// <summary>
    /// Adds a byte to the DSP output FIFO (circular buffer of DspBufSize=64).
    /// Reference: src/hardware/audio/soundblaster.cpp dsp_add_data()
    /// </summary>
    private void DspAddData(byte value) {
        if (_sb.Dsp.Out.Used < DspBufSize) {
            int start = _sb.Dsp.Out.Used + _sb.Dsp.Out.Pos;
            if (start >= DspBufSize) {
                start -= DspBufSize;
            }
            _sb.Dsp.Out.Data[start] = value;
            _sb.Dsp.Out.Used++;
        } else {
            _loggerService.Error("SOUNDBLASTER: DSP output buffer full");
        }
    }

    /// <summary>
    /// Reads from the DSP output FIFO. Returns last value if empty (sticky).
    /// Reference: src/hardware/audio/soundblaster.cpp dsp_read_data()
    /// </summary>
    private byte DspReadData() {
        if (_sb.Dsp.Out.Used > 0) {
            _sb.Dsp.Out.LastVal = _sb.Dsp.Out.Data[_sb.Dsp.Out.Pos];
            _sb.Dsp.Out.Pos++;
            if (_sb.Dsp.Out.Pos >= DspBufSize) {
                _sb.Dsp.Out.Pos -= DspBufSize;
            }
            _sb.Dsp.Out.Used--;
        }
        return _sb.Dsp.Out.LastVal;
    }

    /// <summary>
    /// Determines if the DSP write buffer is at capacity.
    /// Reference: src/hardware/audio/soundblaster.cpp write_buffer_at_capacity()
    /// </summary>
    private bool WriteBufferAtCapacity() {
        // Is the DSP in an abnormal state?
        if (_sb.Dsp.State != DspState.Normal) {
            return true;
        }

        // Report the buffer as having some room every 8th call
        if ((++_sb.Dsp.WriteStatusCounter % 8) == 0) {
            return false;
        }

        // If DMA isn't running then the buffer's definitely not at capacity
        if (_sb.Dma.Mode == DmaMode.None) {
            return false;
        }

        // The DMA buffer is considered full until it can accept a full write
        return _sb.Dma.Left > _sb.Dma.Min;
    }

    /// <summary>
    /// Event callback for delayed IRQ raising.
    /// Reference: src/hardware/audio/soundblaster.cpp dsp_raise_irq_event()
    /// </summary>
    private void DspRaiseIrqEvent(uint val) {
        RaiseIrq(SbIrq.Irq8);
    }

    public void RaiseInterruptRequest() {
        RaiseIrq(SbIrq.Irq8);
    }

    /// <summary>
    /// Gets the configured Sound Blaster type.
    /// </summary>
    public SbType SbTypeProperty => _config.SbType;

    /// <summary>
    /// Gets the configured IRQ line.
    /// </summary>
    public byte IRQ => _config.Irq;

    /// <summary>
    /// Gets the configured base I/O address.
    /// </summary>
    public ushort BaseAddress => _config.BaseAddress;

    /// <summary>
    /// Gets the configured 8-bit DMA channel.
    /// </summary>
    public byte LowDma => _config.LowDma;

    /// <summary>
    /// Gets the configured 16-bit DMA channel.
    /// </summary>
    public byte HighDma => _config.HighDma;

    /// <summary>
    /// Gets the BLASTER environment variable string.
    /// </summary>
    public string BlasterString {
        get {
            string highChannelSegment = ShouldUseHighDmaChannel() ? $" H{_config.HighDma}" : string.Empty;
            return $"A{_config.BaseAddress:X3} I{_config.Irq} D{_config.LowDma}{highChannelSegment} T{(int)_config.SbType}";
        }
    }
}