namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// The one predicate deciding whether a <see cref="SegmentedPointerNode"/> is a global: its segment
/// is a segment register other than SS, its data type is an unsigned byte, word or dword, and its
/// offset resolves to a constant. The constant memory access collector and the printer share it, so
/// every printed global has its property and every collected access prints as one. Also owns the
/// names the generated globals classes and their properties take.
/// </summary>
internal static class IrGlobalAccesses {
    /// <summary>The segment registers a globals class is emitted for, in file order.</summary>
    public static readonly SegmentRegisterIndex[] RegistersInClassOrder = [
        SegmentRegisterIndex.DsIndex, SegmentRegisterIndex.EsIndex, SegmentRegisterIndex.CsIndex,
        SegmentRegisterIndex.FsIndex, SegmentRegisterIndex.GsIndex
    ];

    /// <summary>Whether <paramref name="pointer"/> is a global access; gives the
    /// <see cref="ConstantMemoryAccess"/> it records.</summary>
    public static bool TryAccess(SegmentedPointerNode pointer, out ConstantMemoryAccess access) {
        access = default;
        if (pointer.Segment is not SegmentRegisterNode segment
            || segment.RegisterIndex == (int)SegmentRegisterIndex.SsIndex
            || pointer.DataType.Signed
            || pointer.DataType.BitWidth is not (BitWidth.BYTE_8 or BitWidth.WORD_16
                or BitWidth.DWORD_32)
            || !TryConstantOffset(pointer.Offset, out ushort offset)) {
            return false;
        }
        access = new ConstantMemoryAccess((SegmentRegisterIndex)segment.RegisterIndex, offset,
            (int)pointer.DataType.BitWidth / 8);
        return true;
    }

    /// <summary>The generated globals class name for <paramref name="segment"/>:
    /// <c>DsGlobals</c>, <c>EsGlobals</c>, ...</summary>
    public static string ClassName(SegmentRegisterIndex segment) => segment switch {
        SegmentRegisterIndex.DsIndex => "DsGlobals",
        SegmentRegisterIndex.EsIndex => "EsGlobals",
        SegmentRegisterIndex.CsIndex => "CsGlobals",
        SegmentRegisterIndex.FsIndex => "FsGlobals",
        SegmentRegisterIndex.GsIndex => "GsGlobals",
        _ => throw new NotSupportedException($"No globals class for segment register {segment}.")
    };

    /// <summary>The generated property name of <paramref name="access"/>:
    /// <c>Byte0004</c>, <c>Word0004</c>, <c>Dword0004</c>.</summary>
    public static string PropertyName(ConstantMemoryAccess access) =>
        PropertyName(access.Offset, access.Width);

    /// <summary>The generated property name of an <paramref name="offset"/>/<paramref name="width"/>
    /// access: <c>Byte0004</c>, <c>Word0004</c>, <c>Dword0004</c>.</summary>
    public static string PropertyName(ushort offset, int width) => width switch {
        1 => $"Byte{offset:X4}",
        2 => $"Word{offset:X4}",
        4 => $"Dword{offset:X4}",
        _ => throw new NotSupportedException($"No global property for a {width}-byte access.")
    };

    /// <summary>The generated table class name for <paramref name="segment"/>:
    /// <c>DsTables</c>, <c>EsTables</c>, ...</summary>
    public static string TableClassName(SegmentRegisterIndex segment) =>
        $"{ClassName(segment)[..^7]}Tables";

    /// <summary>Whether <paramref name="pointer"/> is a table access: the same segment and element
    /// type rules as <see cref="TryAccess"/>, with an offset of the shape <c>R + c</c> for a byte
    /// element or <c>(R * width) + c</c> - a shift, a product or <c>R + R</c> - for a word or
    /// dword element; <paramref name="access"/> gives the 16-bit general index register and the
    /// constant base. The range of the index is not part of the shape test.</summary>
    public static bool TryTableAccess(SegmentedPointerNode pointer,
        out (RegisterNode Index, ushort Base) access) {
        access = default;
        int width = pointer.DataType.BitWidth switch {
            BitWidth.BYTE_8 => 1,
            BitWidth.WORD_16 => 2,
            BitWidth.DWORD_32 => 4,
            _ => 0
        };
        if (pointer.Segment is not SegmentRegisterNode segment
            || segment.RegisterIndex == (int)SegmentRegisterIndex.SsIndex
            || pointer.DataType.Signed
            || width == 0
            || StripCasts(pointer.Offset) is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.PLUS, Left: ValueNode left, Right: ValueNode right }) {
            return false;
        }
        if (ConstantValue(left) is { } leftConstant
            && IndexOf(right, width) is { } leftIndex) {
            access = (leftIndex, leftConstant);
            return true;
        }
        if (ConstantValue(right) is { } rightConstant
            && IndexOf(left, width) is { } rightIndex) {
            access = (rightIndex, rightConstant);
            return true;
        }
        return false;
    }

    /// <summary>The index half of a table offset: the register itself for a byte table, the
    /// register scaled by the element width for a word or dword table.</summary>
    private static RegisterNode? IndexOf(ValueNode node, int width) {
        node = StripCasts(node);
        if (width == 1) {
            return WordRegister(node);
        }
        switch (node) {
            case BinaryOperationNode {
                BinaryOperation: BinaryOperation.LEFT_SHIFT, Left: RegisterNode shifted,
                Right: IVisitableAstNode count }
                when ConstantValue(count) is { } shift && shift < 16 && 1 << shift == width:
                return WordRegister(shifted);
            case BinaryOperationNode {
                BinaryOperation: BinaryOperation.MULTIPLY, Left: ValueNode first,
                Right: ValueNode second }:
                if (ConstantValue(first) == width && second is RegisterNode secondRegister) {
                    return WordRegister(secondRegister);
                }
                if (ConstantValue(second) == width && first is RegisterNode firstRegister) {
                    return WordRegister(firstRegister);
                }
                return null;
            case BinaryOperationNode {
                BinaryOperation: BinaryOperation.PLUS, Left: RegisterNode first,
                Right: RegisterNode second }
                when width == 2 && first.Equals(second):
                return WordRegister(first);
            default:
                return null;
        }
    }

    /// <summary><paramref name="node"/> when it is a 16-bit general register.</summary>
    private static RegisterNode? WordRegister(IVisitableAstNode node) {
        if (node is RegisterNode { RegisterIndex: <= 7 } register
            && register is not SegmentRegisterNode
            && register.DataType.BitWidth == BitWidth.WORD_16) {
            return register;
        }
        return null;
    }

    /// <summary>The 16-bit constant a node evaluates to, through instruction fields and
    /// conversions; <see langword="null"/> when a part of it is not compile-time constant or the
    /// value does not fit in 16 bits.</summary>
    private static ushort? ConstantValue(IVisitableAstNode node) {
        if (node is ValueNode value && TryConstantOffset(value, out ushort constant)) {
            return constant;
        }
        return null;
    }

    /// <summary>The offset of a global access: the constant the node evaluates to with its
    /// conversions intact, only when it fits in 16 bits - a wider offset must keep the raw
    /// segmented-indexer path whose MMU checks the truncation would silently skip.</summary>
    private static bool TryConstantOffset(ValueNode offset, out ushort constant) {
        if (ConstantEvaluation.TryEvaluate(offset, _ => null, out ulong evaluated)
            && evaluated <= ushort.MaxValue) {
            constant = (ushort)evaluated;
            return true;
        }
        constant = 0;
        return false;
    }

    private static ValueNode StripCasts(ValueNode node) => node switch {
        TypeConversionNode conversion => StripCasts(conversion.Value),
        ExplicitCastNode { Value: ValueNode castValue } => StripCasts(castValue),
        _ => node
    };
}
