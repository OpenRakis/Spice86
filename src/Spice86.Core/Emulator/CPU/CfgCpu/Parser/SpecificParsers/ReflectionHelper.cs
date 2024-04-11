namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

using System.Linq;
using System.Reflection;

public class ReflectionHelper {
    public CfgInstruction BuildInstruction(string operation,
        int operandSize,
        SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        params object[] lastParams) {
        return BuildInstruction(operation, operation, operandSize, address, opcodeField, prefixes, lastParams);
    }

    public CfgInstruction BuildInstruction(string operationNamespace,
        string operation,
        int operandSize,
        SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        params object[] lastParams) {
        IEnumerable<Type> lastParamTypes = lastParams.Select(param => param.GetType());
        ConstructorInfo constructor = GetConstructor(operationNamespace, operation, operandSize, lastParamTypes);
        List<object> parameters = new List<object>();
        parameters.AddRange([address, opcodeField, prefixes]);
        parameters.AddRange(lastParams);
        return (CfgInstruction)constructor.Invoke(parameters.ToArray());
    }

    private ConstructorInfo GetConstructor(string operationNamespace, string operation, int operandSize, IEnumerable<Type> lastParamTypes) {
        List<Type> constructorSignature = new List<Type>();
        constructorSignature.AddRange([typeof(SegmentedAddress), typeof(InstructionField<byte>), typeof(List<InstructionPrefix>)]);
        constructorSignature.AddRange(lastParamTypes);
        Type type = GetType(operationNamespace, operation, operandSize);
        return GetConstructor(type, constructorSignature.ToArray());
    }

    private ConstructorInfo GetConstructor(Type type, Type[] constructorSignature) {
        ConstructorInfo? res = type.GetConstructor(constructorSignature);
        if (res == null) {
            string signature = string.Join(", ", (object?[])constructorSignature);
            throw new Exception($"Could not find a constructor for class {type} with parameters of type {signature}");
        }
        return res;
    }

    private Type GetType(string operationNamespace, string operation, int operandSize) {
        string? instructionsNamespace = typeof(Hlt).Namespace;
        string className = $"{instructionsNamespace}.{operationNamespace}.{operation}{operandSize}";
        Type? res = Type.GetType(className);
        if (res == null) {
            throw new Exception($"Could not find type with name {className}");
        }
        return res;
    }

    public int GetOperandSize(bool hasOperandSize8, bool hasOperandSize32) {
        return hasOperandSize8 ? 8 : hasOperandSize32 ? 32 : 16;
    }
}