namespace Spice86.Emulator.Function;

public enum ValueOperation {
    READ,

    WRITE
}

public static class ValueOperationExtension {

    public static ValueOperation OppositeOperation(this ValueOperation instance) {
        if (instance == ValueOperation.READ) {
            return ValueOperation.WRITE;
        }
        return ValueOperation.READ;
    }
}