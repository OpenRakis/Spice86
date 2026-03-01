namespace Spice86.Tests.CpuTests.SingleStepTests.TestParsing.Model;

/// <summary>
/// Represents x86 CPU registers with proper types
/// </summary>
public class CpuRegisters {
    public uint EAX { get; set; }
    public uint EBX { get; set; }
    public uint ECX { get; set; }
    public uint EDX { get; set; }
    public ushort CS { get; set; }
    public ushort SS { get; set; }
    public ushort FS { get; set; }
    public ushort GS { get; set; }
    public ushort DS { get; set; }
    public ushort ES { get; set; }
    public uint ESP { get; set; }
    public uint EBP { get; set; }
    public uint ESI { get; set; }
    public uint EDI { get; set; }
    public ushort EIP { get; set; }
    public uint EFlags { get; set; }
}