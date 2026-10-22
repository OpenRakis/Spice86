namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
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
            case CommentNode comment:
                writer.Line($"// {comment.Text}");
                return;
            case CheckExternalEventsNode checkExternalEvents:
                PrintCheckExternalEvents(checkExternalEvents);
                return;
            case SpeculativeGuardNode speculativeGuard:
                PrintSpeculativeGuard(speculativeGuard);
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

    private void PrintIfElse(IfElseNode node) {
        writer.OpenBlock($"if ({expressions.Print(node.Condition)})");
        Print(node.TrueCase);
        writer.CloseBlock();
        if (IsEmptyBlock(node.FalseCase)) {
            return;
        }
        writer.OpenBlock("else");
        Print(node.FalseCase);
        writer.CloseBlock();
    }

    private static bool IsEmptyBlock(IVisitableAstNode node) => node is BlockNode { Statements.Count: 0 };

    private void PrintWhile(WhileNode node) {
        writer.OpenBlock($"while ({expressions.Print(node.Condition)})");
        Print(node.Body);
        writer.CloseBlock();
    }

    private void PrintThrow(ThrowNode node) =>
        writer.Line($"throw new {node.ExceptionType.FullName}(\"{CSharpExpressionPrinter.EscapeString(node.Message)}\");");

    private void PrintSwitch(SwitchNode node) {
        writer.OpenBlock($"switch ({expressions.Print(node.Value)})");
        foreach (SwitchSection section in node.Sections) {
            writer.Line($"case 0x{section.Label:X4}:");
            writer.Indent();
            Print(section.Body);
            writer.Dedent();
        }
        writer.Line("default:");
        writer.Indent();
        Print(node.Default);
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
        Print(node.Body);
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
}
