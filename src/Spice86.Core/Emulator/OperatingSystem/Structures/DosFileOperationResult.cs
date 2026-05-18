namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Represents the result of a DOS file operation, which could be an error or a value.
/// </summary>
public sealed class DosFileOperationResult : IEquatable<DosFileOperationResult?> {
    // Cache the most common error codes into a fast lookup array.
    private const int ErrorCacheLength = 20;
    private static readonly DosFileOperationResult?[] s_errorCache = new DosFileOperationResult[ErrorCacheLength];

    // Cache the common "no value" result to avoid extra allocations.
    private static readonly DosFileOperationResult s_noValue = new(error: false, valueIsUint32: false, value: null);

    private readonly uint? _value;
    private readonly bool _error;
    private readonly bool _valueIsUint32;
    private readonly byte _refCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="DosFileOperationResult" /> class with the specified error state, value type,
    /// value, and handle count.
    /// </summary>
    /// <param name="error"><c>true</c> if the operation resulted in an error; otherwise, <c>false</c>.</param>
    /// <param name="valueIsUint32"><c>true</c> if the value represents a 32-bit unsigned integer; otherwise, <c>false</c>.</param>
    /// <param name="value">The value associated with the file operation, or <c>null</c> if no value is available.</param>
    /// <param name="refCount">The handle count for the associated file or device, after a successful <see cref="DosFileManager.CloseFileOrDevice(ushort)"/> operation.
    /// Defaults to 0 for other DOS operations.</param>
    private DosFileOperationResult(bool error, bool valueIsUint32, uint? value, byte refCount = 0) {
        _error = error;
        _valueIsUint32 = valueIsUint32;
        _value = value;
        _refCount = refCount;
    }

    /// <summary>
    /// Returns a new instance of the class indicating an error.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <returns>A new instance of the class indicating an error.</returns>
    public static DosFileOperationResult Error(DosErrorCode errorCode) {
        // Use cache for common error codes.
        if ((int)errorCode is >= 0 and < ErrorCacheLength) {
            ref DosFileOperationResult? errorCacheEntry = ref s_errorCache[(uint)errorCode];
            errorCacheEntry ??= NewErrorResult(errorCode);
            return errorCacheEntry;
        }

        return NewErrorResult(errorCode);

        static DosFileOperationResult NewErrorResult(DosErrorCode errorCode)
            => new(error: true, valueIsUint32: false, value: (uint?)errorCode);
    }

    /// <summary>
    /// Returns a new instance of the class indicating no value.
    /// </summary>
    /// <returns>A new instance of the class indicating no value.</returns>
    public static DosFileOperationResult NoValue() {
        return s_noValue;
    }

    /// <summary>
    /// Returns a new instance of the class with a 16-bit value.
    /// </summary>
    /// <param name="value">The 16-bit value.</param>
    /// <returns>A new instance of the class with a 16-bit value.</returns>
    public static DosFileOperationResult Value16(ushort value) {
        return new DosFileOperationResult(error: false, valueIsUint32: false, value);
    }

    /// <summary>
    /// Returns a new instance of the class with a 32-bit value.
    /// </summary>
    /// <param name="value">The 32-bit value.</param>
    /// <returns>A new instance of the class with a 32-bit value.</returns>
    public static DosFileOperationResult Value32(uint value) {
        return new DosFileOperationResult(error: false, valueIsUint32: true, value);
    }

    /// <summary>
    /// Returns a new instance of the class indicating no value with a reference count.
    /// </summary>
    /// <param name="refCount">The reference count from the System File Table.</param>
    /// <returns>A new instance of the class with a reference count.</returns>
    public static DosFileOperationResult NoValueWithRefCount(byte refCount) {
        return new DosFileOperationResult(error: false, valueIsUint32: false, value: null, refCount);
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

    /// <summary>
    /// The number of handles associated with the file or device after a successful <see cref="DosFileManager.CloseFileOrDevice(ushort)"/> operation. Defaults to 0 for other DOS operations.
    /// </summary>
    public byte RefCount => _refCount;

    public override bool Equals(object? obj) {
        return Equals(obj as DosFileOperationResult);
    }

    public bool Equals(DosFileOperationResult? other) {
        return other is not null &&
               Value == other.Value &&
               IsError == other.IsError &&
               IsValueIsUint32 == other.IsValueIsUint32 &&
               RefCount == other.RefCount;
    }

    public override int GetHashCode() {
        return HashCode.Combine(Value, IsError, IsValueIsUint32, RefCount);
    }
}
