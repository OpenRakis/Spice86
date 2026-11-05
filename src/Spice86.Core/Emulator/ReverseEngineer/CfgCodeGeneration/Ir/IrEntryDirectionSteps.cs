namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

/// <summary>
/// Folds the direction-flag conditional of every statement and terminator node whose direction
/// flag version resolves to a known value through <see cref="IrSegmentValues"/>: program-wide, the
/// value at every way in of the entry the version is rooted in. The rewrite swaps the
/// <see cref="ConditionalValueNode"/> instance for its chosen arm inside the containing node and
/// refolds the node, so the string-operation step prints its constant form; every other node
/// instance is preserved, which the analyses keyed by node identity rely on.
/// </summary>
internal static class IrEntryDirectionSteps {
    private static readonly IReadOnlyDictionary<string, ConstantNode> EmptyConstants =
        new Dictionary<string, ConstantNode>();

    /// <summary>The prepared <paramref name="body"/> with the known direction steps folded, or the
    /// same instance when no statement or terminator read a known direction flag.</summary>
    public static BlockNode Apply(IrMethod method, IrSegmentValues values, BlockNode body) {
        Dictionary<IVisitableAstNode, IVisitableAstNode> replacements =
            new(ReferenceEqualityComparer.Instance);
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                if (values.ValueAt(statement, IrSsaValue.Direction)
                        is not SegmentValue.Known direction) {
                    continue;
                }
                Replace(statement.Node, FoldSteps(statement.Node, direction.Value != 0));
            }
            if (block.HasTerminator && values.ValueAt(block.Terminator, IrSsaValue.Direction)
                    is SegmentValue.Known directionAtExit) {
                foreach (IVisitableAstNode root in IrAstChildren.RootsOf(block.Terminator)) {
                    Replace(root, FoldSteps(root, directionAtExit.Value != 0));
                }
            }
        }
        if (replacements.Count == 0) {
            return body;
        }
        return (BlockNode)Apply(body);

        void Replace(IVisitableAstNode original, IVisitableAstNode mapped) {
            IVisitableAstNode folded = IrConstantFolder.Fold(mapped, EmptyConstants);
            if (!ReferenceEquals(folded, original)) {
                replacements[original] = folded;
            }
        }

        IVisitableAstNode Apply(IVisitableAstNode node) =>
            replacements.TryGetValue(node, out IVisitableAstNode? replacement)
                ? replacement
                : IrAstChildren.MapChildren(node, Apply);
    }

    private static IVisitableAstNode FoldSteps(IVisitableAstNode node, bool set) {
        if (node is not ConditionalValueNode {
                Condition: CpuFlagNode { FlagMask: Flags.Direction } } conditional) {
            return IrAstChildren.MapChildren(node, child => FoldSteps(child, set));
        }
        return set ? conditional.WhenTrue : conditional.WhenFalse;
    }
}
