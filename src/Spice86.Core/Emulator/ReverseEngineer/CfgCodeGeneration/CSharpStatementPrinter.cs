namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Writes a tree of runtime and code-generation AST nodes as C# statements. Expressions are printed by
/// <see cref="CSharpExpressionPrinter"/>; labels come from <paramref name="labelOf"/>.
/// </summary>
internal sealed class CSharpStatementPrinter(CSharpSourceWriter writer, CSharpExpressionPrinter expressions, Func<ICfgNode, string> labelOf) {
    /// <summary>Writes <paramref name="node"/> in statement position.</summary>
    public void Print(IVisitableAstNode node) {
        switch (node) {
            case BlockNode block:
                PrintBlock(block);
                return;
            case IfElseNode ifElse:
                PrintIfElse(ifElse);
                return;
            case WhileNode whileNode:
                PrintWhile(whileNode);
                return;
            case DoWhileNode doWhile:
                PrintDoWhile(doWhile);
                return;
            case InfiniteLoopNode loop:
                PrintInfiniteLoop(loop);
                return;
            case ThrowNode throwNode:
                PrintThrow(throwNode);
                return;
            case SwitchNode switchNode:
                PrintSwitch(switchNode);
                return;
            case CyclicJumpNode cyclicJump:
                PrintCyclicJump(cyclicJump);
                return;
            case CpuFaultTryCatchNode cpuFault:
                PrintCpuFaultTryCatch(cpuFault);
                return;
            case FailAsUntestedNode failAsUntested:
                PrintFailAsUntested(failAsUntested);
                return;
            case InstructionBoundaryNode boundary:
                PrintInstructionBoundary(boundary);
                return;
            case DivisionNode division:
                PrintDivision(division);
                return;
            case GotoNode gotoNode:
                writer.Line($"goto {labelOf(gotoNode.Target)};");
                return;
            case LabelNode label:
                writer.Label(labelOf(label.Target));
                return;
            case EntryDispatcherLabelNode:
                writer.Label("entrydispatcher");
                return;
            case ReturnNode returnNode:
                writer.Line($"return {expressions.Print(returnNode.Value)};");
                return;
            case BreakNode:
                writer.Line("break;");
                return;
            case ContinueNode:
                writer.Line("continue;");
                return;
            case CommentNode comment:
                writer.Line($"// {comment.Text}");
                return;
            case CheckExternalEventsNode checkExternalEvents:
                PrintCheckExternalEvents(checkExternalEvents);
                return;
            case SpeculativeGuardNode speculativeGuard:
                PrintSpeculativeGuard(speculativeGuard);
                return;
            case BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode register } assignment
                when IncrementOrDecrement(register, assignment.Right) is string step:
                writer.Line($"{expressions.Print(register).Text}{step};");
                return;
            default:
                writer.Line(expressions.Print(node).Text + ";");
                return;
        }
    }

    private void PrintBlock(BlockNode node) {
        foreach (IVisitableAstNode statement in node.Statements) {
            Print(statement);
        }
    }

    /// <summary>
    /// Writes the body of a braced construct. C# needs a statement after a label, so a body whose last label is followed
    /// only by comments gets an empty statement.
    /// </summary>
    private void PrintBody(IVisitableAstNode body) {
        Print(body);
        if (TrailingLabel(body) == true) {
            writer.Line(";");
        }
    }

    /// <summary>
    /// <c>true</c> when the last thing <paramref name="node"/> prints is a label, <c>false</c> when it is a statement,
    /// <c>null</c> when it prints only comments or nothing.
    /// </summary>
    private static bool? TrailingLabel(IVisitableAstNode node) {
        switch (node) {
            case LabelNode:
                return true;
            case CommentNode:
                return null;
            case BlockNode block:
                for (int index = block.Statements.Count - 1; index >= 0; index--) {
                    if (TrailingLabel(block.Statements[index]) is bool result) {
                        return result;
                    }
                }
                return null;
            case InstructionBoundaryNode boundary:
                return TrailingLabel(boundary.Body);
            default:
                return false;
        }
    }

    private void PrintIfElse(IfElseNode node) {
        writer.OpenBlock($"if ({expressions.Print(node.Condition)})");
        PrintBody(node.TrueCase);
        writer.CloseBlock();
        IVisitableAstNode falseCase = node.FalseCase;
        while (OnlyIfElse(falseCase) is IfElseNode elseIf) {
            writer.OpenBlock($"else if ({expressions.Print(elseIf.Condition)})");
            PrintBody(elseIf.TrueCase);
            writer.CloseBlock();
            falseCase = elseIf.FalseCase;
        }
        if (IsEmptyBlock(falseCase)) {
            return;
        }
        writer.OpenBlock("else");
        PrintBody(falseCase);
        writer.CloseBlock();
    }

    /// <summary>The <c>if</c> that is the only statement of <paramref name="node"/>, or <c>null</c>.</summary>
    private static IfElseNode? OnlyIfElse(IVisitableAstNode node) => node switch {
        IfElseNode ifElse => ifElse,
        BlockNode { Statements: [IfElseNode ifElse] } => ifElse,
        _ => null
    };

    private static bool IsEmptyBlock(IVisitableAstNode node) => node is BlockNode { Statements.Count: 0 };

    private void PrintWhile(WhileNode node) {
        writer.OpenBlock($"while ({expressions.Print(node.Condition)})");
        PrintBody(node.Body);
        writer.CloseBlock();
    }

    private void PrintDoWhile(DoWhileNode node) {
        writer.OpenBlock("do");
        PrintBody(node.Body);
        writer.CloseBlock($" while ({expressions.Print(node.Condition)});");
    }

    private void PrintInfiniteLoop(InfiniteLoopNode node) {
        writer.OpenBlock("while (true)");
        PrintBody(node.Body);
        writer.CloseBlock();
    }

    private void PrintThrow(ThrowNode node) =>
        writer.Line($"throw new {node.ExceptionType.FullName}(\"{CSharpExpressionPrinter.EscapeString(node.Message)}\");");

    private void PrintSwitch(SwitchNode node) {
        writer.OpenBlock($"switch ({expressions.Print(node.Value)})");
        foreach (SwitchSection section in node.Sections) {
            writer.Line($"case 0x{section.Label:X4}:");
            writer.Indent();
            PrintBody(section.Body);
            writer.Dedent();
        }
        writer.Line("default:");
        writer.Indent();
        PrintBody(node.Default);
        writer.Dedent();
        writer.CloseBlock();
    }

    private void PrintCyclicJump(CyclicJumpNode node) {
        writer.OpenBlock($"if (JumpDispatcher.Jump({node.MethodName}, 0x{node.LoadOffset:X4}))");
        writer.Line("loadOffset = JumpDispatcher.NextEntryAddress;");
        writer.Line("goto entrydispatcher;");
        writer.CloseBlock();
        writer.Line("return JumpDispatcher.RequiredJumpAsmReturn;");
    }

    private void PrintCpuFaultTryCatch(CpuFaultTryCatchNode node) {
        writer.OpenBlock("try");
        PrintBody(node.Body);
        writer.CloseBlock();
        writer.OpenBlock("catch (CpuException cpuException)");
        string resumeSegment = expressions.SegmentVariable(node.FaultingInstruction.Segment);
        writer.Line($"return DispatchCpuFault(cpuException, {resumeSegment}, 0x{node.FaultingInstruction.Offset:X4}, [{FaultHandlers(node.Handlers)}]);");
        writer.CloseBlock();
    }

    private string FaultHandlers(IReadOnlyList<CpuFaultHandler> handlers) =>
        string.Join(", ", handlers.Select(FaultHandler));

    private string FaultHandler(CpuFaultHandler handler) {
        string handlerSegment = expressions.SegmentVariable(handler.Handler.Segment);
        return $"new CpuFaultTarget(new SegmentedAddress({handlerSegment}, 0x{handler.Handler.Offset:X4}), () => {handler.MethodName}(0x{handler.LoadOffset:X4}))";
    }

    private void PrintFailAsUntested(FailAsUntestedNode node) {
        bool interpolated = node.Parts.Any(part => part.Value is not null);
        string message = interpolated ? InterpolatedMessage(node.Parts) : PlainMessage(node.Parts);
        writer.Line($"throw FailAsUntested({message});");
    }

    private static string PlainMessage(IReadOnlyList<UntestedMessagePart> parts) {
        string text = string.Concat(parts.Select(part => CSharpExpressionPrinter.EscapeString(part.Text)));
        return $"\"{text}\"";
    }

    private string InterpolatedMessage(IReadOnlyList<UntestedMessagePart> parts) {
        string text = string.Concat(parts.Select(InterpolatedPart));
        return $"$\"{text}\"";
    }

    private string InterpolatedPart(UntestedMessagePart part) {
        if (part.Value is null) {
            return EscapeInterpolatedLiteral(part.Text);
        }
        return Hole(part, part.Value);
    }

    private static string EscapeInterpolatedLiteral(string text) =>
        CSharpExpressionPrinter.EscapeString(text).Replace("{", "{{").Replace("}", "}}");

    private string Hole(UntestedMessagePart part, ValueNode value) {
        string expression = expressions.Print(value).Text;
        if (expression.Contains(':') || expression.Contains('?')) {
            expression = $"({expression})";
        }
        string format = part.Format.Length == 0 ? string.Empty : $":{part.Format}";
        return $"{{{expression}{format}}}";
    }

    private void PrintInstructionBoundary(InstructionBoundaryNode node) {
        string savedSuffix = expressions.LocalVariableSuffix;
        expressions.LocalVariableSuffix = node.LocalVariableSuffix;
        Print(node.Body);
        expressions.LocalVariableSuffix = savedSuffix;
    }

    private void PrintDivision(DivisionNode node) {
        DivisionPattern division = node.Division;
        string prefix = division.Signed ? "IDiv" : "Div";
        string helperName = $"{prefix}{(int)division.Width}";
        DataType divisorType = DataType.FromBitWidth(division.Width, division.Signed);
        DataType dividendType = DataType.FromBitWidth(division.Width.Double(), division.Signed);
        string divisor = CSharpExpressionPrinter.Cast(divisorType, expressions.Print(division.Divisor));
        string dividend = CSharpExpressionPrinter.Cast(dividendType, expressions.Print(division.Dividend));
        string low = expressions.Print(division.Low);
        string high = expressions.Print(division.High);
        writer.Line($"({low}, {high}) = {helperName}(divisor: {divisor}, dividend: {dividend});");
    }

    private void PrintCheckExternalEvents(CheckExternalEventsNode node) {
        string resumeSegment = expressions.SegmentVariable(node.ResumeAddress.Segment);
        writer.Line($"CheckExternalEvents({resumeSegment}, 0x{node.ResumeAddress.Offset:X4});");
    }

    private void PrintSpeculativeGuard(SpeculativeGuardNode node) {
        string segment = expressions.SegmentVariable(node.Address.Segment);
        writer.Line($"VerifySpeculativeEntryOrFail({segment}, 0x{node.Address.Offset:X4}, {node.SignatureField});");
    }

    /// <summary>
    /// <c>++</c> or <c>--</c> when <paramref name="value"/> is <paramref name="register"/> plus or minus one in the
    /// register's own type, otherwise <c>null</c>.
    /// </summary>
    private static string? IncrementOrDecrement(RegisterNode register, ValueNode value) {
        if (value is not BinaryOperationNode { BinaryOperation: BinaryOperation.PLUS or BinaryOperation.MINUS } arithmetic
            || arithmetic.Left != register || arithmetic.DataType != register.DataType || !IsOne(arithmetic.Right)) {
            return null;
        }
        return arithmetic.BinaryOperation == BinaryOperation.PLUS ? "++" : "--";
    }

    private static bool IsOne(ValueNode node) =>
        node is ConstantNode { Value: 1 } or InstructionFieldNode { ResolvedNode: ConstantNode { Value: 1 } };
}
