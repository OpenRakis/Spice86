namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Writes a tree of runtime and code-generation AST nodes as C# statements. Expressions are printed by
/// <see cref="CSharpExpressionPrinter"/>; labels come from <paramref name="labelOf"/>.
/// </summary>
internal sealed class CSharpStatementPrinter(CSharpSourceWriter writer, CSharpExpressionPrinter expressions, Func<IGotoTarget, string> labelOf) {
    /// <summary>Writes <paramref name="node"/> in statement position. The statement walk runs on an explicit
    /// stack — nodes to print interleaved with <see cref="Action"/> writer operations — so arbitrarily deep
    /// <c>if</c> / <c>else</c> chains cannot overflow the thread's stack.</summary>
    public void Print(IVisitableAstNode node) {
        Stack<object> pending = new();
        Dispatch(node, pending);
        while (pending.Count > 0) {
            switch (pending.Pop()) {
                case Action action:
                    action();
                    break;
                case IVisitableAstNode next:
                    Dispatch(next, pending);
                    break;
            }
        }
    }

    private void Dispatch(IVisitableAstNode node, Stack<object> pending) {
        switch (node) {
            case BlockNode block:
                for (int index = block.Statements.Count - 1; index >= 0; index--) {
                    pending.Push(block.Statements[index]);
                }
                return;
            case IfElseNode ifElse:
                PushIfElse(pending, ifElse);
                return;
            case WhileNode whileNode:
                PushBraced(pending, $"while ({expressions.Print(whileNode.Condition)})", whileNode.Body);
                return;
            case DoWhileNode doWhile:
                PushDoWhile(pending, doWhile);
                return;
            case PreTestLoopNode preTestLoop:
                PushBraced(pending, $"while ({expressions.Print(preTestLoop.Condition)})", preTestLoop.Body);
                return;
            case InfiniteLoopNode loop:
                PushBraced(pending, "while (true)", loop.Body);
                return;
            case ThrowNode throwNode:
                PrintThrow(throwNode);
                return;
            case SwitchNode switchNode:
                PushSwitch(pending, switchNode);
                return;
            case CyclicJumpNode cyclicJump:
                PrintCyclicJump(cyclicJump);
                return;
            case CpuFaultTryCatchNode cpuFault:
                PushCpuFaultTryCatch(pending, cpuFault);
                return;
            case FailAsUntestedNode failAsUntested:
                PrintFailAsUntested(failAsUntested);
                return;
            case InstructionBoundaryNode boundary:
                PushInstructionBoundary(pending, boundary);
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
            case InterruptReturnNode interruptReturn:
                writer.Line(interruptReturn.Width == BitWidth.WORD_16
                    ? "return InterruptRet();" : "return InterruptRet32();");
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
            case InstructionStartNode instructionStart:
                writer.Line($"// {instructionStart.Comment}");
                return;
            case CheckExternalEventsNode checkExternalEvents:
                PrintCheckExternalEvents(checkExternalEvents);
                return;
            case SpeculativeGuardNode speculativeGuard:
                PrintSpeculativeGuard(speculativeGuard);
                return;
            case TupleSwapNode swap:
                writer.Line($"({expressions.Print(swap.First).Text}, {expressions.Print(swap.Second).Text}) = ({expressions.Print(swap.Second).Text}, {expressions.Print(swap.First).Text});");
                return;
            case BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode register } assignment
                when IncrementOrDecrement(register, assignment.Right) is string step:
                writer.Line($"{expressions.Print(register).Text}{step};");
                return;
            case BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assignment
                when CompoundAssignment(assignment) is string compound:
                writer.Line(compound);
                return;
            default:
                writer.Line(expressions.Print(node).Text + ";");
                return;
        }
    }

    /// <summary>
    /// Queues a body print followed by the empty statement C# needs after a label when
    /// <paramref name="body"/>'s last label is followed only by comments.
    /// </summary>
    private void AppendBody(List<object> items, IVisitableAstNode body) {
        items.Add(body);
        if (TrailingLabel(body) == true) {
            items.Add((Action)(() => writer.Line(";")));
        }
    }

    /// <summary>
    /// <c>true</c> when the last thing <paramref name="node"/> prints is a label, <c>false</c> when it is a statement,
    /// <c>null</c> when it prints only comments or nothing.
    /// </summary>
    private static bool? TrailingLabel(IVisitableAstNode node) {
        Stack<IVisitableAstNode> pending = new();
        pending.Push(node);
        while (pending.Count > 0) {
            switch (pending.Pop()) {
                case LabelNode:
                    return true;
                case CommentNode:
                case InstructionStartNode:
                    break;
                case BlockNode block:
                    foreach (IVisitableAstNode statement in block.Statements) {
                        pending.Push(statement);
                    }
                    break;
                case InstructionBoundaryNode boundary:
                    pending.Push(boundary.Body);
                    break;
                default:
                    return false;
            }
        }
        return null;
    }

    private void PushIfElse(Stack<object> pending, IfElseNode node) {
        List<object> items = [
            (Action)(() => writer.OpenBlock($"if ({expressions.Print(node.Condition)})"))
        ];
        AppendBody(items, node.TrueCase);
        items.Add((Action)(() => writer.CloseBlock()));
        IVisitableAstNode falseCase = node.FalseCase;
        while (OnlyIfElse(falseCase) is IfElseNode elseIf) {
            IfElseNode arm = elseIf;
            items.Add((Action)(() => writer.OpenBlock($"else if ({expressions.Print(arm.Condition)})")));
            AppendBody(items, arm.TrueCase);
            items.Add((Action)(() => writer.CloseBlock()));
            falseCase = arm.FalseCase;
        }
        if (!IsEmptyBlock(falseCase)) {
            items.Add((Action)(() => writer.OpenBlock("else")));
            AppendBody(items, falseCase);
            items.Add((Action)(() => writer.CloseBlock()));
        }
        PushItems(pending, items);
    }

    private void PushBraced(Stack<object> pending, string header, IVisitableAstNode body) {
        List<object> items = [
            (Action)(() => writer.OpenBlock(header))
        ];
        AppendBody(items, body);
        items.Add((Action)(() => writer.CloseBlock()));
        PushItems(pending, items);
    }

    private void PushDoWhile(Stack<object> pending, DoWhileNode node) {
        List<object> items = [
            (Action)(() => writer.OpenBlock("do"))
        ];
        AppendBody(items, node.Body);
        items.Add((Action)(() => writer.CloseBlock($" while ({expressions.Print(node.Condition)});")));
        PushItems(pending, items);
    }

    private static void PushItems(Stack<object> pending, List<object> items) {
        for (int index = items.Count - 1; index >= 0; index--) {
            pending.Push(items[index]);
        }
    }

    /// <summary>The <c>if</c> that is the only statement of <paramref name="node"/>, or <c>null</c>.</summary>
    private static IfElseNode? OnlyIfElse(IVisitableAstNode node) =>
        StatementsOf(node).ToList() is [IfElseNode ifElse] ? ifElse : null;

    private static bool IsEmptyBlock(IVisitableAstNode node) => node is BlockNode { Statements.Count: 0 };

    private void PrintThrow(ThrowNode node) =>
        writer.Line($"throw new {node.ExceptionType.FullName}(\"{CSharpExpressionPrinter.EscapeString(node.Message)}\");");

    private void PushSwitch(Stack<object> pending, SwitchNode node) {
        List<object> items = [
            (Action)(() => writer.OpenBlock($"switch ({expressions.Print(node.Value)})"))
        ];
        foreach (SwitchSection arm in node.Sections) {
            items.Add((Action)(() => writer.Line($"case {CaseLabel(node, arm.Label)}:")));
            items.Add((Action)(() => writer.Indent()));
            AppendBody(items, arm.Body);
            items.Add((Action)(() => writer.Dedent()));
        }
        items.Add((Action)(() => writer.Line("default:")));
        items.Add((Action)(() => writer.Indent()));
        AppendBody(items, node.Default);
        items.Add((Action)(() => writer.Dedent()));
        items.Add((Action)(() => writer.CloseBlock()));
        PushItems(pending, items);
    }

    /// <summary>The <c>case</c> label text: a <see cref="DataType.UINT32"/> switch value holds a segmented
    /// address and prints segment and offset as <c>0xSSSS_OOOO</c>; any other switch keeps
    /// <c>0x{label:X4}</c>.</summary>
    private static string CaseLabel(SwitchNode node, uint label) {
        if (node.Value.DataType == DataType.UINT32) {
            return $"0x{label >> 16:X4}_{label & 0xFFFF:X4}";
        }
        return $"0x{label:X4}";
    }

    private void PrintCyclicJump(CyclicJumpNode node) {
        writer.OpenBlock($"if (JumpDispatcher.Jump({node.MethodName}, 0x{node.LoadOffset:X4}))");
        writer.Line("loadOffset = JumpDispatcher.NextEntryAddress;");
        writer.Line("goto entrydispatcher;");
        writer.CloseBlock();
        writer.Line("return JumpDispatcher.RequiredJumpAsmReturn;");
    }

    private void PushCpuFaultTryCatch(Stack<object> pending, CpuFaultTryCatchNode node) {
        List<object> items = [
            (Action)(() => writer.OpenBlock("try"))
        ];
        AppendBody(items, node.Body);
        items.Add((Action)(() => writer.CloseBlock()));
        items.Add((Action)(() => writer.OpenBlock("catch (CpuException cpuException)")));
        items.Add((Action)(() => {
            string resumeSegment = expressions.SegmentVariable(node.FaultingInstruction.Segment);
            writer.Line($"return DispatchCpuFault(cpuException, {resumeSegment}, 0x{node.FaultingInstruction.Offset:X4}, [{FaultHandlers(node.Handlers)}]);");
        }));
        items.Add((Action)(() => writer.CloseBlock()));
        PushItems(pending, items);
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

    private void PushInstructionBoundary(Stack<object> pending, InstructionBoundaryNode node) {
        string savedSuffix = expressions.LocalVariableSuffix;
        List<object> items = [
            (Action)(() => expressions.LocalVariableSuffix = node.LocalVariableSuffix),
            node.Body,
            (Action)(() => expressions.LocalVariableSuffix = savedSuffix)
        ];
        PushItems(pending, items);
    }

    private void PrintDivision(DivisionNode node) {
        DivisionPattern division = node.Division;
        string helperName = (division.Signed, division.Width) switch {
            (false, BitWidth.BYTE_8) => nameof(CSharpOverrideHelper.Div8),
            (false, BitWidth.WORD_16) => nameof(CSharpOverrideHelper.Div16),
            (false, BitWidth.DWORD_32) => nameof(CSharpOverrideHelper.Div32),
            (true, BitWidth.BYTE_8) => nameof(CSharpOverrideHelper.IDiv8),
            (true, BitWidth.WORD_16) => nameof(CSharpOverrideHelper.IDiv16),
            _ => nameof(CSharpOverrideHelper.IDiv32)
        };
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

    /// <summary>The <c>X op= y;</c> form of <paramref name="assignment"/> when it is <c>X = X op y</c> in the
    /// target's own type and C# accepts <paramref name="assignment"/>'s right operand in a compound assignment,
    /// otherwise <c>null</c>. The right operand prints at assignment precedence.</summary>
    private string? CompoundAssignment(BinaryOperationNode assignment) {
        if (assignment.Left.DataType == DataType.BOOL
            || assignment.Right is not BinaryOperationNode operation
            || CompoundOperator(operation.BinaryOperation) is not string compoundOperator
            || !operation.Left.Equals(assignment.Left)
            || operation.DataType != assignment.Left.DataType
            || !SimpleTarget(assignment.Left)) {
            return null;
        }
        if (operation.BinaryOperation == BinaryOperation.PLUS
            && ConstantEvaluation.Resolved(operation.Right) is { } added
            && ConstantEvaluation.TrySubtractionMagnitude(added, operation.DataType.BitWidth, out ulong subtracted)) {
            ConstantNode magnitude = new(
                DataType.UnsignedFromBitWidth(added.DataType.BitWidth), subtracted);
            return $"{expressions.Print(assignment.Left).Text} -= {expressions.Print(magnitude).Text};";
        }
        if (!CompoundOperand(operation, assignment.Left.DataType, out string operand)) {
            return null;
        }
        return $"{expressions.Print(assignment.Left).Text} {compoundOperator}= {operand};";
    }

    private static string? CompoundOperator(BinaryOperation operation) => operation switch {
        BinaryOperation.PLUS => "+",
        BinaryOperation.MINUS => "-",
        BinaryOperation.BITWISE_AND => "&",
        BinaryOperation.BITWISE_OR => "|",
        BinaryOperation.BITWISE_XOR => "^",
        BinaryOperation.LEFT_SHIFT => "<<",
        BinaryOperation.RIGHT_SHIFT => ">>",
        _ => null
    };

    /// <summary>The right operand of a compound assignment: a shift takes anything, other operators need a
    /// fragment already of the target type or a constant whose value fits it.</summary>
    private bool CompoundOperand(BinaryOperationNode operation, DataType targetType, out string operand) {
        ValueNode right = operation.Right;
        CSharpFragment fragment = expressions.Print(right);
        if (operation.BinaryOperation is BinaryOperation.LEFT_SHIFT or BinaryOperation.RIGHT_SHIFT) {
            operand = ShiftOperandText(right, fragment);
            return true;
        }
        if (fragment.Type != targetType && !FitsInType(ConstantEvaluation.Resolved(right), targetType)) {
            operand = string.Empty;
            return false;
        }
        operand = ParenthesizedOperand(fragment);
        return true;
    }

    /// <summary>The operand text of a shift compound assignment: the resolved count's text, or the
    /// operand parenthesized for assignment precedence.</summary>
    private static string ShiftOperandText(ValueNode right, CSharpFragment fragment) {
        if (ConstantEvaluation.Resolved(right) is { } shiftCount) {
            return CSharpExpressionPrinter.ShiftCountText(shiftCount);
        }
        return ParenthesizedOperand(fragment);
    }

    /// <summary>The operand parenthesized just above assignment precedence.</summary>
    private static string ParenthesizedOperand(CSharpFragment fragment) =>
        CSharpExpressionPrinter.Parenthesize(fragment, CSharpExpressionPrinter.AssignmentPrecedence + 1);

    /// <summary>Whether <paramref name="constant"/>'s value fits <paramref name="targetType"/>, which is what C#
    /// requires of a compound assignment's constant operand.</summary>
    private static bool FitsInType(ConstantNode? constant, DataType targetType) {
        if (constant is null) {
            return false;
        }
        if (!targetType.Signed) {
            return constant.Value <= ConstantNode.Mask(targetType.BitWidth);
        }
        return targetType.BitWidth switch {
            BitWidth.BYTE_8 => constant.SignedValue >= sbyte.MinValue && constant.SignedValue <= sbyte.MaxValue,
            BitWidth.WORD_16 => constant.SignedValue >= short.MinValue && constant.SignedValue <= short.MaxValue,
            _ => constant.SignedValue >= int.MinValue && constant.SignedValue <= int.MaxValue
        };
    }

    /// <summary>A target a compound assignment may write: a register or a variable, or a memory slot whose
    /// address parts are built only from registers, constants, type conversions and non-assignment binary
    /// operations — evaluating them once, as C# does, cannot differ from evaluating them twice.</summary>
    private static bool SimpleTarget(ValueNode target) => target switch {
        RegisterNode or VariableReferenceNode => true,
        SegmentedPointerNode or AbsolutePointerNode =>
            IrAstChildren.ChildrenOf(target).OfType<ValueNode>().All(SimpleOperand),
        _ => false
    };

    private static bool SimpleOperand(ValueNode node) => node switch {
        RegisterNode or ConstantNode => true,
        InstructionFieldNode or TypeConversionNode or ExplicitCastNode { Value: ValueNode }
            or BinaryOperationNode { BinaryOperation: not BinaryOperation.ASSIGN } =>
            IrAstChildren.ChildrenOf(node).OfType<ValueNode>().All(SimpleOperand),
        _ => false
    };

    /// <summary>
    /// <c>++</c> or <c>--</c> when <paramref name="value"/> is <paramref name="register"/> plus or minus one in the
    /// register's own type, otherwise <c>null</c>.
    /// </summary>
    private static string? IncrementOrDecrement(RegisterNode register, ValueNode value) {
        if (value is not BinaryOperationNode { BinaryOperation: BinaryOperation.PLUS or BinaryOperation.MINUS } arithmetic
            || arithmetic.Left != register || arithmetic.DataType != register.DataType) {
            return null;
        }
        if (IsOne(arithmetic.Right)) {
            return arithmetic.BinaryOperation == BinaryOperation.PLUS ? "++" : "--";
        }
        if (arithmetic.BinaryOperation == BinaryOperation.PLUS
            && arithmetic.Right is ConstantNode added
            && ConstantEvaluation.TrySubtractionMagnitude(added, arithmetic.DataType.BitWidth, out ulong magnitude)
            && magnitude == 1) {
            return "--";
        }
        return null;
    }

    private static bool IsOne(ValueNode node) =>
        node is ConstantNode { Value: 1 } or InstructionFieldNode { ResolvedNode: ConstantNode { Value: 1 } };
}
