namespace Spice86.Emulator.InterruptHandlers.Dos;

public class DosFileOperationResult {
    private readonly bool _error;
    private readonly uint? _value;
    private readonly bool _valueIsUint32;

    private DosFileOperationResult(bool error, bool valueIsUint32, uint? value) {
        this._error = error;
        this._valueIsUint32 = valueIsUint32;
        this._value = value;
    }

    public static DosFileOperationResult Error(uint errorCode) {
        return new DosFileOperationResult(true, false, errorCode);
    }

    public static DosFileOperationResult NoValue() {
        return new DosFileOperationResult(false, false, null);
    }

    public static DosFileOperationResult Value16(ushort value) {
        return new DosFileOperationResult(false, false, value);
    }

    public static DosFileOperationResult Value32(uint value) {
        return new DosFileOperationResult(false, true, value);
    }

    public uint? Value => _value;

    public bool IsError => _error;

    public bool IsValueIsUint32 => _valueIsUint32;
}