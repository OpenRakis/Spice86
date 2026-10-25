namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using System.Linq;

/// <summary>
/// Finds the <c>break</c> and <c>continue</c> statements of a loop body that target that loop: the ones not nested in
/// an inner construct that captures them. A <c>switch</c> captures <c>break</c> but not <c>continue</c>; every loop
/// captures both.
/// </summary>
internal static class LoopExits {
    /// <summary>Whether <paramref name="node"/>, a loop body or a statement of one, contains a <c>break</c> that leaves that loop.</summary>
    public static bool HasBreak(IVisitableAstNode node) => node switch {
        BreakNode => true,
        BlockNode block => block.Statements.Any(HasBreak),
        IfElseNode ifElse => HasBreak(ifElse.TrueCase) || HasBreak(ifElse.FalseCase),
        InstructionBoundaryNode boundary => HasBreak(boundary.Body),
        CpuFaultTryCatchNode tryCatch => HasBreak(tryCatch.Body),
        _ => false
    };

    /// <summary>Whether <paramref name="node"/>, a loop body or a statement of one, contains a <c>continue</c> that restarts that loop.</summary>
    public static bool HasContinue(IVisitableAstNode node) => node switch {
        ContinueNode => true,
        BlockNode block => block.Statements.Any(HasContinue),
        IfElseNode ifElse => HasContinue(ifElse.TrueCase) || HasContinue(ifElse.FalseCase),
        InstructionBoundaryNode boundary => HasContinue(boundary.Body),
        CpuFaultTryCatchNode tryCatch => HasContinue(tryCatch.Body),
        SwitchNode switchNode => switchNode.Sections.Any(section => HasContinue(section.Body)) || HasContinue(switchNode.Default),
        _ => false
    };
}