namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;

using System.Globalization;
using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Writes a tree of runtime and code-generation AST nodes as C# statements. Expressions are printed by
/// <see cref="CSharpExpressionPrinter"/>; labels come from <paramref name="labelOf"/>.
/// </summary>
internal sealed class CSharpStatementPrinter(CSharpSourceWriter writer, CSharpExpressionPrinter expressions, Func<IGotoTarget, string> labelOf) {
    /// <summary>The goto targets of the whole method being printed. A
    /// <c>for</c> rewrite refuses a loop when any statement of the method - not only of the block
    /// being dispatched - jumps to its header or its step.</summary>
    public GotoTargets MethodGotoTargets => expressions.MethodGotoTargets;

    /// <summary>Writes <paramref name="node"/> in statement position. The statement walk runs on an explicit
    /// stack - nodes to print interleaved with <see cref="Action"/> writer operations - so arbitrarily deep
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
                case CountedLoop countedLoop:
                    PushCountedLoop(pending, countedLoop);
                    break;
                case BoundaryPrint boundaryPrint:
                    PushBoundaryPrint(pending, boundaryPrint);
                    break;
            }
        }
    }

    private void Dispatch(IVisitableAstNode node, Stack<object> pending) {
        switch (node) {
            case BlockNode block:
                PushBlockStatements(pending, block);
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
            case MultiplicationNode multiplication:
                PrintMultiplication(multiplication);
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
            case ReturnNode { Value: ProcedureReturnHelperNode helper } when
                    expressions.TypedSignature is { } typed:
                PrintTypedReturn(helper, typed);
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
            case InterruptServiceCallNode serviceCall:
                PrintInterruptServiceCall(serviceCall);
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

    /// <summary>A typed service call and the assignment of its return value to the contract's
    /// output lvalues: the bare invocation for a void contract, <c>AX = call</c> for one output
    /// or <c>(AX, CarryFlag) = call</c> for several. Register-local promotion can turn an output
    /// target into a local (<c>ax = call</c>), which the existing writeback machinery stores back.</summary>
    private void PrintInterruptServiceCall(InterruptServiceCallNode call) {
        if (call.Vector == 0x21 && DosInt21Handler.SavesCallerStack((byte)call.SelectorValue)) {
            // The saved pointer addresses the frame top, six bytes below this caller SP.
            writer.Line($"{call.PropertyPath}.{nameof(DosInt21Handler.RecordCallerStack)}(SS, SP);");
        }
        string invocation = expressions.Print(call).Text;
        if (call.Results.Count == 0) {
            writer.Line(invocation + ";");
            return;
        }
        string targets = string.Join(", ",
            call.Results.Select(result => expressions.Print(result).Text));
        string left = call.Results.Count == 1 ? targets : $"({targets})";
        writer.Line($"{left} = {invocation};");
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

    /// <summary>A <c>while</c> loop (<see cref="PreTestLoopNode"/> or <see cref="WhileNode"/>) that prints as
    /// <c>for</c>: <paramref name="Init"/> runs once before the first test, <paramref name="Step"/> runs at
    /// the end of every iteration (removed from <paramref name="Body"/>), the condition is the loop's own.</summary>
    private sealed record CountedLoop(ValueNode Condition, IVisitableAstNode Init, IVisitableAstNode Step, BlockNode Body);

    /// <summary>An <see cref="InstructionBoundaryNode"/> whose body is queued as <paramref name="Items"/>,
    /// which can hold <see cref="CountedLoop"/> markers; prints with the boundary's local variable
    /// suffix like <see cref="PushInstructionBoundary"/>.</summary>
    private sealed record BoundaryPrint(InstructionBoundaryNode Boundary, IReadOnlyList<object> Items);

    /// <summary>The condition, step and rewritten body of a <see cref="PreTestLoopNode"/> eligible for the
    /// <c>for</c> form, before its init statement is located.</summary>
    private sealed record CountedLoopShape(string Counter, IVisitableAstNode Step, BlockNode Body);

    private static readonly ISet<BinaryOperation> CountedLoopComparisons = new HashSet<BinaryOperation> {
        BinaryOperation.EQUAL, BinaryOperation.NOT_EQUAL,
        BinaryOperation.LESS_THAN, BinaryOperation.GREATER_THAN,
        BinaryOperation.LESS_THAN_OR_EQUAL, BinaryOperation.GREATER_THAN_OR_EQUAL
    };

    /// <summary>Queues the statements of <paramref name="block"/>; a <see cref="PreTestLoopNode"/> whose
    /// counter is a register local initialized right before the loop, tested against a constant and stepped
    /// by a constant as the body's last statement is queued as <c>for (init; test; step)</c> instead of
    /// <c>while</c>.</summary>
    private void PushBlockStatements(Stack<object> pending, BlockNode block) {
        IReadOnlyList<IVisitableAstNode> statements = block.Statements;
        if (expressions.LocalReads is null) {
            for (int index = statements.Count - 1; index >= 0; index--) {
                pending.Push(statements[index]);
            }
            return;
        }
        object?[] slots = new object?[statements.Count];
        for (int index = 0; index < statements.Count; index++) {
            slots[index] = statements[index];
        }
        for (int index = 0; index < statements.Count; index++) {
            switch (statements[index]) {
                case PreTestLoopNode loop:
                    if (CountedLoopShapeOf(block, MethodGotoTargets, loop.Condition, loop.Body, loop.HeaderTarget) is { } shape
                        && InitBefore(slots, statements, index, shape.Counter) is { } init) {
                        slots[index] = new CountedLoop(loop.Condition, init, shape.Step, shape.Body);
                    }
                    break;
                case WhileNode whileNode:
                    if (CountedLoopShapeOf(block, MethodGotoTargets, whileNode.Condition, whileNode.Body, null) is { } whileShape
                        && InitBefore(slots, statements, index, whileShape.Counter) is { } whileInit) {
                        slots[index] = new CountedLoop(whileNode.Condition, whileInit, whileShape.Step, whileShape.Body);
                    }
                    break;
                case InstructionBoundaryNode boundary:
                    BoundaryCountedLoops(block, MethodGotoTargets, slots, index, boundary);
                    break;
            }
        }
        for (int index = statements.Count - 1; index >= 0; index--) {
            if (slots[index] is { } item) {
                pending.Push(item);
            }
        }
    }

    /// <summary>Queues a boundary body's statements in which every counted loop whose init is the last
    /// statement of the previous sibling boundary became a <see cref="CountedLoop"/> marker.</summary>
    private void BoundaryCountedLoops(BlockNode block, GotoTargets targets, object?[] slots, int index,
        InstructionBoundaryNode boundary) {
        IReadOnlyList<IVisitableAstNode> inner = boundary.Body.Statements;
        List<object>? items = null;
        for (int position = 0; position < inner.Count; position++) {
            if (inner[position] is not (PreTestLoopNode or WhileNode)) {
                continue;
            }
            (ValueNode condition, BlockNode body, IGotoTarget? header) = inner[position] switch {
                PreTestLoopNode preTestLoop => (preTestLoop.Condition, preTestLoop.Body, preTestLoop.HeaderTarget),
                WhileNode whileNode => (whileNode.Condition, whileNode.Body, null),
                _ => throw new InvalidOperationException()
            };
            int previous = position - 1;
            while (previous >= 0 && CommentOnly(inner[previous])) {
                previous--;
            }
            // A real statement inside the boundary precedes the loop: it, not a sibling boundary, is the
            // only possible init, and the body block itself decides that case when it is dispatched.
            if (previous >= 0
                || CountedLoopShapeOf(block, targets, condition, body, header) is not { } shape
                || InitBefore(slots, block.Statements, index, shape.Counter) is not { } init) {
                continue;
            }
            items ??= inner.Cast<object>().ToList();
            items[position] = new CountedLoop(condition, init, shape.Step, shape.Body);
        }
        if (items is not null) {
            slots[index] = new BoundaryPrint(boundary, items);
        }
    }

    /// <summary>The statement right before position <paramref name="index"/> of <paramref name="statements"/>
    /// (only comment lines are skipped) when it assigns the local
    /// <paramref name="counter"/>: the statement itself, or the last statement of the previous instruction
    /// boundary. Consumed slots are replaced so the init is not printed twice.</summary>
    private IVisitableAstNode? InitBefore(object?[] slots, IReadOnlyList<IVisitableAstNode> statements, int index,
        string counter) {
        int previous = index - 1;
        while (previous >= 0 && CommentOnly(statements[previous])) {
            previous--;
        }
        if (previous < 0 || slots[previous] is not IVisitableAstNode candidate) {
            return null;
        }
        if (AssignTo(candidate, counter) is { } init) {
            slots[previous] = null;
            return init;
        }
        if (candidate is InstructionBoundaryNode boundary) {
            IReadOnlyList<IVisitableAstNode> inner = boundary.Body.Statements;
            if (inner.Count > 0 && AssignTo(inner[^1], counter) is { } boundaryInit) {
                slots[previous] = boundary with { Body = new BlockNode(inner.Take(inner.Count - 1).ToArray()) };
                return boundaryInit;
            }
        }
        return null;
    }

    /// <summary>A line that only prints a comment: an <see cref="InstructionStartNode"/>, a
    /// <see cref="CommentNode"/>, or an instruction boundary left with only its comment lines.</summary>
    private static bool CommentOnly(IVisitableAstNode statement) =>
        statement is InstructionStartNode or CommentNode
        || statement is InstructionBoundaryNode boundary
            && boundary.Body.Statements.All(inner => inner is InstructionStartNode or CommentNode);

    /// <summary>The condition, step and rewritten body making <paramref name="loop"/> a counted loop,
    /// or <c>null</c>: the condition compares a register local with a constant; the body's last statement
    /// (possibly the last statement of its last instruction boundary) adds or subtracts a constant to that
    /// local; no <see cref="ContinueNode"/> binds to the loop, no goto of the method targets its header,
    /// and nothing jumps to the step.</summary>
    private CountedLoopShape? CountedLoopShapeOf(BlockNode block, GotoTargets targets, ValueNode condition,
        BlockNode loopBody, IGotoTarget? headerTarget) {
        if (CounterName(condition) is not { } counter) {
            return null;
        }
        IReadOnlyList<IVisitableAstNode> body = loopBody.Statements;
        if (body.Count == 0) {
            return null;
        }
        IVisitableAstNode last = body[^1];
        InstructionBoundaryNode? stepBoundary = last as InstructionBoundaryNode;
        IReadOnlyList<IVisitableAstNode> stepList = stepBoundary?.Body.Statements ?? body;
        IVisitableAstNode step = stepList[^1];
        if (stepBoundary is not null && stepList.Count - 1 <= InstructionStartIndex(stepList)) {
            return null;
        }
        if (!IsStep(step, counter)) {
            return null;
        }
        if (headerTarget is not null && targets.Labels.Contains(headerTarget)) {
            return null;
        }
        if (StepIsJumpedInto(stepBoundary, stepList, targets)) {
            return null;
        }
        if (BindsContinue(loopBody)) {
            return null;
        }
        return new CountedLoopShape(counter, step, ForBody(body, stepBoundary, stepList));
    }

    private static BlockNode ForBody(IReadOnlyList<IVisitableAstNode> body,
        InstructionBoundaryNode? stepBoundary, IReadOnlyList<IVisitableAstNode> stepList) {
        if (stepBoundary is null) {
            return new BlockNode(body.Take(body.Count - 1).ToArray());
        }
        return new BlockNode([
            .. body.Take(body.Count - 1),
            stepBoundary with { Body = new BlockNode(stepList.Take(stepList.Count - 1).ToArray()) }]);
    }

    /// <summary>Whether the method jumps to the step's boundary anchor or to a label inside the
    /// boundary just before the step: either jump would land past the step in the <c>while</c> but
    /// at the step in the <c>for</c>.</summary>
    private static bool StepIsJumpedInto(InstructionBoundaryNode? stepBoundary,
        IReadOnlyList<IVisitableAstNode> stepList, GotoTargets targets) {
        if (stepBoundary?.Anchor is { } anchor && targets.Labels.Contains(anchor)) {
            return true;
        }
        return stepList.Count > 1 && stepList[^2] is LabelNode beforeStep
            && targets.Labels.Contains(beforeStep.Target);
    }

    private static int InstructionStartIndex(IReadOnlyList<IVisitableAstNode> statements) {
        for (int index = 0; index < statements.Count; index++) {
            if (statements[index] is InstructionStartNode) {
                return index;
            }
        }
        return -1;
    }

    /// <summary>A <see cref="ContinueNode"/> directly inside <paramref name="body"/> - one outside any nested
    /// loop binds to the loop being rewritten, and in a <c>for</c> it would wrongly run the step.</summary>
    private static bool BindsContinue(BlockNode body) {
        Stack<IVisitableAstNode> pending = new();
        foreach (IVisitableAstNode statement in body.Statements) {
            pending.Push(statement);
        }
        while (pending.Count > 0) {
            IVisitableAstNode current = pending.Pop();
            if (current is ContinueNode) {
                return true;
            }
            if (current is not (WhileNode or WhileWithExitNode or InfiniteLoopNode or DoWhileNode or PreTestLoopNode)) {
                foreach (IVisitableAstNode child in ChildStatements(current)) {
                    pending.Push(child);
                }
            }
        }
        return false;
    }

    /// <summary>The local name a comparison operand of <paramref name="condition"/> substitutes to, when the
    /// other operand resolves to a constant; <c>null</c> when neither side matches.</summary>
    private string? CounterName(ValueNode condition) {
        if (condition is not BinaryOperationNode comparison
            || !CountedLoopComparisons.Contains(comparison.BinaryOperation)) {
            return null;
        }
        return CounterOperand(comparison.Left, comparison.Right) ?? CounterOperand(comparison.Right, comparison.Left);
    }

    private string? CounterOperand(ValueNode localSide, ValueNode constantSide) {
        if (ConstantEvaluation.Resolved(constantSide) is not null) {
            return LocalNameOf(UnwrapConversions(localSide));
        }
        return null;
    }

    /// <summary>The name of the local <paramref name="node"/> prints as; <c>null</c> when the plan does not
    /// substitute it.</summary>
    private string? LocalNameOf(IVisitableAstNode node) {
        IReadOnlyDictionary<IVisitableAstNode, SubstitutedRead>? reads = expressions.LocalReads;
        if (reads is null || !reads.TryGetValue(node, out SubstitutedRead? read) || read is null) {
            return null;
        }
        int castEnd = read.Text.LastIndexOf(')');
        return castEnd < 0 ? read.Text : read.Text[(castEnd + 1)..];
    }

    private static ValueNode UnwrapConversions(ValueNode node) => node switch {
        TypeConversionNode conversion => UnwrapConversions(conversion.Value),
        ExplicitCastNode { Value: ValueNode value } => UnwrapConversions(value),
        _ => node
    };

    /// <summary><paramref name="statement"/> when it assigns the local <paramref name="counter"/>, else
    /// <c>null</c>.</summary>
    private IVisitableAstNode? AssignTo(IVisitableAstNode statement, string counter) =>
        statement is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assignment
        && LocalNameOf(assignment.Left) == counter ? statement : null;

    /// <summary>Whether <paramref name="statement"/> assigns the local <paramref name="counter"/> from itself
    /// plus or minus a constant.</summary>
    private bool IsStep(IVisitableAstNode statement, string counter) =>
        statement is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assignment
        && LocalNameOf(assignment.Left) == counter
        && UnwrapConversions(assignment.Right) is BinaryOperationNode {
            BinaryOperation: BinaryOperation.PLUS or BinaryOperation.MINUS } arithmetic
        && LocalNameOf(UnwrapConversions(arithmetic.Left)) == counter
        && ConstantEvaluation.Resolved(arithmetic.Right) is not null;

    /// <summary>The text <paramref name="statement"/> prints, without its trailing semicolon, for a
    /// <c>for</c> header slot.</summary>
    private string StatementText(IVisitableAstNode statement) => statement switch {
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode register } assignment
            when IncrementOrDecrement(register, assignment.Right) is { } step =>
            expressions.Print(register).Text + step,
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assignment
            when CompoundAssignment(assignment) is { } compound =>
            compound[..^1],
        _ => expressions.Print(statement).Text
    };

    private void PushCountedLoop(Stack<object> pending, CountedLoop loop) {
        List<object> items = [
            (Action)(() => writer.OpenBlock(
                $"for ({StatementText(loop.Init)}; {expressions.Print(loop.Condition)}; {StatementText(loop.Step)})"))
        ];
        AppendBody(items, loop.Body);
        items.Add((Action)(() => writer.CloseBlock()));
        PushItems(pending, items);
    }

    private void PushBoundaryPrint(Stack<object> pending, BoundaryPrint boundaryPrint) {
        string savedSuffix = expressions.LocalVariableSuffix;
        List<object> items = [
            (Action)(() => expressions.LocalVariableSuffix = boundaryPrint.Boundary.LocalVariableSuffix)
        ];
        items.AddRange(boundaryPrint.Items);
        items.Add((Action)(() => expressions.LocalVariableSuffix = savedSuffix));
        PushItems(pending, items);
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

    /// <summary>A typed method's <c>ret</c> / <c>retf</c>: the <c>NearReturn(n)</c> /
    /// <c>FarReturn(n)</c> statement, then <c>return</c> of the signature's outputs. A count of
    /// <c>0</c> prints the parameterless overload.</summary>
    private void PrintTypedReturn(ProcedureReturnHelperNode helper, TypedSignature typed) {
        string helperName = helper.Distance == CallDistance.Near ? "NearReturn" : "FarReturn";
        string arguments = TypedReturnArguments(helper);
        writer.Line(arguments == "0" ? $"{helperName}();" : $"{helperName}({arguments});");
        writer.Line(typed.ReturnStatement);
    }

    private string TypedReturnArguments(ProcedureReturnHelperNode helper) {
        if (helper.Arguments.Count == 0) {
            return "0";
        }
        return string.Join(", ", helper.Arguments.Select(TypedReturnArgumentText));
    }

    private string TypedReturnArgumentText(IVisitableAstNode argument) {
        if (ConstantEvaluation.Resolved(argument) is { } constant) {
            return constant.Value.ToString(CultureInfo.InvariantCulture);
        }
        return expressions.Print(argument).Text;
    }

    private void PrintCyclicJump(CyclicJumpNode node) {
        string delegateName = expressions.TypedSignatures?.DelegateNameOf(node.MethodName) ?? node.MethodName;
        string target = node.SingleEntry
            ? GeneratedOverrideNames.EntryFieldNameOf(delegateName)
            : delegateName;
        writer.OpenBlock($"if (JumpDispatcher.Jump({target}, 0x{node.LoadOffset:X4}))");
        if (expressions.NeedsEntryDispatch) {
            writer.Line("loadOffset = JumpDispatcher.NextEntryAddress;");
        }
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
        string invoke = FaultInvoke(handler);
        return $"new CpuFaultTarget(new SegmentedAddress({handlerSegment}, 0x{handler.Handler.Offset:X4}), {invoke})";
    }

    private string FaultInvoke(CpuFaultHandler handler) {
        if (handler.SingleEntry) {
            return expressions.TypedSignatures?.DelegateNameOf(handler.MethodName) ?? handler.MethodName;
        }
        return $"() => {handler.MethodName}(0x{handler.LoadOffset:X4})";
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

    private void PrintMultiplication(MultiplicationNode node) {
        string helperName = (node.Signed, node.Width) switch {
            (false, BitWidth.WORD_16) => nameof(CSharpOverrideHelper.Mul16),
            (false, BitWidth.DWORD_32) => nameof(CSharpOverrideHelper.Mul32),
            (true, BitWidth.WORD_16) => nameof(CSharpOverrideHelper.IMul16),
            _ => nameof(CSharpOverrideHelper.IMul32)
        };
        DataType operandType = DataType.FromBitWidth(node.Width, node.Signed);
        string left = CSharpExpressionPrinter.Cast(operandType, expressions.Print(node.Left));
        string right = CSharpExpressionPrinter.Cast(operandType, expressions.Print(node.Right));
        string low = expressions.Print(node.Low);
        string high = expressions.Print(node.High);
        writer.Line($"({low}, {high}) = {helperName}({left}, {right});");
    }

    private void PrintCheckExternalEvents(CheckExternalEventsNode node) {
        string resumeSegment = expressions.SegmentVariable(node.ResumeAddress.Segment);
        writer.Line($"CheckExternalEvents({resumeSegment}, 0x{node.ResumeAddress.Offset:X4});");
    }

    private void PrintSpeculativeGuard(SpeculativeGuardNode node) {
        string segment = expressions.SegmentVariable(node.Address.Segment);
        if (node.SignatureFields.Count == 1) {
            writer.Line($"VerifySpeculativeEntryOrFail({segment}, 0x{node.Address.Offset:X4}, {node.SignatureFields[0]});");
            return;
        }
        writer.Line($"VerifySpeculativeRunOrFail({segment}, 0x{node.Address.Offset:X4}, {node.RunFieldName});");
    }

    /// <summary>The <c>X op= y;</c> form of <paramref name="assignment"/> when it is <c>X = X op y</c> in the
    /// target's own type and C# accepts <paramref name="assignment"/>'s right operand in a compound assignment,
    /// otherwise <c>null</c>. The right operand prints at assignment precedence.</summary>
    private string? CompoundAssignment(BinaryOperationNode assignment) {
        if (assignment.Left.DataType == DataType.BOOL
            || assignment.Right is not BinaryOperationNode operation
            || CompoundOperator(operation.BinaryOperation) is not string compoundOperator
            || operation.DataType != assignment.Left.DataType
            || !SimpleTarget(assignment.Left)
            || expressions.Print(operation.Left).Text != expressions.Print(assignment.Left).Text) {
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
        if (right.DataType == DataType.UINT32) {
            return CSharpExpressionPrinter.Cast(DataType.INT32, fragment);
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
    /// operations - evaluating them once, as C# does, cannot differ from evaluating them twice.</summary>
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
    private string? IncrementOrDecrement(RegisterNode register, ValueNode value) {
        if (value is not BinaryOperationNode { BinaryOperation: BinaryOperation.PLUS or BinaryOperation.MINUS } arithmetic
            || arithmetic.DataType != register.DataType
            || expressions.Print(arithmetic.Left).Text != expressions.Print(register).Text) {
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
