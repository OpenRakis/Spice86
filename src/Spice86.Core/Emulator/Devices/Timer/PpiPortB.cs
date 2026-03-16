namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
///     Represents the programmable peripheral interface port B register used to gate PIT channel 2 and control speaker
///     and keyboard signals.
/// </summary>
public struct PpiPortB {
    /// <summary>
    ///     Gets or sets the raw register value. Individual bits expose timer, speaker, and keyboard wiring.
    /// </summary>
    public byte Data;

    private const int Timer2GatingBit = 0;
    private const int SpeakerOutputBit = 1;
    private const int ReadToggleBit = 4;
    private const int XtReadToggleBit = 5;
    private const int Timer2GatingAliasBit = 5;
    private const int XtClearKeyboardBit = 7;

    /// <summary>
    ///     Determines whether the specified bit is set in the given byte.
    /// </summary>
    /// <param name="value">Source byte to examine.</param>
    /// <param name="bit">Zero-based bit index.</param>
    /// <returns><see langword="true" /> when the bit is set; otherwise, <see langword="false" />.</returns>
    private static bool GetBit(byte value, int bit) {
        return (value & (1 << bit)) != 0;
    }

    /// <summary>
    ///     Sets or clears the specified bit in the given byte and returns the updated value.
    /// </summary>
    /// <param name="value">Source byte to modify.</param>
    /// <param name="bit">Zero-based bit index.</param>
    /// <param name="state">Desired bit state.</param>
    /// <returns>Modified byte containing the updated bit value.</returns>
    private static byte SetBit(byte value, int bit, bool state) {
        byte mask = (byte)(1 << bit);
        return state ? (byte)(value | mask) : (byte)(value & ~mask);
    }

    /// <summary>
    ///     Gets or sets the timer 2 gate bit routed to the speaker control path.
    /// </summary>
    public bool Timer2Gating {
        get => GetBit(Data, Timer2GatingBit);
        set => Data = SetBit(Data, Timer2GatingBit, value);
    }

    /// <summary>
    ///     Gets or sets the speaker data bit that drives the audio output flip-flop.
    /// </summary>
    public bool SpeakerOutput {
        get => GetBit(Data, SpeakerOutputBit);
        set => Data = SetBit(Data, SpeakerOutputBit, value);
    }

    /// <summary>
    ///     Gets or sets the XT/PC read toggle bit that flips with each port poll.
    /// </summary>
    public bool ReadToggle {
        get => GetBit(Data, ReadToggleBit);
        set => Data = SetBit(Data, ReadToggleBit, value);
    }

    /// <summary>
    ///     Gets or sets the XT-specific read toggle bit.
    /// </summary>
    public bool XtReadToggle {
        get => GetBit(Data, XtReadToggleBit);
        set => Data = SetBit(Data, XtReadToggleBit, value);
    }

    /// <summary>
    ///     Gets or sets the PC-specific alias of the timer 2 gate bit exposed on bit 5.
    /// </summary>
    public bool Timer2GatingAlias {
        get => GetBit(Data, Timer2GatingAliasBit);
        set => Data = SetBit(Data, Timer2GatingAliasBit, value);
    }

    /// <summary>
    ///     Gets or sets the XT keyboard buffer clear control.
    /// </summary>
    public bool XtClearKeyboard {
        get => GetBit(Data, XtClearKeyboardBit);
        set => Data = SetBit(Data, XtClearKeyboardBit, value);
    }

    /// <summary>
    ///     Gets or sets the combined view of bits 0 and 1, allowing timer 2 gating and speaker output to be addressed
    ///     together.
    /// </summary>
    /// <value>
    ///     Lower bit 0 reflects the gate state, and bit 1 reflects the speaker data line. Writes update the individual
    ///     properties atomically after masking to two bits.
    /// </value>
    public byte Timer2GatingAndSpeakerOut {
        get {
            int lower = Timer2Gating ? 1 : 0;
            int upper = SpeakerOutput ? 1 : 0;
            return (byte)(lower | (upper << 1));
        }
        set {
            byte masked = (byte)(value & 0x03);
            Timer2Gating = (masked & 0x01) != 0;
            SpeakerOutput = (masked & 0x02) != 0;
        }
    }
}