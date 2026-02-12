namespace Spice86.Tests.CpuTests.SingleStepTests;

using System.Text.Json.Serialization;

/// <summary>
/// Represents an x86 CPU test case with initial and final states
/// From https://github.com/SingleStepTests/
/// </summary>
public class CpuTest {
    [JsonPropertyName("idx")] public int Index { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("bytes")] public uint[] Bytes { get; set; } = Array.Empty<uint>();
    [JsonPropertyName("initial")] public CpuState Initial { get; set; } = new();
    [JsonPropertyName("final")] public CpuState Final { get; set; } = new();
    [JsonPropertyName("hash")] public string Hash { get; set; } = string.Empty;
}

/// <summary>
/// Represents the CPU state including registers, RAM, and instruction queue
/// </summary>
public class CpuState {
    [JsonPropertyName("regs")] public Registers Registers { get; set; } = new();
    [JsonPropertyName("ram")] public uint[][] Ram { get; set; } = Array.Empty<uint[]>();
    [JsonPropertyName("queue")] public object[] Queue { get; set; } = Array.Empty<object>();
}

/// <summary>
/// Represents the x86 CPU registers
/// </summary>
public class Registers {
    [JsonPropertyName("ax")] public uint? AX { get; set; }
    [JsonPropertyName("bx")] public uint? BX { get; set; }
    [JsonPropertyName("cx")] public uint? CX { get; set; }
    [JsonPropertyName("dx")] public uint? DX { get; set; }
    [JsonPropertyName("cs")] public uint? CS { get; set; }
    [JsonPropertyName("ss")] public uint? SS { get; set; }
    [JsonPropertyName("fs")] public uint? FS { get; set; }
    [JsonPropertyName("gs")] public uint? GS { get; set; }
    [JsonPropertyName("ds")] public uint? DS { get; set; }
    [JsonPropertyName("es")] public uint? ES { get; set; }
    [JsonPropertyName("sp")] public uint? SP { get; set; }
    [JsonPropertyName("bp")] public uint? BP { get; set; }
    [JsonPropertyName("si")] public uint? SI { get; set; }
    [JsonPropertyName("di")] public uint? DI { get; set; }
    [JsonPropertyName("ip")] public uint? IP { get; set; }
    [JsonPropertyName("flags")] public uint? Flags { get; set; }
}



