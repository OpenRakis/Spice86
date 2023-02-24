namespace Spice86.Core.Emulator.Errors;

public abstract class CpuException : Exception {
    public byte Vector { get; }
    public CpuExceptionType Type { get; }
    public ushort? ErrorCode { get; }
    public string Mnemonic { get; }

    protected CpuException(string message, byte vector, CpuExceptionType type, string mnemonic, ushort? errorCode = null)
        : base(message) {
        Vector = vector;
        Type = type;
        Mnemonic = mnemonic;
        ErrorCode = errorCode;
    }

    public override string ToString() {
        return $"CPU Exception: {Type}: {Mnemonic} - {Message} [{ErrorCode:X4}]";
    }
}