namespace Spice86.Tests.CpuTests.SingleStepTests;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Represents an x86 CPU test case with initial and final states
/// From https://github.com/SingleStepTests/
/// </summary>
public class CpuTest {
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte[] Bytes { get; set; } = [];
    public CpuTestState Initial { get; set; } = new();
    public CpuTestState Final { get; set; } = new();
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Deserialize from JSON using DTO approach
    /// </summary>
    public static CpuTest FromJson(string json) {
        var dto = JsonSerializer.Deserialize<CpuTestDto>(json)
            ?? throw new JsonException("Failed to deserialize CpuTest");
        
        var test = new CpuTest {
            Index = dto.Index,
            Name = dto.Name,
            Bytes = dto.Bytes.Select(i => (byte)i).ToArray(),
            Initial = ToInitialModel(dto.Initial),
            Hash = dto.Hash
        };

        // Convert final state using initial registers as defaults
        test.Final = ToFinalModel(dto.Final, test.Initial.Registers);

        return test;
    }

    private static CpuTestState ToInitialModel(CpuTestStateDto dto) {
        return new CpuTestState {
            Registers = ToInitialModel(dto.Registers),
            Ram = ValidateRam(dto.Ram ?? [])
        };
    }

    private static CpuTestState ToFinalModel(CpuTestStateDto dto, CpuRegisters initialRegisters) {
        return new CpuTestState {
            Registers = ToFinalModel(dto.Registers, initialRegisters),
            Ram = ValidateRam(dto.Ram ?? [])
        };
    }

    private static CpuRegisters ToInitialModel(CpuRegistersDto dto) {
        return new CpuRegisters {
            EAX = RequireRegister(dto.EAX, "eax"),
            EBX = RequireRegister(dto.EBX, "ebx"),
            ECX = RequireRegister(dto.ECX, "ecx"),
            EDX = RequireRegister(dto.EDX, "edx"),
            ESP = RequireRegister(dto.ESP, "esp"),
            EBP = RequireRegister(dto.EBP, "ebp"),
            ESI = RequireRegister(dto.ESI, "esi"),
            EDI = RequireRegister(dto.EDI, "edi"),
            CS = RequireUshortRegister(dto.CS, "cs"),
            SS = RequireUshortRegister(dto.SS, "ss"),
            DS = RequireUshortRegister(dto.DS, "ds"),
            ES = RequireUshortRegister(dto.ES, "es"),
            FS = RequireUshortRegister(dto.FS, "fs"),
            GS = RequireUshortRegister(dto.GS, "gs"),
            EIP = RequireUshortRegister(dto.EIP, "eip"),
            EFlags = RequireRegister(dto.EFlags, "eflags")
        };
    }

    private static CpuRegisters ToFinalModel(CpuRegistersDto dto, CpuRegisters initialRegisters) {
        return new CpuRegisters {
            EAX = dto.EAX ?? initialRegisters.EAX,
            EBX = dto.EBX ?? initialRegisters.EBX,
            ECX = dto.ECX ?? initialRegisters.ECX,
            EDX = dto.EDX ?? initialRegisters.EDX,
            ESP = dto.ESP ?? initialRegisters.ESP,
            EBP = dto.EBP ?? initialRegisters.EBP,
            ESI = dto.ESI ?? initialRegisters.ESI,
            EDI = dto.EDI ?? initialRegisters.EDI,
            CS = ValidateUshort(dto.CS ?? initialRegisters.CS, "cs"),
            SS = ValidateUshort(dto.SS ?? initialRegisters.SS, "ss"),
            DS = ValidateUshort(dto.DS ?? initialRegisters.DS, "ds"),
            ES = ValidateUshort(dto.ES ?? initialRegisters.ES, "es"),
            FS = ValidateUshort(dto.FS ?? initialRegisters.FS, "fs"),
            GS = ValidateUshort(dto.GS ?? initialRegisters.GS, "gs"),
            EIP = ValidateUshort(dto.EIP ?? initialRegisters.EIP, "eip"),
            EFlags = dto.EFlags ?? initialRegisters.EFlags
        };
    }

    private static RamEntry[] ValidateRam(uint[][] ram) {
        var ramEntries = new RamEntry[ram.Length];
        for (int i = 0; i < ram.Length; i++) {
            var entry = ram[i];
            if (entry.Length != 2) {
                throw new InvalidTestException($"RAM entry must have exactly 2 elements (address, value), got {entry.Length}");
            }
            uint address = entry[0];
            uint value = entry[1];
            if (value > 0xFF) {
                throw new InvalidTestException($"RAM value {value} at address {address:X} exceeds byte range (0-255)");
            }
            ramEntries[i] = new RamEntry(address, (byte)value);
        }
        return ramEntries;
    }

    private static uint RequireRegister(uint? value, string name) {
        if (!value.HasValue) {
            throw new InvalidTestException($"Missing required register(s) in initial state: {name}");
        }
        return value.Value;
    }

    private static ushort RequireUshortRegister(uint? value, string name) {
        if (!value.HasValue) {
            throw new InvalidTestException($"Missing required register(s) in initial state: {name}");
        }
        return ValidateUshort(value.Value, name);
    }

    private static ushort ValidateUshort(uint value, string name) {
        if (value > ushort.MaxValue) {
            throw new InvalidTestException($"Value {value} for register {name} exceeds ushort.MaxValue (65535)");
        }
        return (ushort)value;
    }

    // Internal JSON DTOs - Used only for deserialization
    private class CpuTestDto {
        [JsonPropertyName("idx")] public int Index { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("bytes")] public uint[] Bytes { get; set; } = Array.Empty<uint>();
        [JsonPropertyName("initial")] public CpuTestStateDto Initial { get; set; } = new();
        [JsonPropertyName("final")] public CpuTestStateDto Final { get; set; } = new();
        [JsonPropertyName("hash")] public string Hash { get; set; } = string.Empty;
    }

    private class CpuTestStateDto {
        [JsonPropertyName("regs")] public CpuRegistersDto Registers { get; set; } = new();
        [JsonPropertyName("ram")] public uint[][]? Ram { get; set; }
        [JsonPropertyName("queue")] public object[]? Queue { get; set; } // Ignored
    }

    private class CpuRegistersDto {
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

        [JsonPropertyName("cs")] public uint? CS { get; set; }
        [JsonPropertyName("ss")] public uint? SS { get; set; }
        [JsonPropertyName("ds")] public uint? DS { get; set; }
        [JsonPropertyName("es")] public uint? ES { get; set; }
        [JsonPropertyName("fs")] public uint? FS { get; set; }
        [JsonPropertyName("gs")] public uint? GS { get; set; }

        [JsonPropertyName("eip")] public uint? EIP { get; set; }
        [JsonPropertyName("ip")] public uint? IP { set => EIP = value; }

        [JsonPropertyName("eflags")] public uint? EFlags { get; set; }
        [JsonPropertyName("flags")] public uint? Flags { set => EFlags = value; }
    }
}

/// <summary>
/// Represents registers and RAM states
/// </summary>
public class CpuTestState {
    public CpuRegisters Registers { get; set; } = new();
    public RamEntry[] Ram { get; set; } = Array.Empty<RamEntry>();
}

/// <summary>
/// Represents registers states
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

/// <summary>
/// Represents a RAM value at a given address
/// </summary>
/// <param name="Address"></param>
/// <param name="Value"></param>
public record RamEntry(uint Address, byte Value);

/// <summary>
/// Thrown when test json parsing fails, should never happen but nothing is perfect :)
/// </summary>
public class InvalidTestException : Exception {
    public InvalidTestException() : base() { }
    public InvalidTestException(string message) : base(message) { }
    public InvalidTestException(string message, Exception innerException) : base(message, innerException) { }
}
