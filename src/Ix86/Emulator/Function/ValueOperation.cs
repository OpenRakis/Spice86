namespace Ix86.Emulator.Function;
public enum ValueOperation
{
    READ,
    WRITE,
}
public static class ValueOperationConverter
{
    public static ValueOperation OppositeOperation(ValueOperation value)
    {
        if(value == ValueOperation.READ)
        {
            return ValueOperation.WRITE;
        }
        return ValueOperation.READ;
    }
}
