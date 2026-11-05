namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// The constant segment value a register version provably holds: a full definition assigning a
/// constant, a copy of a version that resolves to one, or a phi without an entry operand whose
/// operands all resolve to the same constant. Everything else is unknown.
/// </summary>
internal sealed class IrSegmentValueResolver {
    private readonly IrSsa _ssa;
    private readonly Dictionary<int, ushort?> _knownValues = new();

    /// <summary>Builds the resolver over the SSA of one method.</summary>
    public IrSegmentValueResolver(IrSsa ssa) => _ssa = ssa;

    /// <summary>The known value of a segment register <paramref name="version"/>: a full definition
    /// assigning a constant, or a phi without an entry operand whose operands all resolve to the
    /// same constant. Everything else is unknown.</summary>
    public ushort? KnownSegmentValue(int version) => KnownSegmentValue(version, new HashSet<int>());

    private ushort? KnownSegmentValue(int version, HashSet<int> visiting) {
        if (version < 0) {
            return null;
        }
        if (_knownValues.TryGetValue(version, out ushort? cached)) {
            return cached;
        }
        ushort? value = SegmentValueOf(version, visiting);
        _knownValues[version] = value;
        return value;
    }

    private ushort? SegmentValueOf(int version, HashSet<int> visiting) {
        if (!visiting.Add(version)) {
            return null;
        }
        IrSsaVersionInfo info = _ssa.Info(version);
        ushort? value = info.Kind switch {
            IrSsaDefinitionKind.Definition or IrSsaDefinitionKind.PartialDefinition
                => DefinitionValue(version, info, visiting),
            IrSsaDefinitionKind.Phi => PhiValue(version, visiting),
            _ => null
        };
        visiting.Remove(version);
        return value;
    }

    /// <summary>The constant the defining statement of <paramref name="info"/> assigns to the
    /// register: the whole value for a segment register or a 16/32-bit write, the low word for a
    /// 16-bit write of a general register (a partial definition of the 32-bit value). A copy from
    /// another register resolves through that register's version.</summary>
    private ushort? DefinitionValue(int infoVersion, IrSsaVersionInfo info, HashSet<int> visiting) {
        if (_ssa.DefiningStatement(infoVersion) is not { } statement
            || statement.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode left,
                Right: ValueNode right }
            || !CoversWord(info.Value, left)) {
            return null;
        }
        ushort? direct = ConstantWordOf(right);
        if (direct is not null) {
            return direct;
        }
        if (right is not RegisterNode source) {
            return null;
        }
        IrSsaValue sourceValue = SourceValueOf(source);
        return KnownSegmentValue(_ssa.VersionUsed(statement, sourceValue), visiting);
    }

    /// <summary>Whether an assignment to <paramref name="left"/> determines the low word of
    /// <paramref name="value"/>: a segment-register write, or a 16/32-bit write of the general
    /// register itself (not one of its bytes).</summary>
    internal static bool CoversWord(IrSsaValue value, RegisterNode left) {
        if (left is SegmentRegisterNode segment) {
            return value == (IrSsaValue)((int)IrSsaValue.Es + segment.RegisterIndex);
        }
        return value <= IrSsaValue.Edi && left.RegisterIndex == (int)value
            && left.DataType.BitWidth != BitWidth.BYTE_8;
    }

    private ushort? PhiValue(int version, HashSet<int> visiting) {
        IrSsaPhi? phi = _ssa.Phis.FirstOrDefault(candidate => candidate.Result == version);
        if (phi is null || phi.HasEntryOperand) {
            return null;
        }
        ushort? value = null;
        foreach ((IrBlock _, int operand) in phi.Operands) {
            ushort? operandValue = SegmentValueOf(operand, visiting);
            if (operandValue is null || (value is not null && operandValue != value)) {
                return null;
            }
            value = operandValue;
        }
        return value;
    }

    /// <summary>The constant a value node stands for: a constant, an instruction field whose
    /// value is used, or a conversion wrapping either - the conversion applies its own width to
    /// the constant it wraps.</summary>
    internal static ushort? ConstantWordOf(ValueNode node) => node switch {
        ConstantNode constant => (ushort)constant.Convert(DataType.UINT16),
        InstructionFieldNode { ResolvedNode: ConstantNode resolved }
            => (ushort)resolved.Convert(DataType.UINT16),
        TypeConversionNode conversion => ConvertedConstantWord(conversion),
        _ => null
    };

    private static IrSsaValue SourceValueOf(RegisterNode source) {
        if (source is SegmentRegisterNode) {
            return (IrSsaValue)((int)IrSsaValue.Es + source.RegisterIndex);
        }
        return (IrSsaValue)source.RegisterIndex;
    }

    private static ushort? ConvertedConstantWord(TypeConversionNode conversion) {
        if (ConstantWordOf(conversion.Value) is not { } inner) {
            return null;
        }
        return (ushort)new ConstantNode(conversion.Value.DataType, inner)
            .Convert(conversion.DataType);
    }
}
