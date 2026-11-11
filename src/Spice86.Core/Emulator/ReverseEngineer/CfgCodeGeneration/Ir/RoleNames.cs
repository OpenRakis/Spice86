namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.Contracts;

using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

/// <summary>
/// The G4.2 semantic names of generated locals and typed parameters: the first rule that yields
/// one name wins - <b>sink</b> (the semantic parameter name of a service function or device
/// operation the value is passed to, a typed callee's parameter, propagated backwards to a fixed
/// point), <b>source</b> (a callee's named output, a frame slot), <b>role</b> (<c>index</c> for the
/// register term of a memory offset, <c>count</c> for a <c>loop</c>/<c>rep</c> counter,
/// <c>segment</c> / <c>offset</c> for the halves of a far pointer loaded by <c>lds</c>/<c>les</c>,
/// <c>result</c> for a returned output), else G4.1's generic name. A name two different sinks would
/// give counts as no name. Names are text only: value lifetimes and required CPU-state writes do
/// not change.
/// </summary>
internal static class RoleNames {
    /// <summary>The service/device/typed-call sink names the value read through
    /// <paramref name="value"/> receives at <paramref name="roots"/>: the semantic name of each
    /// contract or callee parameter one of <paramref name="readInstances"/> - or the register or
    /// flag node itself - feeds. Semantic means declared on the implementation: a parameter still
    /// named after its register or a generic <c>wordN</c> carries no meaning and is skipped.</summary>
    public static HashSet<string> SinkNames(IEnumerable<IVisitableAstNode> roots, IrSsaValue value,
        RegisterNode? register, IReadOnlySet<IVisitableAstNode>? readInstances,
        TypedSignatures? signatures) {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (IVisitableAstNode descendant in roots.SelectMany(IrAstChildren.Descendants)) {
            switch (descendant) {
                case InterruptServiceCallNode serviceCall:
                    CollectParameterNames(names, serviceCall.Arguments,
                        ServiceParameters(serviceCall), value, register, readInstances);
                    break;
                case DeviceOperationCallNode deviceCall:
                    CollectParameterNames(names, deviceCall.Arguments,
                        DeviceParameters(deviceCall), value, register, readInstances);
                    break;
                case StackArgumentCallNode stackCall:
                    CollectTypedCall(names, stackCall.Call, signatures, value);
                    break;
                case ProcedureCallHelperNode call:
                    CollectTypedCall(names, call, signatures, value);
                    break;
            }
        }
        return names;
    }

    /// <summary>The source names a definition gives the value: the callee's named output when the
    /// definition is a typed or service call's result lvalue of <paramref name="value"/>, or the
    /// frame slot name (camel-cased) when the defining statement reads a proved frame access.</summary>
    public static HashSet<string> SourceNames(IEnumerable<IVisitableAstNode> roots,
        IrSsaValue value, RegisterNode? register, TypedSignatures? signatures,
        FrameSlotPlan? framePlan) {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (IVisitableAstNode descendant in roots.SelectMany(IrAstChildren.Descendants)) {
            switch (descendant) {
                case InterruptServiceCallNode serviceCall:
                    for (int index = 0; index < serviceCall.Results.Count; index++) {
                        if (TypedSignatures.ValueOf(serviceCall.Results[index]) == value
                            && ReturnName(serviceCall.Service.Implementation, index)
                                is { } outputName && IsSemantic(outputName)) {
                            names.Add(outputName);
                        }
                    }
                    break;
                case StackArgumentCallNode stackCall:
                    CollectTypedOutputs(names, stackCall.Call, signatures, value);
                    break;
                case ProcedureCallHelperNode call:
                    CollectTypedOutputs(names, call, signatures, value);
                    break;
                case SegmentedPointerNode pointer
                    when framePlan is not null && framePlan.Names.TryGetValue(pointer, out string? slot):
                    names.Add(char.ToLowerInvariant(slot[0]) + slot[1..]);
                    break;
            }
        }
        return names;
    }

    /// <summary>The role name of a value at <paramref name="roots"/>: <c>index</c> when the value
    /// is the register term of a memory or table offset, <c>count</c> when a <c>loop</c> or
    /// <c>rep</c>-prefixed instruction counts on it, <c>segment</c> or <c>offset</c> for the halves
    /// of a far pointer loaded by a <c>lds</c>/<c>les</c> definition, or <see langword="null"/>
    /// when no role applies.</summary>
    public static string? RoleName(IEnumerable<IVisitableAstNode> roots, IrSsaValue value,
        RegisterNode? register, bool isSegmentRegister,
        IEnumerable<CfgInstruction> origins) {
        bool index = false;
        bool count = false;
        bool pointer = false;
        foreach (IVisitableAstNode descendant in roots.SelectMany(IrAstChildren.Descendants)) {
            switch (descendant) {
                case SegmentedPointerNode segmented
                    when ContainsValue(segmented.Offset, value, register, null):
                    index = true;
                    break;
                case AbsolutePointerNode absolute
                    when ContainsValue(absolute.AbsoluteAddress, value, register, null):
                    index = true;
                    break;
            }
        }
        foreach (CfgInstruction instruction in origins) {
            switch (instruction.DisplayAst.Operation) {
                case InstructionOperation.LOOP or InstructionOperation.LOOPE
                    or InstructionOperation.LOOPNE:
                    count = true;
                    break;
                case InstructionOperation.LES or InstructionOperation.LDS or InstructionOperation.LSS
                    or InstructionOperation.LFS or InstructionOperation.LGS:
                    pointer = true;
                    break;
            }
            if (instruction.DisplayAst.RepPrefix is not null) {
                count = true;
            }
        }
        if (index) {
            return "index";
        }
        if (count) {
            return "count";
        }
        if (pointer) {
            return isSegmentRegister ? "segment" : "offset";
        }
        return null;
    }

    /// <summary>Whether <paramref name="name"/> carries meaning: not a G4.1 generic name and not a
    /// register- or flag-derived name (the contract kept the default).</summary>
    public static bool IsSemantic(string name) =>
        !System.Text.RegularExpressions.Regex.IsMatch(name, "^(byte|word|dword|flag)[0-9]+$")
        && !RegisterDerivedNames.Contains(name);

    /// <summary>The single name of <paramref name="names"/>, or <see langword="null"/> when zero
    /// or several different sinks name the value: an arbitrary choice between sinks would not be
    /// honest, so the next rule applies instead.</summary>
    public static string? UniqueName(HashSet<string> names) =>
        names.Count == 1 ? names.Single() : null;

    /// <summary>The first free name of base <paramref name="hint"/> in <paramref name="taken"/>:
    /// the name itself or with a numeric suffix (<c>count</c>, <c>count2</c>).</summary>
    public static string Next(string hint, IReadOnlySet<string> taken) {
        string name = hint;
        int number = 1;
        while (taken.Contains(name)) {
            name = $"{hint}{++number}";
        }
        return name;
    }

    /// <summary>Whether <paramref name="name"/> is a valid non-keyword C# identifier.</summary>
    public static bool IsIdentifier(string name) =>
        !string.IsNullOrEmpty(name)
        && (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(character => char.IsLetterOrDigit(character) || character == '_')
        && !Keywords.Contains(name);

    /// <summary>The semantic-name context register-local naming reads from: the program's typed
    /// signatures (callee-parameter sinks and named outputs) and the method's frame slots (named
    /// sources). Both may be absent in unit tests, in which case every rule degrades to the
    /// generic name.</summary>
    internal sealed record RoleNameContext(TypedSignatures? Signatures, FrameSlotPlan? FramePlan) {
        internal static readonly RoleNameContext Empty = new(null, null);
    }

    private static void CollectParameterNames(HashSet<string> names,
        IReadOnlyList<IVisitableAstNode> arguments, IReadOnlyList<ParameterInfo>? parameters,
        IrSsaValue value, RegisterNode? register, IReadOnlySet<IVisitableAstNode>? readInstances) {
        if (parameters is null) {
            return;
        }
        for (int index = 0; index < arguments.Count && index < parameters.Count; index++) {
            // A string parameter takes the string's segmented address as its argument: a register
            // inside it is a pointer half, not the string, so it carries no parameter name.
            if (parameters[index].ParameterType == typeof(string)) {
                continue;
            }
            if (parameters[index].Name is { } parameterName && IsSemantic(parameterName)
                && ContainsValue(arguments[index], value, register, readInstances)) {
                names.Add(parameterName);
            }
        }
    }

    private static void CollectTypedCall(HashSet<string> names, MethodCallNode call,
        TypedSignatures? signatures, IrSsaValue value) {
        if (call is not ProcedureCallHelperNode procedure
            || signatures?.TypedCallOf(procedure) is not { } signature) {
            return;
        }
        foreach (TypedParameter parameter in signature.Parameters) {
            if (TypedSignatures.ValueOf(parameter.Mask) == value && IsSemantic(parameter.Name)) {
                names.Add(parameter.Name);
            }
        }
    }

    private static void CollectTypedOutputs(HashSet<string> names, MethodCallNode call,
        TypedSignatures? signatures, IrSsaValue value) {
        if (call is not ProcedureCallHelperNode procedure
            || signatures?.TypedCallOf(procedure) is not { } signature) {
            return;
        }
        foreach (TypedParameter output in signature.Outputs) {
            if (TypedSignatures.ValueOf(output.Mask) == value && IsSemantic(output.Name)) {
                names.Add(output.Name);
            }
        }
    }

    private static bool ContainsValue(IVisitableAstNode root, IrSsaValue value,
        RegisterNode? register, IReadOnlySet<IVisitableAstNode>? readInstances) {
        foreach (IVisitableAstNode node in IrAstChildren.Descendants(root)) {
            if (readInstances?.Contains(node) == true
                || (register is not null && node.Equals(register))
                || TypedSignatures.ValueOf(node) == value) {
                return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<ParameterInfo>? ServiceParameters(InterruptServiceCallNode call) =>
        call.Service.Implementation?.GetParameters();

    /// <summary>The reflected parameters of a device operation: one name per argument of the
    /// printed call, from the device implementation methods the contracts declare.</summary>
    private static IReadOnlyList<ParameterInfo>? DeviceParameters(DeviceOperationCallNode call) {
        Type? receiver = DeviceOperationRegistry.ReceiverType(call.PropertyPath);
        if (receiver is null) {
            return null;
        }
        List<MethodInfo> candidates = receiver.GetMethods()
            .Where(method => method.Name == call.MethodName
                && method.GetParameters().Length == call.Arguments.Count)
            .ToList();
        return candidates.Count == 1 ? candidates[0].GetParameters() : null;
    }

    /// <summary>The declared name of a service implementation's output
    /// <paramref name="index"/>: the return tuple's element name, or <see langword="null"/> for a
    /// scalar return.</summary>
    private static string? ReturnName(MethodInfo? implementation, int index) =>
        implementation?.ReturnParameter
            .GetCustomAttribute<TupleElementNamesAttribute>()?.TransformNames?[index];

    /// <summary>Register- and flag-derived parameter names: they document the binding, not the
    /// meaning, so they count as no name.</summary>
    private static readonly HashSet<string> RegisterDerivedNames = new(StringComparer.Ordinal) {
        "ax", "cx", "dx", "bx", "bp", "si", "di", "sp",
        "al", "cl", "dl", "bl", "ah", "ch", "dh", "bh",
        "eax", "ecx", "edx", "ebx", "ebp", "esi", "edi", "esp",
        "es", "cs", "ss", "ds", "fs", "gs",
        "carryFlag", "parityFlag", "auxiliaryFlag", "zeroFlag", "signFlag", "overflowFlag",
        "directionFlag"
    };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal) {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
        "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while",
        "value", "var", "record", "nint", "nuint"
    };
}
