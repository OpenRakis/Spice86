namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

/// <summary>Shared dispatch rule of the code-generation-only nodes.</summary>
internal static class CodeGenerationNode {
    /// <summary>Returns <paramref name="visitor"/> as a code-generation visitor, or throws when it cannot visit <paramref name="node"/>.</summary>
    public static ICodeGenerationAstVisitor<T> RequireVisitor<T>(IAstVisitor<T> visitor, IVisitableAstNode node) =>
        visitor as ICodeGenerationAstVisitor<T>
        ?? throw new NotSupportedException($"{node.GetType().Name} is a code-generation node; only an ICodeGenerationAstVisitor can visit it.");
}
