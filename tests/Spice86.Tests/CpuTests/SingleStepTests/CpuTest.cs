namespace Spice86.Tests.CpuTests.SingleStepTests;

using System.Text.Json;
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
[JsonConverter(typeof(RegistersJsonConverter))]
public class Registers {
    public uint? EAX { get; set; }
    public uint? EBX { get; set; }
    public uint? ECX { get; set; }
    public uint? EDX { get; set; }
    public uint? CS { get; set; }
    public uint? SS { get; set; }
    public uint? FS { get; set; }
    public uint? GS { get; set; }
    public uint? DS { get; set; }
    public uint? ES { get; set; }
    public uint? ESP { get; set; }
    public uint? EBP { get; set; }
    public uint? ESI { get; set; }
    public uint? EDI { get; set; }
    public uint? EIP { get; set; }
    public uint? EFlags { get; set; }
}

/// <summary>
/// Custom JSON converter that supports both 16-bit (ax, ip) and 32-bit (eax, eip) register names
/// </summary>
public class RegistersJsonConverter : JsonConverter<Registers> {
    public override Registers Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        var registers = new Registers();

        if (reader.TokenType != JsonTokenType.StartObject) {
            throw new JsonException();
        }

        while (reader.Read()) {
            if (reader.TokenType == JsonTokenType.EndObject) {
                return registers;
            }

            if (reader.TokenType != JsonTokenType.PropertyName) {
                throw new JsonException();
            }

            string propertyName = reader.GetString()!.ToLowerInvariant();
            reader.Read();

            uint? value = reader.TokenType == JsonTokenType.Number ? reader.GetUInt32() : null;

            switch (propertyName) {
                case "ax":
                case "eax":
                    registers.EAX = value;
                    break;
                case "bx":
                case "ebx":
                    registers.EBX = value;
                    break;
                case "cx":
                case "ecx":
                    registers.ECX = value;
                    break;
                case "dx":
                case "edx":
                    registers.EDX = value;
                    break;
                case "cs":
                    registers.CS = value;
                    break;
                case "ss":
                    registers.SS = value;
                    break;
                case "fs":
                    registers.FS = value;
                    break;
                case "gs":
                    registers.GS = value;
                    break;
                case "ds":
                    registers.DS = value;
                    break;
                case "es":
                    registers.ES = value;
                    break;
                case "sp":
                case "esp":
                    registers.ESP = value;
                    break;
                case "bp":
                case "ebp":
                    registers.EBP = value;
                    break;
                case "si":
                case "esi":
                    registers.ESI = value;
                    break;
                case "di":
                case "edi":
                    registers.EDI = value;
                    break;
                case "ip":
                case "eip":
                    registers.EIP = value;
                    break;
                case "flags":
                case "eflags":
                    registers.EFlags = value;
                    break;
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Registers value, JsonSerializerOptions options) {
        writer.WriteStartObject();

        if (value.EAX.HasValue) writer.WriteNumber("eax", value.EAX.Value);
        if (value.EBX.HasValue) writer.WriteNumber("ebx", value.EBX.Value);
        if (value.ECX.HasValue) writer.WriteNumber("ecx", value.ECX.Value);
        if (value.EDX.HasValue) writer.WriteNumber("edx", value.EDX.Value);
        if (value.CS.HasValue) writer.WriteNumber("cs", value.CS.Value);
        if (value.SS.HasValue) writer.WriteNumber("ss", value.SS.Value);
        if (value.FS.HasValue) writer.WriteNumber("fs", value.FS.Value);
        if (value.GS.HasValue) writer.WriteNumber("gs", value.GS.Value);
        if (value.DS.HasValue) writer.WriteNumber("ds", value.DS.Value);
        if (value.ES.HasValue) writer.WriteNumber("es", value.ES.Value);
        if (value.ESP.HasValue) writer.WriteNumber("esp", value.ESP.Value);
        if (value.EBP.HasValue) writer.WriteNumber("ebp", value.EBP.Value);
        if (value.ESI.HasValue) writer.WriteNumber("esi", value.ESI.Value);
        if (value.EDI.HasValue) writer.WriteNumber("edi", value.EDI.Value);
        if (value.EIP.HasValue) writer.WriteNumber("eip", value.EIP.Value);
        if (value.EFlags.HasValue) writer.WriteNumber("eflags", value.EFlags.Value);

        writer.WriteEndObject();
    }
}