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
    public static bool HasBreak(IVisitableAstNode node) {
        Stack<IVisitableAstNode> pending = new();
        pending.Push(node);
        while (pending.Count > 0) {
            switch (pending.Pop()) {
                case BreakNode:
                    return true;
                case BlockNode block:
                    foreach (IVisitableAstNode statement in block.Statements) {
                        pending.Push(statement);
                    }
                    break;
                case IfElseNode ifElse:
                    pending.Push(ifElse.TrueCase);
                    pending.Push(ifElse.FalseCase);
                    break;
                case InstructionBoundaryNode boundary:
                    pending.Push(boundary.Body);
                    break;
                case CpuFaultTryCatchNode tryCatch:
                    pending.Push(tryCatch.Body);
                    break;
            }
        }
        return false;
    }

    /// <summary>Whether <paramref name="node"/>, a loop body or a statement of one, contains a <c>continue</c> that restarts that loop.</summary>
    public static bool HasContinue(IVisitableAstNode node) {
        Stack<IVisitableAstNode> pending = new();
        pending.Push(node);
        while (pending.Count > 0) {
            switch (pending.Pop()) {
                case ContinueNode:
                    return true;
                case BlockNode block:
                    foreach (IVisitableAstNode statement in block.Statements) {
                        pending.Push(statement);
                    }
                    break;
                case IfElseNode ifElse:
                    pending.Push(ifElse.TrueCase);
                    pending.Push(ifElse.FalseCase);
                    break;
                case InstructionBoundaryNode boundary:
                    pending.Push(boundary.Body);
                    break;
                case CpuFaultTryCatchNode tryCatch:
                    pending.Push(tryCatch.Body);
                    break;
                case SwitchNode switchNode:
                    foreach (SwitchSection section in switchNode.Sections) {
                        pending.Push(section.Body);
                    }
                    pending.Push(switchNode.Default);
                    break;
            }
        }
        return false;
    }
}