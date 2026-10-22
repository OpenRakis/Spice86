namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using System.Collections.Generic;

/// <summary>Builds and inspects lowered code: statement sequences, the empty sequence and switch sections.</summary>
internal static class CodeGenerationAst {
    /// <summary>A new empty statement sequence.</summary>
    public static BlockNode Empty() => new();

    /// <summary>The statements of <paramref name="parts"/> in order; a <see cref="BlockNode"/> part contributes its statements.</summary>
    public static BlockNode Sequence(params IVisitableAstNode[] parts) => Sequence((IEnumerable<IVisitableAstNode>)parts);

    /// <inheritdoc cref="Sequence(IVisitableAstNode[])"/>
    public static BlockNode Sequence(IEnumerable<IVisitableAstNode> parts) {
        List<IVisitableAstNode> statements = [];
        foreach (IVisitableAstNode part in parts) {
            if (part is BlockNode block) {
                statements.AddRange(block.Statements);
            } else {
                statements.Add(part);
            }
        }
        return new BlockNode(statements.ToArray());
    }

    /// <summary>True when <paramref name="node"/> is a <see cref="BlockNode"/> without statements.</summary>
    public static bool IsEmpty(IVisitableAstNode node) => node is BlockNode { Statements.Count: 0 };

    /// <summary>A switch section whose body ends with <c>break;</c> exactly when it would otherwise complete normally.</summary>
    public static SwitchSection Section(int label, IVisitableAstNode body) {
        BlockNode statements = Sequence(body);
        if (!CompletesNormallyVisitor.CompletesNormally(statements)) {
            return new SwitchSection(label, statements);
        }
        return new SwitchSection(label, Sequence(statements, new BreakNode()));
    }
}
