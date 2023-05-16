namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Represents the result of a DOS file operation, which could be an error or a value.
/// </summary>
public class DosFileOperationResult {
    private readonly bool _error;
    private readonly uint? _value;
    private readonly bool _valueIsUint32;

    private DosFileOperationResult(bool error, bool valueIsUint32, uint? value) {
        _error = error;
        _valueIsUint32 = valueIsUint32;
        _value = value;
    }

    /// <summary>
    /// Returns a new instance of the class indicating an error.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <returns>A new instance of the class indicating an error.</returns>
    public static DosFileOperationResult Error(ErrorCode errorCode) {
        return new DosFileOperationResult(true, false, (uint?)errorCode);
    }

    /// <summary>
    /// Returns a new instance of the class indicating no value.
    /// </summary>
    /// <returns>A new instance of the class indicating no value.</returns>
    public static DosFileOperationResult NoValue() {
        return new DosFileOperationResult(false, false, null);
    }

    /// <summary>
    /// Returns a new instance of the class with a 16-bit value.
    /// </summary>
    /// <param name="value">The 16-bit value.</param>
    /// <returns>A new instance of the class with a 16-bit value.</returns>
    public static DosFileOperationResult Value16(ushort value) {
        return new DosFileOperationResult(false, false, value);
    }

    /// <summary>
    /// Returns a new instance of the class with a 32-bit value.
    /// </summary>
    /// <param name="value">The 32-bit value.</param>
    /// <returns>A new instance of the class with a 32-bit value.</returns>
    public static DosFileOperationResult Value32(uint value) {
        return new DosFileOperationResult(false, true, value);
    }

    /// <summary>
    /// The value of the operation, if any.
    /// </summary>
    public uint? Value => _value;

    /// <summary>
    /// Indicates whether the operation resulted in an error.
    /// </summary>
    public bool IsError => _error;

    /// <summary>
    /// Indicates whether the value of the operation is 32 bits.
    /// </summary>
    public bool IsValueIsUint32 => _valueIsUint32;
}