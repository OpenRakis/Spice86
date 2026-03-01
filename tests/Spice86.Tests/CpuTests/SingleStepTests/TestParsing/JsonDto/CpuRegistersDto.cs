namespace Spice86.Tests.CpuTests.SingleStepTests.TestParsing.JsonDto;

using System.Text.Json.Serialization;

/// <summary>
/// JSON DTO for CpuRegisters deserialization with all nullable fields
/// Supports both 16-bit and 32-bit register names via JsonPropertyName aliases
/// </summary>
internal class CpuRegistersDto {
    // 32-bit general purpose registers (with 16-bit aliases)
    [JsonPropertyName("eax")] public uint? EAX { get; set; }
    [JsonPropertyName("ax")] public uint? AX { set => EAX = value; }

    [JsonPropertyName("ebx")] public uint? EBX { get; set; }
    [JsonPropertyName("bx")] public uint? BX { set => EBX = value; }

    [JsonPropertyName("ecx")] public uint? ECX { get; set; }
    [JsonPropertyName("cx")] public uint? CX { set => ECX = value; }

    [JsonPropertyName("edx")] public uint? EDX { get; set; }
    [JsonPropertyName("dx")] public uint? DX { set => EDX = value; }

    [JsonPropertyName("esp")] public uint? ESP { get; set; }
    [JsonPropertyName("sp")] public uint? SP { set => ESP = value; }

    [JsonPropertyName("ebp")] public uint? EBP { get; set; }
    [JsonPropertyName("bp")] public uint? BP { set => EBP = value; }

    [JsonPropertyName("esi")] public uint? ESI { get; set; }
    [JsonPropertyName("si")] public uint? SI { set => ESI = value; }

    [JsonPropertyName("edi")] public uint? EDI { get; set; }
    [JsonPropertyName("di")] public uint? DI { set => EDI = value; }

    // 16-bit segment registers
    [JsonPropertyName("cs")] public uint? CS { get; set; }
    [JsonPropertyName("ss")] public uint? SS { get; set; }
    [JsonPropertyName("ds")] public uint? DS { get; set; }
    [JsonPropertyName("es")] public uint? ES { get; set; }
    [JsonPropertyName("fs")] public uint? FS { get; set; }
    [JsonPropertyName("gs")] public uint? GS { get; set; }

    // Instruction pointer and flags (with 16-bit aliases)
    [JsonPropertyName("eip")] public uint? EIP { get; set; }
    [JsonPropertyName("ip")] public uint? IP { set => EIP = value; }

    [JsonPropertyName("eflags")] public uint? EFlags { get; set; }
    [JsonPropertyName("flags")] public uint? Flags { set => EFlags = value; }
}