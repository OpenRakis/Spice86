namespace Spice86.Tests.CpuTests.SingleStepTests.TestParsing;

using Spice86.Tests.CpuTests.SingleStepTests.TestParsing.JsonDto;
using Spice86.Tests.CpuTests.SingleStepTests.TestParsing.Model;

/// <summary>
/// Converts CpuTest DTOs to domain models
/// </summary>
internal static class CpuTestConverter {
    /// <summary>
    /// Convert CpuTestDto to CpuTest model
    /// </summary>
    public static CpuTest ToModel(CpuTestDto dto) {
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

    /// <summary>
    /// Convert CpuStateDto to CpuState model for initial state
    /// </summary>
    public static CpuState ToInitialModel(CpuStateDto dto) {
        return new CpuState {
            Registers = ToInitialModel(dto.Registers),
            Ram = ValidateRam(dto.Ram ?? [])
        };
    }

    /// <summary>
    /// Convert CpuStateDto to CpuState model for final state, using initial registers as defaults
    /// </summary>
    public static CpuState ToFinalModel(CpuStateDto dto, CpuRegisters initialRegisters) {
        return new CpuState {
            Registers = ToFinalModel(dto.Registers, initialRegisters),
            Ram = ValidateRam(dto.Ram ?? [])
        };
    }

    /// <summary>
    /// Convert CpuRegistersDto to CpuRegisters model for initial state (all registers required)
    /// </summary>
    public static CpuRegisters ToInitialModel(CpuRegistersDto dto) {
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

    /// <summary>
    /// Convert CpuRegistersDto to CpuRegisters model for final state, using initial registers as defaults
    /// </summary>
    public static CpuRegisters ToFinalModel(CpuRegistersDto dto, CpuRegisters initialRegisters) {
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
}
