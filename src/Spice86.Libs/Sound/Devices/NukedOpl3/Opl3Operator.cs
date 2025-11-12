// SPDX-FileCopyrightText: 2013-2025 Nuked-OPL3 by nukeykt
// SPDX-License-Identifier: LGPL-2.1

namespace Spice86.Libs.Sound.Devices.NukedOpl3;

/* struct _opl3_slot {
 *     opl3_channel *channel;
 *     opl3_chip *chip;
 *     int16_t out;
 *     int16_t fbmod;
 *     int16_t *mod;
 *     int16_t prout;
 *     uint16_t eg_rout;
 *     uint16_t eg_out;
 *     uint8_t eg_inc;
 *     uint8_t eg_gen;
 *     uint8_t eg_rate;
 *     uint8_t eg_ksl;
 *     uint8_t *trem;
 *     uint8_t reg_vib;
 *     uint8_t reg_type;
 *     uint8_t reg_ksr;
 *     uint8_t reg_mult;
 *     uint8_t reg_ksl;
 *     uint8_t reg_tl;
 *     uint8_t reg_ar;
 *     uint8_t reg_dr;
 *     uint8_t reg_sl;
 *     uint8_t reg_rr;
 *     uint8_t reg_wf;
 *     uint8_t key;
 *     uint32_t pg_reset;
 *     uint32_t pg_phase;
 *     uint16_t pg_phase_out;
 *     uint8_t slot_num;
 * };
 */
public sealed class Opl3Operator {
    /// <summary>
    ///     Effective envelope rate index for current state (after KSR/KSL computations).
    /// </summary>
    internal byte EffectiveEnvelopeRateIndex;

    /// <summary>
    ///     Effective Key-Scale Level value combined with note/f-number (attenuation offset applied to TL).
    /// </summary>
    internal byte EffectiveKeyScaleLevel;

    /// <summary>
    ///     Envelope generator increment per sample tick (rate step accumulator).
    /// </summary>
    internal byte EnvelopeGeneratorIncrement;

    /// <summary>
    ///     Envelope generator current output level (attenuation value used to scale operator output).
    /// </summary>
    public ushort EnvelopeGeneratorLevel;

    /// <summary>
    ///     Envelope generator raw output routed to mixer (pre-scaling).
    /// </summary>
    public ushort EnvelopeGeneratorOutput;

    /// <summary>
    ///     Envelope generator state.
    ///     0 = Attack, 1 = Decay, 2 = Sustain, 3 = Release.
    /// </summary>
    public byte EnvelopeGeneratorState;

    /// <summary>
    ///     Feedback-modified signal fed back into the operator (used when operator is carrier with feedback).
    /// </summary>
    internal short FeedbackModifiedSignal;

    /// <summary>
    ///     External modulation source providing the modulator signal for this operator (usually previous operator in
    ///     algorithm).
    /// </summary>
    internal ShortSignalSource ModulationSource = ShortSignalSource.Zero;

    /// <summary>
    ///     Current operator audio output sample (linear, post-EG, post-waveform).
    /// </summary>
    internal short Out;

    /// <summary>
    ///     Phase generator output phase used to index waveform (reduced bits of PgPhase).
    /// </summary>
    internal ushort PhaseGeneratorOutput;

    /// <summary>
    ///     Previous output sample (used for feedback calculation).
    /// </summary>
    internal short PreviousOutputSample;

    /// <summary>
    ///     Register: AR (Attack Rate).
    ///     0..15 = Attack speed (0 = no attack, 15 = fastest).
    /// </summary>
    public byte RegAttackRate;

    /// <summary>
    ///     Register: DR (Decay Rate).
    ///     0..15 = Decay speed from peak to sustain level (0 = none, 15 = fastest).
    /// </summary>
    public byte RegDecayRate;

    /// <summary>
    ///     Register: MULT (frequency multiplier).
    ///     0..15 = Multiplier table index; 0 usually maps to 0.5, 1..15 map to 1..15 with specific chip table.
    /// </summary>
    public byte RegFrequencyMultiplier;

    /// <summary>
    ///     Register: KSL (Key Scale Level).
    ///     0..3 = Key-scaling curve amount applied to TL (0 = none, 3 = maximum).
    /// </summary>
    public byte RegKeyScaleLevel;

    /// <summary>
    ///     Register: KSR (Key Scale Rate).
    ///     0 = Rate not scaled by key number, 1 = Rate scaled by key number (higher pitch -> faster EG rates).
    /// </summary>
    public byte RegKeyScaleRate;

    /// <summary>
    ///     Internal key state for this operator.
    ///     0 = Key off, non-zero = Key on (key-on latched for EG/PG logic).
    /// </summary>
    internal byte RegKeyState;

    /// <summary>
    ///     Register: Operator type (modulator/carrier selection by algorithm; also AM enable on some variants).
    ///     Typically: 0 = Modulator, 1 = Carrier for the channel algorithm step.
    /// </summary>
    internal byte RegOperatorType;

    /// <summary>
    ///     Phase generator accumulator (phase counter in fixed-point domain).
    /// </summary>
    internal uint RegPhaseGeneratorAccumulator;

    /// <summary>
    ///     Phase generator reset request/flag (set when key-on or algorithm requires resetting phase).
    ///     Any non-zero value indicates a reset event.
    /// </summary>
    internal uint RegPhaseResetRequest;

    /// <summary>
    ///     Register: RR (Release Rate).
    ///     0..15 = Release speed after key-off (0 = none, 15 = fastest).
    /// </summary>
    public byte RegReleaseRate;

    /// <summary>
    ///     Register: SL (Sustain Level).
    ///     0..15 = Sustain level step (0 = maximum level, 15 = minimum; chip-specific mapping to attenuation).
    /// </summary>
    public byte RegSustainLevel;

    /// <summary>
    ///     Register: TL (Total Level).
    ///     0..63 = Output attenuation level (0 = loudest, larger value = quieter).
    /// </summary>
    public byte RegTotalLevel;

    /// <summary>
    ///     Register: VIB (vibrato enable).
    ///     0 = Vibrato off, 1 = Vibrato on (depth controlled globally).
    /// </summary>
    internal byte RegVibrato;

    /// <summary>
    ///     Register: WF (Waveform select).
    ///     0..7 = Waveform index (availability depends on OPL mode; common: 0 = sine, others = variants/rectified).
    /// </summary>
    internal byte RegWaveformSelect;

    /// <summary>
    ///     Slot index within the chip (0-based operator number).
    /// </summary>
    internal byte SlotIndex;

    /// <summary>
    ///     Back-reference to the owning channel this operator belongs to.
    /// </summary>
    internal Opl3Channel? Channel { get; set; }

    /// <summary>
    ///     Back-reference to the parent chip instance.
    /// </summary>
    internal Opl3Chip? Chip { get; set; }

    /// <summary>
    ///     Indicates whether the tremolo LFO is applied to this operator.
    /// </summary>
    internal bool TremoloEnabled { get; set; }

    /// <summary>
    ///     Value source exposing current operator output sample.
    /// </summary>
    internal ShortSignalSource OutputSignal => ShortSignalSource.FromOutput(this);

    /// <summary>
    ///     Value source exposing feedback-modified signal.
    /// </summary>
    internal ShortSignalSource FeedbackSignal => ShortSignalSource.FromFeedback(this);
}