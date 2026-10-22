namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

using System.Linq;

/// <summary>
/// A <c>repe</c> / <c>repne</c> loop whose string operation only compares two operands with an ALU subtraction,
/// so that, when no flag is read after the instruction, the comparison can be written as an equality.
/// </summary>
/// <param name="CounterCondition">The condition tested before each iteration (<c>CX != 0</c>).</param>
/// <param name="Left">The first compared operand, read first.</param>
/// <param name="Right">The second compared operand.</param>
/// <param name="Updates">The statements after the comparison: pointer updates, then the counter decrement.</param>
/// <param name="StopWhenEqual">True for <c>repne</c> (stop on a match), false for <c>repe</c> (stop on a difference).</param>
internal sealed record RepCompareLoopPattern(ValueNode CounterCondition, ValueNode Left, ValueNode Right,
    IReadOnlyList<IVisitableAstNode> Updates, bool StopWhenEqual) {
    /// <summary>The comparison loop of the instruction AST <paramref name="node"/>, or <c>null</c> when it is not one.</summary>
    public static RepCompareLoopPattern? TryMatch(IVisitableAstNode node) {
        if (node is not BlockNode { Statements: [WhileWithExitNode loop, MoveIpNextNode] }) {
            return null;
        }
        if (loop.Body.Statements is not [BlockNode core, IVisitableAstNode decrementCounter]
            || core.Statements is not [MethodCallValueNode {
                CallNode: {
                    PropertyPath: "Alu8" or "Alu16" or "Alu32",
                    MethodName: "Sub",
                    Arguments: [ValueNode left, ValueNode right]
                }
            }, ..]) {
            return null;
        }
        if (loop.ExitCondition is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.EQUAL,
                Left: CpuFlagNode { FlagMask: Flags.Zero },
                Right: ConstantNode exitZeroFlag
            }) {
            return null;
        }
        List<IVisitableAstNode> updates = [.. core.Statements.Skip(1), decrementCounter];
        return new RepCompareLoopPattern(loop.Condition, left, right, updates, exitZeroFlag.Value != 0);
    }
}
