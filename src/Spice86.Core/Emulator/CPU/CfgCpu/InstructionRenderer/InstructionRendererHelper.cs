namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Helps instruction instances render themselves as assembly strings.
/// Convention we try to follow is MASM.
/// </summary>
public class InstructionRendererHelper {
    private readonly RegisterRenderer _registerRenderer =  new();

    private string SizeToMasmPointer(int targetBitWidth) {
        return targetBitWidth switch {
            8 => "byte ptr",
            16 => "word ptr",
            32 => "dword ptr",
            _ => throw new ArgumentOutOfRangeException(nameof(targetBitWidth), targetBitWidth, "value not handled")
        };
    }

    public string ToAssemblyString(string mnemonic, params string[] parameters) {
        string suffix = "";
        if (parameters.Length > 0) {
            suffix = " " + string.Join(",", parameters);
        }
        return mnemonic.ToLower() + suffix;
    }
    
    public string ToAssemblyString(StringInstruction instruction, string mnemonic, params string[] parameters) {
        return RepPrefix(instruction) + ToAssemblyString(mnemonic, parameters);
    }

    private string RepPrefix(StringInstruction instruction) {
        if (instruction.RepPrefix is null) {
            return "";
        }
        if (!instruction.ChangesFlags) {
            return "rep ";
        }
        if (instruction.RepPrefix.ContinueZeroFlagValue) {
            return "repe ";
        }
        return "repne ";
    }

    public string ToStringCountSource(string countSource) {
        return countSource.Replace("helper.State.", string.Empty);
    }
    
    public string ToStringRegister(int registerIndex, int bitWidth) {
        return _registerRenderer.ToStringRegister(registerIndex, bitWidth);
    }

    public string ToStringSegmentRegister(int registerIndex) {
        return _registerRenderer.ToStringSegmentRegister(registerIndex);
    }

    public string ToString(InstructionField<byte> field) {
        if (field.UseValue) {
            return ToHex(field.Value);
        }

        return ToAbsolutePointer(8, field.PhysicalAddress);
    }

    public string ToString(InstructionField<ushort> field) {
        if (field.UseValue) {
            return ToHex(field.Value);
        }

        return ToAbsolutePointer(16, field.PhysicalAddress);
    }

    public string ToString(InstructionField<uint> field) {
        if (field.UseValue) {
            return ToHex(field.Value);
        }

        return ToAbsolutePointer(32, field.PhysicalAddress);
    }

    public string ToString(InstructionField<sbyte> field) {
        if (field.UseValue) {
            return ToHex(field.Value);
        }

        return ToAbsolutePointer(8, field.PhysicalAddress);
    }

    public string ToString(InstructionField<short> field) {
        if (field.UseValue) {
            return ToHex(field.Value);
        }

        return ToAbsolutePointer(16, field.PhysicalAddress);
    }

    public string ToString(InstructionField<int> field) {
        if (field.UseValue) {
            return ToHex(field.Value);
        }

        return ToAbsolutePointer(32, field.PhysicalAddress);
    }

    public string ToString(InstructionField<SegmentedAddress> field) {
        if (field.UseValue) {
            return ToHex(field.Value);
        }

        return ToAbsolutePointer(32, field.PhysicalAddress);
    }

    public string ToStringRm(int targetBitWidth, ModRmContext modRmContext) {
        if (modRmContext.MemoryAddressType == MemoryAddressType.NONE) {
            // then it's a register
            return ToStringR(targetBitWidth, modRmContext);
        }
        return ToStringMemoryAddress(targetBitWidth, modRmContext);
    }

    public string ToStringR(int bitWidth, ModRmContext modRmContext) {
        return ToStringRegister(modRmContext.RegisterMemoryIndex, bitWidth);
    }

    public string ToStringMemoryAddress(int targetBitWidth, ModRmContext modRmContext) {
        if (modRmContext.MemoryAddressType == MemoryAddressType.NONE) {
            return "";
        }

        if (modRmContext.SegmentIndex == null) {
            throw new ArgumentException("SegmentIndex is null");
        }
        string segment = ToStringSegmentRegister(modRmContext.SegmentIndex.Value);
        string offset = ToStringModRmMemoryOffset(modRmContext);
        return ToSegmentedPointer(targetBitWidth, segment, offset);
    }

    public string ToStringModRmMemoryOffset(ModRmContext modRmContext) {
        if (modRmContext.MemoryOffsetType == MemoryOffsetType.NONE) {
            return "";
        }

        string displacement = ToStringModRmDisplacement(modRmContext);
        string join = "+";
        if (displacement.StartsWith("-")) {
            displacement = displacement.Substring(1);
            join = "-";
        }
        string offset = ToStringModRmOffset(modRmContext);
        return JoinFiltered(join, [offset, displacement]);
    }

    public string ToStringModRmDisplacement(ModRmContext modRmContext) {
        if (modRmContext.DisplacementType == DisplacementType.ZERO) {
            return "";
        }

        return modRmContext.DisplacementType switch {
            DisplacementType.INT8 => ToString((InstructionField<sbyte>)EnsureNonNull(modRmContext.DisplacementField)),
            DisplacementType.INT16 => ToString((InstructionField<short>)EnsureNonNull(modRmContext.DisplacementField)),
            DisplacementType.INT32 => ToString((InstructionField<int>)EnsureNonNull(modRmContext.DisplacementField)),
            _ => throw new ArgumentOutOfRangeException(nameof(modRmContext.DisplacementType), modRmContext.DisplacementType, "value not handled")
        };
    }
    
    public string ToStringModRmOffset(ModRmContext modRmContext) {
        return modRmContext.ModRmOffsetType switch {
            ModRmOffsetType.BX_PLUS_SI => "BX+SI",
            ModRmOffsetType.BX_PLUS_DI => "BX+DI",
            ModRmOffsetType.BP_PLUS_SI => "BP+SI",
            ModRmOffsetType.BP_PLUS_DI => "BP+DI",
            ModRmOffsetType.SI => "SI",
            ModRmOffsetType.DI => "DI",
            ModRmOffsetType.OFFSET_FIELD_16 => ToString(EnsureNonNull(modRmContext.ModRmOffsetField)),
            ModRmOffsetType.BP => "BP",
            ModRmOffsetType.BX => "BX",
            ModRmOffsetType.EAX => "EAX",
            ModRmOffsetType.ECX => "ECX",
            ModRmOffsetType.EDX => "EDX",
            ModRmOffsetType.EBX => "EBX",
            ModRmOffsetType.SIB => ToStringSibValue(EnsureNonNull(modRmContext.SibContext)),
            ModRmOffsetType.EBP => "EBP",
            ModRmOffsetType.ESI => "ESI",
            ModRmOffsetType.EDI => "EDI",
            _ => throw new ArgumentOutOfRangeException(nameof(modRmContext.ModRmOffsetType), modRmContext.ModRmOffsetType, "value not handled")
        };
    }

    private string ToStringSibValue(SibContext sibContext) {
        string @base = ToStringSibBase(sibContext);
        string index = ToStringSibIndex(sibContext);
        string indexExpression = JoinFiltered("*", [sibContext.Scale.ToString(), index]);
        return JoinFiltered("+", [@base, indexExpression]);
    }

    private string ToStringSibBase(SibContext sibContext) {
        return sibContext.SibBase switch {
            SibBase.EAX => "EAX",
            SibBase.ECX => "ECX",
            SibBase.EDX => "EDX",
            SibBase.EBX => "EBX",
            SibBase.ESP => "ESP",
            SibBase.BASE_FIELD_32 => ToString(EnsureNonNull(sibContext.BaseField)),
            SibBase.EBP => "EBP",
            SibBase.ESI => "ESI",
            SibBase.EDI => "EDI",
            _ => throw new ArgumentOutOfRangeException(nameof(sibContext.SibBase), sibContext.SibBase, "value not handled")
        };
    }
    
    private string ToStringSibIndex(SibContext sibContext) {
        return sibContext.SibIndex switch {
            SibIndex.EAX => "EAX",
            SibIndex.ECX => "ECX",
            SibIndex.EDX => "EDX",
            SibIndex.EBX => "EBX",
            SibIndex.ZERO => "0",
            SibIndex.EBP => "EBP",
            SibIndex.ESI => "ESI",
            SibIndex.EDI => "EDI",
            _ => throw new ArgumentOutOfRangeException(nameof(sibContext.SibIndex), sibContext.SibIndex, "value not handled")
        };
    }

    private T EnsureNonNull<T>(T? argument) {
        ArgumentNullException.ThrowIfNull(argument);
        return argument;
    }

    private string ToAbsolutePointer(int targetBitWidth, uint address) {
        return SizeToMasmPointer(targetBitWidth) + " " + ToPointer(ToHex(address));
    }
    
    public string ToSegmentedPointer(int targetBitWidth, InstructionWithSegmentRegisterIndexAndOffsetField<ushort> instruction) {
        return ToSegmentedPointer(targetBitWidth, instruction, ToString(instruction.OffsetField));
    }
    
    private string ToSegmentedPointer(int targetBitWidth, IInstructionWithSegmentRegisterIndex instruction, string offset) {
        return ToSegmentedPointer(targetBitWidth, instruction.SegmentRegisterIndex, offset);
    }

    public string ToSegmentedPointer(int targetBitWidth, int segmentRegisterIndex, string offsetExpression) {
        return ToSegmentedPointer(targetBitWidth, ToStringSegmentRegister(segmentRegisterIndex), offsetExpression);
    }

    public string ToSegmentedPointer(int targetBitWidth, string segmentExpression, string offsetExpression) {
        return SizeToMasmPointer(targetBitWidth) + " " + ToSegmentedAddress(segmentExpression, ToPointer(offsetExpression));
    }

    private string ToPointer(string expression) {
        return "[" + expression + "]";
    }

    public string ToSegmentedAddress(string segmentExpression, string offsetExpression) {
        return segmentExpression + ":" + offsetExpression;
    }

    public string ToHex(byte value) {
        return $"0x{value:X2}";
    }

    public string ToHex(ushort value) {
        return $"0x{value:X4}";
    }
    
    public string ToHex(uint value) {
        return $"0x{value:X8}";
    }
    
    public string ToHex(sbyte value) {
        if (value < 0) {
            return value.ToString();
        }
        return $"0x{value:X2}";
    }

    public string ToHex(short value) {
        if (value < 0) {
            return value.ToString();
        }
        return $"0x{value:X4}";
    }
    
    public string ToHex(int value) {
        if (value < 0) {
            return value.ToString();
        }
        return $"0x{value:X8}";
    }
    
    public string ToHex(SegmentedAddress segmentedAddress) {
        return segmentedAddress.ToString();
    }
    
    private static string JoinFiltered(string separator, string[] parameters) {
        string[] filteredParameters = parameters.Where(s => !string.IsNullOrEmpty(s) && !IsZero(s)).ToArray();
        return string.Join(separator, filteredParameters);
    }

    private static bool IsZero(string number) {
        foreach (char c in number) {
            if (c == 'x') {
                continue;
            }
            if (c != '0') {
                return false;
            }
        }
        return true;
    }
}