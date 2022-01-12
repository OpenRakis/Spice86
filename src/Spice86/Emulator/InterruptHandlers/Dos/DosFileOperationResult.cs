namespace Spice86.Emulator.InterruptHandlers.Dos;

public class DosFileOperationResult
{
    private readonly bool _error;
    private readonly bool _valueIsUint32;
    private readonly int? _value;

    public static DosFileOperationResult Error(int errorCode)
    {
        return new DosFileOperationResult(true, false, errorCode);
    }

    public static DosFileOperationResult Value16(int fileHandle)
    {
        return new DosFileOperationResult(false, false, fileHandle);
    }

    public static DosFileOperationResult Value32(int offset)
    {
        return new DosFileOperationResult(false, true, offset);
    }

    public static DosFileOperationResult NoValue()
    {
        return new DosFileOperationResult(false, false, null);
    }

    private DosFileOperationResult(bool error, bool valueIsUint32, int? value)
    {
        this._error = error;
        this._valueIsUint32 = valueIsUint32;
        this._value = value;
    }

    public bool IsError()
    {
        return _error;
    }

    public bool IsValueIsUint32()
    {
        return _valueIsUint32;
    }

    public int? GetValue()
    {
        return _value;
    }
}