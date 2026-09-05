namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Audio.Common;

using System;

/// <summary>
/// One of the 32 independent sample-playing voices on the Gravis UltraSound GF1 chip.
/// Each voice has independent wave-playback control (position, start, end, rate, loop)
/// and a volume-ramp envelope, plus stereo panning.
/// </summary>
/// <remarks>
/// 2022-2025 The DOSBox Staging Team
/// </remarks>
public sealed class GusVoice {
    // Control-state bit flags shared by wave and volume controls.
    private const byte CtrlReset = (byte)GusVoiceControl.Reset;
    private const byte CtrlStopped = (byte)GusVoiceControl.Stopped;
    private const byte CtrlDisabled = CtrlReset | CtrlStopped;
    private const byte CtrlBit16 = (byte)GusVoiceControl.Bit16;
    private const byte CtrlLoop = (byte)GusVoiceControl.Loop;
    private const byte CtrlBidirectional = (byte)GusVoiceControl.Bidirectional;
    private const byte CtrlRaiseIrq = (byte)GusVoiceControl.RaiseIrq;
    private const byte CtrlDecreasing = (byte)GusVoiceControl.Decreasing;

    /// <summary>Width used for wave-position fractional interpolation (2^9 = 512 sub-steps per sample).</summary>
    private const int WaveWidth = 1 << 9;

    /// <summary>8-bit samples are scaled to match the 16-bit range.</summary>
    private const float To16BitRange = 256f; // = 2^(16-8)

    private readonly GusVoiceIrq _voiceIrq;
    private readonly uint _irqMask;

    // Wave-control state

    /// <summary>Playback start address, in fixed-point units of 1/512 of a sample (registers 0x02/0x03).</summary>
    public int WaveStart { get; set; }
    /// <summary>Playback end address, in fixed-point units of 1/512 of a sample (registers 0x04/0x05).</summary>
    public int WaveEnd { get; set; }
    /// <summary>Current playback position, in fixed-point units of 1/512 of a sample (registers 0x0A/0x0B).</summary>
    public int WavePos { get; set; }
    /// <summary>Per-output-sample position increment, decoded from <see cref="WaveRate"/>.</summary>
    public int WaveInc { get; set; }
    /// <summary>Raw frequency-control register value (register 0x01).</summary>
    public ushort WaveRate { get; set; }
    /// <summary>Wave-control state flags (register 0x00): running, 16-bit, looping, bidirectional, direction, IRQ.</summary>
    public byte WaveState { get; set; } = CtrlDisabled;

    // Volume-ramp state

    /// <summary>Volume-ramp start level, in fixed-point volume-index units scaled by VolumeIncScalar (register 0x07).</summary>
    public int VolStart { get; set; }
    /// <summary>Volume-ramp end level, in fixed-point volume-index units scaled by VolumeIncScalar (register 0x08).</summary>
    public int VolEnd { get; set; }
    /// <summary>Current volume position, in fixed-point volume-index units scaled by VolumeIncScalar (register 0x09).</summary>
    public int VolPos { get; set; }
    /// <summary>Per-output-sample volume-index increment, decoded from <see cref="VolRate"/>.</summary>
    public int VolInc { get; set; }
    /// <summary>Raw volume-rate register value (register 0x06).</summary>
    public ushort VolRate { get; set; }
    /// <summary>Volume-control state flags (register 0x0D): ramping, looping, direction, IRQ.</summary>
    public byte VolState { get; set; } = CtrlDisabled;

    /// <summary>Pan position index, 0 (full left) to 15 (full right); 7 is centre (register 0x0C).</summary>
    public byte PanPosition { get; private set; } = GravisUltraSound.PanDefaultPosition;

    /// <summary>Milliseconds of 8-bit audio generated (for statistics).</summary>
    public uint Generated8BitMs { get; set; }

    /// <summary>Milliseconds of 16-bit audio generated (for statistics).</summary>
    public uint Generated16BitMs { get; set; }

    /// <summary>Decoded <see cref="WaveState"/> flags.</summary>
    public GusVoiceControl WaveControl => (GusVoiceControl)WaveState;

    /// <summary>Decoded <see cref="VolState"/> flags.</summary>
    public GusVoiceControl VolControl => (GusVoiceControl)VolState;

    /// <summary>True when the wave control is neither in reset nor stopped, so the voice advances its position.</summary>
    public bool IsPlaying => (WaveState & CtrlDisabled) == 0;

    /// <summary>True when this voice reads 16-bit samples from DRAM.</summary>
    public bool Is16BitSample => (WaveState & CtrlBit16) != 0;

    /// <summary>Initialises a voice with its index and a reference to the shared IRQ state.</summary>
    /// <param name="num">Voice number (0-31); determines this voice's bit in the shared IRQ masks.</param>
    /// <param name="voiceIrq">The IRQ state shared by all 32 voices.</param>
    internal GusVoice(byte num, GusVoiceIrq voiceIrq) {
        _voiceIrq = voiceIrq;
        _irqMask = 1u << num;
    }

    // Public interface

    /// <summary>
    /// Accumulates this voice's rendered samples into the first <paramref name="count"/> entries of <paramref name="frames"/>.
    /// </summary>
    /// <param name="ram">The 1 MiB GUS DRAM samples are read from.</param>
    /// <param name="volScalars">The shared logarithmic volume-scalar lookup table.</param>
    /// <param name="panScalars">The shared constant-power panning lookup table.</param>
    /// <param name="frames">The stereo output buffer to accumulate into.</param>
    /// <param name="count">Number of frames to render.</param>
    public void RenderFrames(byte[] ram, float[] volScalars, AudioFrame[] panScalars, AudioFrame[] frames, int count) {
        // Skip rendering only when BOTH wave AND vol controls are independently disabled,
        // matching the GF1 hardware behaviour documented in the UltraSound SDK.
        if ((WaveState & VolState & CtrlDisabled) != 0) {
            return;
        }

        AudioFrame pan = panScalars[PanPosition];

        for (int i = 0; i < count; i++) {
            float sample = GetSample(ram);
            sample *= PopVolScalar(volScalars);
            frames[i] = new AudioFrame(
                frames[i].Left + sample * pan.Left,
                frames[i].Right + sample * pan.Right);
        }

        if (Is16BitSample) {
            Generated16BitMs++;
        } else {
            Generated8BitMs++;
        }
    }

    /// <summary>Reads the wave-control state (register 0x80), with bit 7 set while a wave IRQ is pending for this voice.</summary>
    public byte ReadWaveState() => ReadCtrlState(WaveState, _voiceIrq.WaveState);

    /// <summary>Reads the volume-control state (register 0x8D), with bit 7 set while a volume IRQ is pending for this voice.</summary>
    public byte ReadVolState() => ReadCtrlState(VolState, _voiceIrq.VolState);

    /// <summary>Resets the wave and volume controls to their power-on (stopped) state, volume to zero, pan to centre.</summary>
    public void ResetCtrls() {
        VolPos = 0;
        UpdateVolState(0x01);
        UpdateWaveState(0x01);
        WritePanPot(GravisUltraSound.PanDefaultPosition);
    }

    /// <summary>Sets the pan position (register 0x0C write), clamped to the valid 0-15 range.</summary>
    public void WritePanPot(byte pos) {
        const byte MaxPos = GravisUltraSound.PanPositions - 1;
        PanPosition = Math.Min(pos, MaxPos);
    }

    /// <summary>Decodes a wave-rate register value into a per-sample position increment.</summary>
    public void WriteWaveRate(ushort val) {
        WaveRate = val;
        WaveInc = CeilUdivide(val, 2u);
    }

    /// <summary>
    /// Decodes a volume-rate register value into a per-sample volume-index increment.
    /// The register encodes four banks of fractional increments via bits 6-7.
    /// </summary>
    public void WriteVolRate(byte val) {
        VolRate = val;
        const byte BankLength = 63;
        int posInBank = val & BankLength;
        int decimator = 1 << (3 * (val >> 6));
        VolInc = CeilSdivide(posInBank * GravisUltraSound.VolumeIncScalar, decimator);
    }

    /// <summary>Updates wave control state; returns true when the voice IRQ flag changed.</summary>
    public bool UpdateWaveState(byte state) {
        uint origIrqState = _voiceIrq.WaveState;
        if ((state & 0xA0) == 0xA0) {
            _voiceIrq.WaveState |= _irqMask;
        } else {
            _voiceIrq.WaveState &= ~_irqMask;
        }
        WaveState = (byte)(state & 0x7F);
        return origIrqState != _voiceIrq.WaveState;
    }

    /// <summary>Updates volume control state; returns true when the voice IRQ flag changed.</summary>
    public bool UpdateVolState(byte state) {
        uint origIrqState = _voiceIrq.VolState;
        if ((state & 0xA0) == 0xA0) {
            _voiceIrq.VolState |= _irqMask;
        } else {
            _voiceIrq.VolState &= ~_irqMask;
        }
        VolState = (byte)(state & 0x7F);
        return origIrqState != _voiceIrq.VolState;
    }

    // Private rendering helpers

    private float GetSample(byte[] ram) {
        int pos = PopWavePos();
        int addr = pos / WaveWidth;
        int fraction = pos & (WaveWidth - 1);
        bool shouldInterpolate = WaveInc < WaveWidth && fraction != 0;
        float sample = Is16BitSample ? Read16BitSample(ram, addr) : Read8BitSample(ram, addr);

        if (shouldInterpolate) {
            float next = Is16BitSample ? Read16BitSample(ram, addr + 1) : Read8BitSample(ram, addr + 1);
            sample += (next - sample) * fraction / (float)WaveWidth;
        }

        return sample;
    }

    private int PopWavePos() {
        int current = WavePos;
        CtrlPosUpdate update = IncrementCtrlPos(WavePos, WaveState, WaveInc, WaveStart, WaveEnd,
            _voiceIrq.WaveState, CheckWaveRolloverCondition());
        WavePos = update.Pos;
        WaveState = update.State;
        _voiceIrq.WaveState = update.SharedIrqState;
        return current;
    }

    private float PopVolScalar(float[] volScalars) {
        int i = CeilSdivide(VolPos, GravisUltraSound.VolumeIncScalar);
        i = Math.Max(0, Math.Min(i, volScalars.Length - 1));
        CtrlPosUpdate update = IncrementCtrlPos(VolPos, VolState, VolInc, VolStart, VolEnd,
            _voiceIrq.VolState, false);
        VolPos = update.Pos;
        VolState = update.State;
        _voiceIrq.VolState = update.SharedIrqState;
        return volScalars[i];
    }

    private bool CheckWaveRolloverCondition() {
        // Rollover: volume control has BIT16 set and wave control has no LOOP.
        return (VolState & CtrlBit16) != 0 && (WaveState & CtrlLoop) == 0;
    }

    private readonly struct CtrlPosUpdate {
        public CtrlPosUpdate(int pos, byte state, uint sharedIrqState) {
            Pos = pos;
            State = state;
            SharedIrqState = sharedIrqState;
        }

        public int Pos { get; }
        public byte State { get; }
        public uint SharedIrqState { get; }
    }

    private CtrlPosUpdate IncrementCtrlPos(
        int pos,
        byte state,
        int inc,
        int start,
        int end,
        uint sharedIrqState,
        bool skipLoopOrRestart) {

        if ((state & CtrlDisabled) != 0) { return new CtrlPosUpdate(pos, state, sharedIrqState); }

        int remaining;
        if ((state & CtrlDecreasing) != 0) {
            pos -= inc;
            remaining = start - pos;
        } else {
            pos += inc;
            remaining = pos - end;
        }

        if (remaining < 0) { return new CtrlPosUpdate(pos, state, sharedIrqState); }

        if ((state & CtrlRaiseIrq) != 0) {
            sharedIrqState |= _irqMask;
        }

        if (skipLoopOrRestart) { return new CtrlPosUpdate(pos, state, sharedIrqState); }

        if ((state & CtrlLoop) != 0) {
            if ((state & CtrlBidirectional) != 0) {
                state ^= CtrlDecreasing;
            }
            pos = (state & CtrlDecreasing) != 0 ? end - remaining : start + remaining;
        } else {
            state |= 1; // stop the voice
            pos = (state & CtrlDecreasing) != 0 ? start : end;
        }

        return new CtrlPosUpdate(pos, state, sharedIrqState);
    }

    private static float Read8BitSample(byte[] ram, int addr) {
        int i = addr & 0xFFFFF;
        return (sbyte)ram[i] * To16BitRange;
    }

    private static float Read16BitSample(byte[] ram, int addr) {
        uint upper = (uint)addr & 0xC0000u;
        uint lower = (uint)addr & 0x1FFFFu;
        int i = (int)(upper | (lower << 1));
        return (short)(ram[i] | (ram[i + 1] << 8));
    }

    private byte ReadCtrlState(byte state, uint irqState) {
        byte result = state;
        if ((irqState & _irqMask) != 0) {
            result |= 0x80;
        }
        return result;
    }

    private static int CeilSdivide(int a, int b) {
        if (b == 0) { return 0; }
        if (a >= 0) { return (a + b - 1) / b; }
        return a / b;
    }

    private static int CeilUdivide(uint a, uint b) {
        if (b == 0) { return 0; }
        return (int)((a + b - 1) / b);
    }
}
