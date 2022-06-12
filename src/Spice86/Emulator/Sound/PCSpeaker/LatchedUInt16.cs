namespace Spice86.Emulator.Sound.PCSpeaker;

using System;

/// <summary>
/// Allows a 16-bit value to be read or written one byte at a time.
/// </summary>
public sealed class LatchedUInt16 {
    private ushort _value;
    private bool _wroteLow;
    private bool _readLow;
    private byte _latchedHighByte;
    private byte _latchedLowByte;

    /// <summary>
    /// Initializes a new instance of the LatchedUInt16 class.
    /// </summary>
    public LatchedUInt16() {
    }
    /// <summary>
    /// Initializes a new instance of the LatchedUInt16 class.
    /// </summary>
    /// <param name="value">The initial value.</param>
    public LatchedUInt16(ushort value) => this._value = value;

    /// <summary>
    /// Occurs when the value has changed.
    /// </summary>
    public event EventHandler? ValueChanged;

    public static implicit operator ushort(LatchedUInt16 value) => value == null ? (ushort)0 : value._value;
    public static implicit operator LatchedUInt16(ushort value) => new(value);

    /// <summary>
    /// Returns the next byte of the value.
    /// </summary>
    /// <returns>The next byte of the value.</returns>
    public byte ReadByte() {
        if (this._readLow) {
            this._readLow = false;
            return this._latchedHighByte;
        } else {
            this._readLow = true;
            ushort value = this._value;
            this._latchedHighByte = (byte)(value >> 8);
            return (byte)value;
        }
    }
    /// <summary>
    /// Writes the next byte of the value.
    /// </summary>
    /// <param name="value">The next byte of the value.</param>
    public void WriteByte(byte value) {
        if (this._wroteLow) {
            this._wroteLow = false;
            this._value = (ushort)((value << 8) | this._latchedLowByte);
            OnValueChanged(EventArgs.Empty);
        } else {
            this._wroteLow = true;
            this._latchedLowByte = value;
        }
    }
    /// <summary>
    /// Sets the full 16-bit value.
    /// </summary>
    /// <param name="value">The full 16-bit value.</param>
    public void SetValue(ushort value) {
        this._value = value;
        this.OnValueChanged(EventArgs.Empty);
    }
    /// <summary>
    /// Returns a string representation of the value.
    /// </summary>
    /// <returns>String representation of the value.</returns>
    public override string ToString() => this._value.ToString();

    /// <summary>
    /// Raises the ValueChanged event.
    /// </summary>
    /// <param name="e">Unused EventArgs instance.</param>
    private void OnValueChanged(EventArgs e) => this.ValueChanged?.Invoke(this, e);
}
