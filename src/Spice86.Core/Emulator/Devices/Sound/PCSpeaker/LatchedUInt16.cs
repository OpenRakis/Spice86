namespace Spice86.Core.Emulator.Devices.Sound.PCSpeaker;

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
    public LatchedUInt16(ushort value) => _value = value;

    /// <summary>
    /// Occurs when the value has changed.
    /// </summary>
    public event EventHandler? ValueChanged;

    /// <summary>
    /// Implicitly converts a LatchedUInt16 to ushort by returning the underlying value if not null, otherwise returns 0.
    /// </summary>
    /// <param name="value">The LatchedUInt16 object to convert.</param>
    /// <returns>The underlying ushort value of the LatchedUInt16 object, or 0 if the object is null.</returns>
    public static implicit operator ushort(LatchedUInt16 value) => value._value;

    /// <summary>
    /// Implicitly converts a ushort to a LatchedUInt16 object by wrapping it in a new LatchedUInt16 instance.
    /// </summary>
    /// <param name="value">The ushort value to convert to a LatchedUInt16 object.</param>
    /// <returns>A new LatchedUInt16 object wrapping the specified ushort value.</returns>
    public static implicit operator LatchedUInt16(ushort value) => new(value);

    /// <summary>
    /// Returns the next byte of the value.
    /// </summary>
    /// <returns>The next byte of the value.</returns>
    public byte ReadByte() {
        if (_readLow) {
            _readLow = false;
            return _latchedHighByte;
        } else {
            _readLow = true;
            ushort value = _value;
            _latchedHighByte = (byte)(value >> 8);
            return (byte)value;
        }
    }
    /// <summary>
    /// Writes the next byte of the value.
    /// </summary>
    /// <param name="value">The next byte of the value.</param>
    public void WriteByte(byte value) {
        if (_wroteLow) {
            _wroteLow = false;
            _value = (ushort)(value << 8 | _latchedLowByte);
            OnValueChanged(EventArgs.Empty);
        } else {
            _wroteLow = true;
            _latchedLowByte = value;
        }
    }
    /// <summary>
    /// Sets the full 16-bit value.
    /// </summary>
    /// <param name="value">The full 16-bit value.</param>
    public void SetValue(ushort value) {
        _value = value;
        OnValueChanged(EventArgs.Empty);
    }
    /// <summary>
    /// Returns a string representation of the value.
    /// </summary>
    /// <returns>String representation of the value.</returns>
    public override string ToString() => _value.ToString();

    /// <summary>
    /// Raises the ValueChanged event.
    /// </summary>
    /// <param name="e">Unused EventArgs instance.</param>
    private void OnValueChanged(EventArgs e) => ValueChanged?.Invoke(this, e);
}
