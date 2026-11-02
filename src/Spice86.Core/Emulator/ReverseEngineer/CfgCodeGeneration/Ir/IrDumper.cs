namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using System.Linq;
using System.Text;

/// <summary>
/// A text dump of an <see cref="IrMethod"/> for review and test diagnostics: one header per block, one line
/// per statement with its origin address and an effects summary, one line per terminator. The format is not
/// a contract.
/// </summary>
internal static class IrDumper {
    /// <summary>Dumps <paramref name="method"/>.</summary>
    public static string Dump(IrMethod method, CfgGeneratorContext context) {
        CSharpExpressionPrinter expressions = new(context.SegmentVariables);
        Dictionary<IrBlock, ICfgNode> entryNodeByBlock = method.Entries
            .GroupBy(entry => entry.Block)
            .ToDictionary(group => group.Key, group => group.First().Node);
        StringBuilder builder = new();
        builder.AppendLine($"method {method.Name}");
        foreach (IrBlock block in method.Blocks) {
            builder.Append("  block ").Append(block.Id);
            if (entryNodeByBlock.TryGetValue(block, out ICfgNode? entryNode)) {
                builder.Append(" (entry ").Append(entryNode.Address).Append(')');
            }
            builder.Append(" starts ").Append(block.EntryOrigin.Address);
            if (block.StartsInstruction) {
                builder.Append(" [instruction]");
            }
            builder.AppendLine();
            foreach (IrStatement statement in block.Statements) {
                builder.Append("    ").Append(statement.Origin.Address)
                    .Append(' ').Append(EffectsSummary(statement.Effects))
                    .Append(' ').AppendLine(Print(statement.Node, expressions));
            }
            if (block.HasTerminator) {
                builder.Append("    ").AppendLine(Dump(block.Terminator, expressions));
            } else {
                builder.AppendLine("    <no terminator>");
            }
        }
        return builder.ToString();
    }

    private static string Dump(IrTerminator terminator, CSharpExpressionPrinter expressions) =>
        terminator switch {
            IrJump jump => $"jump b{jump.Edge.Target.Id}{Inside(jump.Edge)}",
            IrBranch branch => $"branch {branch.Condition.Accept(expressions).Text} ? "
                + $"b{branch.WhenTrue.Target.Id}{Inside(branch.WhenTrue)}"
                + $" : b{branch.WhenFalse.Target.Id}{Inside(branch.WhenFalse)}",
            IrSwitch switchNode => $"switch {switchNode.Value.Accept(expressions).Text} "
                + $"[{string.Join(", ", switchNode.Cases.Select(c => $"{c.Label}:b{c.Target.Target.Id}{Inside(c.Target)}"))}] "
                + $"default b{switchNode.Default.Target.Id}{Inside(switchNode.Default)}",
            IrExit exit => $"exit {Print(exit.Transfer, expressions)}",
            _ => $"terminator {terminator.GetType().Name}"
        };

    private static string Inside(IrEdge edge) => edge.InsideInstruction ? " [inside]" : string.Empty;

    private static string Print(IVisitableAstNode node, CSharpExpressionPrinter expressions) {
        CSharpSourceWriter writer = new();
        new CSharpStatementPrinter(writer, expressions, target => target switch {
            CfgNodeGotoTarget cfgTarget => cfgTarget.Node.Address.ToString(),
            IrBlock block => $"b{block.Id}",
            _ => target.ToString() ?? "target"
        }).Print(node);
        return writer.ToString().Trim();
    }

    private static string EffectsSummary(IrEffects effects) {
        StringBuilder builder = new();
        builder.Append('R').Append(':').Append(Values(effects.Reads));
        builder.Append(' ').Append('W').Append(':').Append(Values(effects.MustWrite));
        if (effects.MayWrite != effects.MustWrite) {
            builder.Append(' ').Append('M').Append(':').Append(Values(effects.MayWrite));
        }
        if (effects.TokenReads != IrTokens.None || effects.TokenWrites != IrTokens.None) {
            builder.Append(' ').Append('T').Append(':').Append(Tokens(effects.TokenReads))
                .Append('/').Append(Tokens(effects.TokenWrites));
        }
        if (effects.MayFault) {
            builder.Append(' ').Append('F');
        }
        if (effects.DeliversInterrupts) {
            builder.Append(' ').Append('I');
        }
        return builder.ToString();
    }

    private static string Tokens(IrTokens tokens) {
        StringBuilder builder = new();
        if (tokens.HasFlag(IrTokens.Memory)) {
            builder.Append('M');
        }
        if (tokens.HasFlag(IrTokens.Io)) {
            builder.Append('I');
        }
        return builder.Length == 0 ? "-" : builder.ToString();
    }

    private static string Values(IrValues values) {
        if (values == IrValues.None) {
            return "-";
        }
        StringBuilder builder = new((values & IrValues.Registers).ToString("X"));
        if (values.HasFlag(IrValues.Carry)) {
            builder.Append(",CF");
        }
        if (values.HasFlag(IrValues.Parity)) {
            builder.Append(",PF");
        }
        if (values.HasFlag(IrValues.Auxiliary)) {
            builder.Append(",AF");
        }
        if (values.HasFlag(IrValues.Zero)) {
            builder.Append(",ZF");
        }
        if (values.HasFlag(IrValues.Sign)) {
            builder.Append(",SF");
        }
        if (values.HasFlag(IrValues.Overflow)) {
            builder.Append(",OF");
        }
        if (values.HasFlag(IrValues.Direction)) {
            builder.Append(",DF");
        }
        return builder.ToString();
    }
}
