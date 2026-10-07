namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

/// <summary>
/// Visitor that detects whether an AST writes the interrupt flag to a value that
/// may enable it: an assignment to <see cref="FlagRegisterNode"/>, or to the interrupt
/// <see cref="CpuFlagNode"/> with anything but the constant false.
/// Flag-register merges that keep the interrupt flag (SAHF: <c>(flags &amp; mask) | value</c>
/// with the interrupt bit set in <c>mask</c>) are not counted.
/// </summary>
internal sealed class InterruptEnableDetectorVisitor : StatementPredicateVisitor {
    private static readonly InterruptEnableDetectorVisitor Instance = new();

    /// <summary>
    /// Returns whether the given AST tree writes the interrupt flag to a value that may enable it.
    /// </summary>
    public static bool EnablesInterrupts(IVisitableAstNode node) {
        return node.Accept(Instance);
    }

    public override bool VisitBinaryOperationNode(BinaryOperationNode node) {
        if (node.BinaryOperation == BinaryOperation.ASSIGN && node.Left is FlagRegisterNode) {
            return !PreservesInterruptFlag(node.Right);
        }
        if (node.BinaryOperation == BinaryOperation.ASSIGN
            && node.Left is CpuFlagNode { FlagMask: Flags.Interrupt }
            && node.Right is not ConstantNode { Value: 0 }) {
            return true;
        }
        return false;
    }

    private static bool PreservesInterruptFlag(IVisitableAstNode right) {
        return right is BinaryOperationNode {
            BinaryOperation: BinaryOperation.BITWISE_OR,
            Left: BinaryOperationNode {
                BinaryOperation: BinaryOperation.BITWISE_AND,
                Left: FlagRegisterNode,
                Right: ConstantNode mask
            }
        } && (mask.Value & Flags.Interrupt) != 0;
    }
}
