namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using System.Linq;

/// <summary>
/// Records in <see cref="ValueNode.BranchOrigin"/> which instruction decides each branch condition and switch
/// value, for the branch probes of a check build. The origin is written on the node in place, never on a
/// copy, because the register-local and parameter tables key nodes by instance.
/// <list type="bullet">
/// <item>The condition of an <see cref="IrBranch"/> and the value of an <see cref="IrSwitch"/> get the
/// instruction the terminator was lifted from, once every IR pass has run (<see cref="StampTerminators"/>).
/// The passes rebuild conditions often, and the terminator origin is the one fact they all keep.</item>
/// <item>A pass that merges the tests of several instructions into one condition stamps the parts itself:
/// each operand of an <c>&amp;&amp;</c> or <c>||</c> gets its own instruction, and a single comparison folded
/// from several jumps is synthetic.</item>
/// <item>A rewrite that builds a new condition from an old one (a negation, a single-case switch turned into
/// an <c>if</c>) carries the old origin with <see cref="Carry"/>.</item>
/// </list>
/// A condition that reaches the printer without an origin, or with <see cref="BranchOrigin.Synthetic"/>, is a
/// synthetic site in the manifest.
/// </summary>
internal static class IrBranchOrigins {
    /// <summary>Records <paramref name="instruction"/> as the origin of <paramref name="condition"/>. A node
    /// that already has another origin is shared by the conditions of two instructions, so no single
    /// instruction decides it and it becomes synthetic.</summary>
    public static void Stamp(ValueNode condition, ICfgNode instruction) {
        if (condition.BranchOrigin is null) {
            condition.BranchOrigin = new BranchOrigin(instruction.Address);
        } else if (condition.BranchOrigin.Address != instruction.Address) {
            condition.BranchOrigin = BranchOrigin.Synthetic;
        }
    }

    /// <summary>Marks <paramref name="condition"/> as decided by no single instruction.</summary>
    public static void MarkSynthetic(ValueNode condition) {
        condition.BranchOrigin = BranchOrigin.Synthetic;
    }

    /// <summary>Gives <paramref name="derived"/>, a condition built from <paramref name="source"/>, the origin
    /// of <paramref name="source"/> when it has none of its own.</summary>
    /// <returns><paramref name="derived"/>.</returns>
    public static ValueNode Carry(ValueNode derived, ValueNode source) {
        derived.BranchOrigin ??= source.BranchOrigin;
        return derived;
    }

    /// <summary>Stamps every condition and switch value the terminators of <paramref name="method"/> hold with
    /// the terminator's instruction: the branch condition, the switch value, and the conditions inside the
    /// transfer of an exit.</summary>
    public static void StampTerminators(IrMethod method) {
        foreach (IrTerminator terminator in method.Blocks.Select(block => block.Terminator)) {
            switch (terminator) {
                case IrBranch branch:
                    Stamp(branch.Condition, branch.Origin);
                    break;
                case IrSwitch @switch:
                    Stamp(@switch.Value, @switch.Origin);
                    break;
                case IrExit exit:
                    StampNested(exit.Transfer, exit.Origin);
                    break;
            }
        }
    }

    /// <summary>Stamps the condition of every conditional value inside the statements of
    /// <paramref name="method"/> with the statement's instruction. Run right after the lift, while each
    /// statement still holds only what its own instruction computes.</summary>
    public static void StampConditionalValues(IrMethod method) {
        foreach (IrStatement statement in method.Blocks.SelectMany(block => block.Statements)) {
            StampNested(statement.Node, statement.Origin);
        }
    }

    private static void StampNested(IVisitableAstNode root, ICfgNode instruction) {
        foreach (IVisitableAstNode node in IrAstChildren.Descendants(root)) {
            switch (node) {
                case ConditionalValueNode conditional:
                    Stamp(conditional.Condition, instruction);
                    break;
                case IfElseNode ifElse:
                    Stamp(ifElse.Condition, instruction);
                    break;
                case WhileNode whileNode:
                    Stamp(whileNode.Condition, instruction);
                    break;
                case SwitchNode switchNode:
                    Stamp(switchNode.Value, instruction);
                    break;
            }
        }
    }
}
