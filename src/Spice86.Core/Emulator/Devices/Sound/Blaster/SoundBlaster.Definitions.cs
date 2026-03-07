namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

using Serilog.Events;

using Spice86.Audio.Backend;
using Spice86.Audio.Common;
using Spice86.Audio.Filters;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;

using System;
using System.Collections.Generic;

public partial class SoundBlaster {
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

        public class DacState {
            private readonly SbInfo _sb;

            public DacState(SbInfo sb) {
                _sb = sb;
            }

            public AudioFrame RenderFrame() {
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

        uint bytesAvailable = (DmaBufSize - bufferIndex) * 2;
        uint clampedWords = Math.Min(bytesRequested, bytesAvailable);
        if (is16BitChannel) {
            clampedWords /= 2;
        }

        try {
            int byteOffset = (int)bufferIndex * 2;
            Span<byte> byteBuffer = System.Runtime.InteropServices.MemoryMarshal.Cast<short, byte>(_sb.Dma.Buf16.AsSpan());
            Span<byte> targetBuffer = byteBuffer.Slice(byteOffset);
            int wordsRead = _sb.Dma.Channel.Read((int)clampedWords, targetBuffer);
            uint actualWordsRead = (uint)Math.Max(0, wordsRead);
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
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        1, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  1, 0, 0, 0,  0, 0, 0, 0,
        1, 2, 2, 0,  0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,
        2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        1, 0, 1, 0,  1, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0
    };

    private static readonly byte[] DspCommandLengthsSb16 = new byte[256] {
        0, 0, 0, 0,  1, 2, 0, 0,  1, 0, 0, 0,  0, 0, 2, 1,
        1, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  1, 0, 0, 0,  0, 0, 0, 0,
        1, 2, 2, 0,  0, 0, 0, 0,  2, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  2, 2, 2, 2,  0, 0, 0, 0,  0, 0, 0, 0,
        2, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,
        3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,  3, 3, 3, 3,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        1, 0, 1, 0,  1, 0, 0, 0,  0, 0, 0, 0,  0, 0, 0, 0,
        0, 0, 0, 0,  0, 0, 0, 0,  0, 1, 0, 0,  0, 0, 0, 0
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
    private readonly SoftwareMixer _mixer;
    private readonly SoundChannel _dacChannel;
    private readonly Opl3Fm _opl;
    private readonly EmulationLoopScheduler _scheduler;
    private readonly IEmulatedClock _clock;
    private readonly HardwareMixer _hardwareMixer;
    private readonly RWQueue<AudioFrame> _outputQueue;
    private readonly AudioFrame[] _enqueueBatch = new AudioFrame[4096];
    private int _enqueueBatchCount;
    private readonly AudioFrame[] _dequeueBatch = new AudioFrame[4096];
    private Queue<byte> _outputData = new Queue<byte>();
    private BlasterState _blasterState = BlasterState.WaitingForCommand;
    private double _lastDmaCallbackTime;
    private float _frameCounter = 0.0f;
    private int _framesNeeded = 0;
    private int _framesAddedThisTick = 0;
    private TimingType _timingType = TimingType.None;
    private const int MaxSingleFrameBaseCount = sizeof(short) * 2 - 1;
}