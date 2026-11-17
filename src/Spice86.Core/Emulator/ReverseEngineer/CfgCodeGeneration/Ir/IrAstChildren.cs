namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using System.Linq;

/// <summary>Child enumeration and child mapping of IR statement and terminator AST nodes.</summary>
internal static class IrAstChildren {
    /// <summary>The AST roots <paramref name="terminator"/> embeds: a branch condition, a switch value or an exit transfer.</summary>
    public static IEnumerable<IVisitableAstNode> RootsOf(IrTerminator terminator) =>
        terminator switch {
            IrBranch branch => [branch.Condition],
            IrSwitch switchNode => [switchNode.Value],
            IrExit exit => [exit.Transfer],
            _ => []
        };

    /// <summary>Every AST node <paramref name="terminator"/> holds, each exactly once, in
    /// pre-order: <see cref="RootsOf"/> with <see cref="Descendants"/> of each root.</summary>
    public static IEnumerable<IVisitableAstNode> TerminatorNodes(IrTerminator terminator) =>
        RootsOf(terminator).SelectMany(Descendants);

    /// <summary><paramref name="node"/> and every descendant, in pre-order. The walk runs on an explicit
    /// stack so deeply nested statements cannot overflow the thread's stack.</summary>
    public static IEnumerable<IVisitableAstNode> Descendants(IVisitableAstNode node) {
        yield return node;
        Stack<IVisitableAstNode> pending = new();
        List<IVisitableAstNode> children = [];
        PushChildren(node, pending, children);
        while (pending.Count > 0) {
            IVisitableAstNode current = pending.Pop();
            yield return current;
            PushChildren(current, pending, children);
        }
    }

    private static void PushChildren(IVisitableAstNode node, Stack<IVisitableAstNode> pending,
        List<IVisitableAstNode> children) {
        children.Clear();
        AddChildren(node, children);
        for (int i = children.Count - 1; i >= 0; i--) {
            pending.Push(children[i]);
        }
    }

    /// <summary>The direct children of <paramref name="node"/>.</summary>
    /// <exception cref="NotSupportedException"><paramref name="node"/> is a type the walker does not know.</exception>
    public static IEnumerable<IVisitableAstNode> ChildrenOf(IVisitableAstNode node) {
        List<IVisitableAstNode> children = [];
        AddChildren(node, children);
        return children;
    }

    /// <summary>Appends the direct children of <paramref name="node"/> to
    /// <paramref name="children"/>: the same children <see cref="ChildrenOf"/> reports, without the
    /// per-node enumerators <see cref="Descendants"/> would otherwise allocate.</summary>
    /// <exception cref="NotSupportedException"><paramref name="node"/> is a type the walker does not know.</exception>
    private static void AddChildren(IVisitableAstNode node, List<IVisitableAstNode> children) {
        void Add(IVisitableAstNode? child) {
            if (child is not null) {
                children.Add(child);
            }
        }
        switch (node) {
            case BinaryOperationNode binary:
                Add(binary.Left); Add(binary.Right);
                break;
            case UnaryOperationNode unary:
                Add(unary.Value);
                break;
            case TypeConversionNode conversion:
                Add(conversion.Value);
                break;
            case ExplicitCastNode cast:
                Add(cast.Value);
                break;
            case SegmentedPointerNode pointer:
                Add(pointer.Segment); Add(pointer.DefaultSegment); Add(pointer.Offset);
                break;
            case AbsolutePointerNode pointer:
                Add(pointer.AbsoluteAddress);
                break;
            case SegmentedAddressNode address:
                Add(address.Segment); Add(address.Offset);
                break;
            case InstructionFieldNode field:
                Add(field.ResolvedNode);
                break;
            case VariableDeclarationNode declaration:
                Add(declaration.Initializer);
                break;
            case ReturnNode returnNode:
                Add(returnNode.Value);
                break;
            case InterruptServiceCallNode serviceCall:
                children.AddRange(serviceCall.Arguments);
                children.AddRange(serviceCall.Results);
                break;
            case MethodCallNode call:
                children.AddRange(call.Arguments);
                break;
            case MethodCallValueNode callValue:
                children.AddRange(callValue.CallNode.Arguments);
                break;
            case DivisionNode division:
                Add(division.Division.Dividend); Add(division.Division.Divisor);
                Add(division.Division.Low); Add(division.Division.High);
                break;
            case MultiplicationNode multiplication:
                Add(multiplication.Left); Add(multiplication.Right);
                Add(multiplication.Low); Add(multiplication.High);
                break;
            case TupleSwapNode swap:
                Add(swap.First); Add(swap.Second);
                break;
            case AluOperationNode aluOperation:
                children.AddRange(aluOperation.Call.CallNode.Arguments);
                break;
            case AluStatementNode aluStatement:
                children.AddRange(aluStatement.Call.Arguments);
                break;
            case ConditionalValueNode conditional:
                Add(conditional.Condition); Add(conditional.WhenTrue); Add(conditional.WhenFalse);
                break;
            case OpaqueNode opaque:
                Add(opaque.Original);
                break;
            case FailAsUntestedNode fail:
                foreach (UntestedMessagePart part in fail.Parts) {
                    Add(part.Value);
                }
                break;
            case BlockNode block:
                children.AddRange(block.Statements);
                break;
            case IfElseNode ifElse:
                Add(ifElse.Condition); Add(ifElse.TrueCase); Add(ifElse.FalseCase);
                break;
            case PreTestLoopNode preTestLoop:
                Add(preTestLoop.Condition); Add(preTestLoop.Body);
                break;
            case WhileNode whileNode:
                Add(whileNode.Condition); Add(whileNode.Body);
                break;
            case WhileWithExitNode whileWithExit:
                Add(whileWithExit.Condition); Add(whileWithExit.Body); Add(whileWithExit.ExitCondition);
                break;
            case DoWhileNode doWhile:
                Add(doWhile.Body); Add(doWhile.Condition);
                break;
            case InfiniteLoopNode infiniteLoop:
                Add(infiniteLoop.Body);
                break;
            case SwitchNode switchNode:
                foreach (SwitchSection section in switchNode.Sections) {
                    children.Add(section.Body);
                }
                Add(switchNode.Value); Add(switchNode.Default);
                break;
            case CpuFaultTryCatchNode cpuFault:
                Add(cpuFault.Body);
                break;
            case InstructionBoundaryNode boundary:
                Add(boundary.Body);
                break;
            case InstructionNode instruction:
                children.AddRange(instruction.Parameters);
                break;
            case MoveIpNextNode moveIp:
                Add(moveIp.NextIp);
                break;
            case RegisterWriteBackNode writeBack:
                Add(writeBack.Register); Add(writeBack.Local);
                break;
            case ConstantNode or CpuFlagNode or FlagRegisterNode or RegisterNode or VariableReferenceNode
                or InstructionPointerNode or LoadOffsetParameterNode or SegmentVariableNode
                or OffsetLiteralNode or SignatureFieldNode or CommentNode or InstructionStartNode
                or CheckExternalEventsNode or SpeculativeGuardNode or BreakNode or ContinueNode
                or GotoNode or LabelNode or EntryDispatcherLabelNode or CyclicJumpNode
                or MethodGroupNode or LocalDeclarationNode or InterruptReturnNode or SelectorNode
                or SetInterruptShadowNode or ThrowNode or CfgInstructionNode
                or SegmentedAddressListNode or RegisterLocalNode:
                break;
            default:
                throw new NotSupportedException(
                    $"{node.GetType().Name} has no AST children enumeration.");
        }
    }

    /// <summary>
    /// <paramref name="node"/> rebuilt with <paramref name="map"/> applied to every direct child
    /// <see cref="ChildrenOf"/> reports, except computed children like
    /// <see cref="InstructionFieldNode.ResolvedNode"/> which cannot be written back and make the node a
    /// leaf here. A null child stays null and is not passed to <paramref name="map"/>. Returns the same
    /// instance when every mapped child is reference-equal to the original; leaves return themselves.
    /// Call nodes rebuild through <c>WithArguments</c> so the typed subtype is kept.
    /// </summary>
    /// <exception cref="NotSupportedException"><paramref name="node"/> is a type the walker does not know.</exception>
    public static IVisitableAstNode MapChildren(IVisitableAstNode node,
        Func<IVisitableAstNode, IVisitableAstNode> map) => node switch {
        BinaryOperationNode binary => Map(node, map, [binary.Left, binary.Right],
            c => binary with { Left = Child<ValueNode>(c[0]), Right = Child<ValueNode>(c[1]) }),
        UnaryOperationNode unary => Map(node, map, [unary.Value],
            c => unary with { Value = Child<ValueNode>(c[0]) }),
        TypeConversionNode conversion => Map(node, map, [conversion.Value],
            c => conversion with { Value = Child<ValueNode>(c[0]) }),
        ExplicitCastNode cast => Map(node, map, [cast.Value],
            c => cast with { Value = Child<IVisitableAstNode>(c[0]) }),
        SegmentedPointerNode pointer => Map(node, map,
            [pointer.Segment, pointer.DefaultSegment, pointer.Offset],
            c => pointer with {
                Segment = Child<ValueNode>(c[0]), DefaultSegment = (ValueNode?)c[1], Offset = Child<ValueNode>(c[2]) }),
        AbsolutePointerNode pointer => Map(node, map, [pointer.AbsoluteAddress],
            c => pointer with { AbsoluteAddress = Child<ValueNode>(c[0]) }),
        SegmentedAddressNode address => Map(node, map, [address.Segment, address.Offset],
            c => address with { Segment = Child<ValueNode>(c[0]), Offset = Child<ValueNode>(c[1]) }),
        VariableDeclarationNode declaration => Map(node, map, [declaration.Initializer],
            c => declaration with { Initializer = Child<ValueNode>(c[0]) }),
        ReturnNode returnNode => Map(node, map, [returnNode.Value],
            c => returnNode with { Value = Child<IVisitableAstNode>(c[0]) }),
        // A service call's results are write targets, like an assignment's left: the mapping
        // covers the argument reads only and never substitutes a result lvalue.
        InterruptServiceCallNode serviceCall => MapList(serviceCall, map, serviceCall.Arguments,
            serviceCall.WithArguments),
        MethodCallNode call => MapList(call, map, call.Arguments, call.WithArguments),
        MethodCallValueNode callValue => MapList(callValue, map, callValue.CallNode.Arguments,
            callValue.WithArguments),
        DivisionNode division => Map(node, map,
            [division.Division.Dividend, division.Division.Divisor,
                division.Division.Low, division.Division.High],
            c => division with {
                Division = division.Division with {
                    Dividend = Child<ValueNode>(c[0]), Divisor = Child<ValueNode>(c[1]),
                    Low = Child<RegisterNode>(c[2]), High = Child<RegisterNode>(c[3]) } }),
        MultiplicationNode multiplication => Map(node, map,
            [multiplication.Left, multiplication.Right, multiplication.Low, multiplication.High],
            c => multiplication with {
                Left = Child<ValueNode>(c[0]), Right = Child<ValueNode>(c[1]),
                Low = Child<RegisterNode>(c[2]), High = Child<RegisterNode>(c[3]) }),
        TupleSwapNode swap => Map(node, map, [swap.First, swap.Second],
            c => swap with { First = Child<ValueNode>(c[0]), Second = Child<ValueNode>(c[1]) }),
        AluOperationNode aluOperation => MapList(aluOperation, map,
            aluOperation.Call.CallNode.Arguments,
            args => aluOperation with { Call = aluOperation.Call.WithArguments(args) }),
        AluStatementNode aluStatement => MapList(aluStatement, map, aluStatement.Call.Arguments,
            args => aluStatement with { Call = aluStatement.Call.WithArguments(args) }),
        ConditionalValueNode conditional => Map(node, map,
            [conditional.Condition, conditional.WhenTrue, conditional.WhenFalse],
            c => conditional with {
                Condition = Child<ValueNode>(c[0]), WhenTrue = Child<ValueNode>(c[1]), WhenFalse = Child<ValueNode>(c[2]) }),
        OpaqueNode opaque => Map(node, map, [opaque.Original],
            c => opaque with { Original = Child<IVisitableAstNode>(c[0]) }),
        FailAsUntestedNode fail => MapParts(fail, map),
        BlockNode block => MapList(block, map, block.Statements,
            statements => new BlockNode(statements)),
        IfElseNode ifElse => Map(node, map, [ifElse.Condition, ifElse.TrueCase, ifElse.FalseCase],
            c => new IfElseNode(Child<ValueNode>(c[0]), Child<IVisitableAstNode>(c[1]), Child<IVisitableAstNode>(c[2]))),
        PreTestLoopNode preTestLoop => Map(node, map, [preTestLoop.Condition, preTestLoop.Body],
            c => preTestLoop with { Condition = Child<ValueNode>(c[0]), Body = Child<BlockNode>(c[1]) }),
        WhileNode whileNode => Map(node, map, [whileNode.Condition, whileNode.Body],
            c => new WhileNode(Child<ValueNode>(c[0]), Child<BlockNode>(c[1]))),
        WhileWithExitNode whileWithExit => Map(node, map,
            [whileWithExit.Condition, whileWithExit.Body, whileWithExit.ExitCondition],
            c => whileWithExit with {
                Condition = Child<ValueNode>(c[0]), Body = Child<BlockNode>(c[1]), ExitCondition = Child<ValueNode>(c[2]) }),
        DoWhileNode doWhile => Map(node, map, [doWhile.Body, doWhile.Condition],
            c => doWhile with { Body = Child<BlockNode>(c[0]), Condition = Child<ValueNode>(c[1]) }),
        InfiniteLoopNode infiniteLoop => Map(node, map, [infiniteLoop.Body],
            c => infiniteLoop with { Body = Child<BlockNode>(c[0]) }),
        SwitchNode switchNode => MapSwitch(switchNode, map),
        CpuFaultTryCatchNode cpuFault => Map(node, map, [cpuFault.Body],
            c => cpuFault with { Body = Child<BlockNode>(c[0]) }),
        InstructionBoundaryNode boundary => Map(node, map, [boundary.Body],
            c => boundary with { Body = Child<BlockNode>(c[0]) }),
        InstructionNode instruction => MapList(instruction, map, instruction.Parameters,
            parameters => instruction with { Parameters = parameters }),
        MoveIpNextNode moveIp => Map(node, map, [moveIp.NextIp],
            c => moveIp with { NextIp = Child<IVisitableAstNode>(c[0]) }),
        InstructionFieldNode or ConstantNode or CpuFlagNode or FlagRegisterNode or RegisterNode
            or VariableReferenceNode or InstructionPointerNode or LoadOffsetParameterNode
            or SegmentVariableNode or OffsetLiteralNode or SignatureFieldNode or CommentNode
            or InstructionStartNode or CheckExternalEventsNode or SpeculativeGuardNode or BreakNode
            or ContinueNode or GotoNode or LabelNode or EntryDispatcherLabelNode or CyclicJumpNode
            or MethodGroupNode or LocalDeclarationNode or InterruptReturnNode or SelectorNode
            or SetInterruptShadowNode or ThrowNode or CfgInstructionNode
            or SegmentedAddressListNode or RegisterLocalNode => node,
        RegisterWriteBackNode writeBack => Map(node, map, [writeBack.Register, writeBack.Local],
            c => new RegisterWriteBackNode(Child<RegisterNode>(c[0]), Child<RegisterLocalNode>(c[1]))),
        _ => throw new NotSupportedException(
            $"{node.GetType().Name} has no AST children mapping."),
    };

    /// <summary>The <typeparamref name="T"/> a mapped child must be; throws when it is missing or
    /// of another type.</summary>
    private static T Child<T>(IVisitableAstNode? child) where T : IVisitableAstNode {
        if (child is T typed) {
            return typed;
        }
        throw new InvalidOperationException("A mapped child is missing or of another type.");
    }

    /// <summary>
    /// <see cref="MapChildren"/> for mappings that can refuse a child: <paramref name="map"/> runs once
    /// per distinct child <see cref="ChildrenOf"/> reports, and the first child it answers
    /// <see langword="null"/> for makes the whole call <see langword="null"/> instead of rebuilding.
    /// A child <see cref="ChildrenOf"/> reports but <see cref="MapChildren"/> does not rebuild
    /// (<see cref="InstructionFieldNode.ResolvedNode"/>) still reaches <paramref name="map"/>: refusing
    /// it returns <see langword="null"/> even though the node itself is a leaf for the rebuild.
    /// </summary>
    /// <exception cref="NotSupportedException"><paramref name="node"/> is a type the walker does not know.</exception>
    public static IVisitableAstNode? TryMapChildren(IVisitableAstNode node,
        Func<IVisitableAstNode, IVisitableAstNode?> map) {
        Dictionary<IVisitableAstNode, IVisitableAstNode> mapped =
            new(ReferenceEqualityComparer.Instance);
        foreach (IVisitableAstNode child in ChildrenOf(node)) {
            if (mapped.ContainsKey(child)) {
                continue;
            }
            if (map(child) is not { } mappedChild) {
                return null;
            }
            mapped[child] = mappedChild;
        }
        return MapChildren(node, child => mapped[child]);
    }

    private static IVisitableAstNode Map(IVisitableAstNode node,
        Func<IVisitableAstNode, IVisitableAstNode> map, IVisitableAstNode?[] children,
        Func<IVisitableAstNode?[], IVisitableAstNode> rebuild) {
        IVisitableAstNode?[] mapped = new IVisitableAstNode?[children.Length];
        bool same = true;
        for (int i = 0; i < children.Length; i++) {
            IVisitableAstNode? child = children[i];
            mapped[i] = child == null ? null : map(child);
            same = same && ReferenceEquals(child, mapped[i]);
        }
        return same ? node : rebuild(mapped);
    }

    private static IVisitableAstNode MapList(IVisitableAstNode node,
        Func<IVisitableAstNode, IVisitableAstNode> map, IReadOnlyList<IVisitableAstNode> children,
        Func<IVisitableAstNode[], IVisitableAstNode> rebuild) {
        IVisitableAstNode[] mapped = new IVisitableAstNode[children.Count];
        bool same = true;
        for (int i = 0; i < children.Count; i++) {
            mapped[i] = map(children[i]);
            same = same && ReferenceEquals(children[i], mapped[i]);
        }
        return same ? node : rebuild(mapped);
    }

    private static IVisitableAstNode MapParts(FailAsUntestedNode fail,
        Func<IVisitableAstNode, IVisitableAstNode> map) {
        List<UntestedMessagePart>? parts = null;
        for (int i = 0; i < fail.Parts.Count; i++) {
            UntestedMessagePart part = fail.Parts[i];
            if (part.Value == null) {
                continue;
            }
            IVisitableAstNode mapped = map(part.Value);
            if (ReferenceEquals(mapped, part.Value)) {
                continue;
            }
            parts ??= [.. fail.Parts];
            parts[i] = part with { Value = (ValueNode)mapped };
        }
        return parts is null ? fail : fail with { Parts = parts };
    }

    private static IVisitableAstNode MapSwitch(SwitchNode switchNode,
        Func<IVisitableAstNode, IVisitableAstNode> map) {
        List<SwitchSection>? sections = null;
        for (int i = 0; i < switchNode.Sections.Count; i++) {
            SwitchSection section = switchNode.Sections[i];
            IVisitableAstNode body = map(section.Body);
            if (ReferenceEquals(body, section.Body)) {
                continue;
            }
            sections ??= [.. switchNode.Sections];
            sections[i] = section with { Body = (BlockNode)body };
        }
        IVisitableAstNode value = map(switchNode.Value);
        IVisitableAstNode defaultBody = map(switchNode.Default);
        if (sections is null && ReferenceEquals(value, switchNode.Value)
            && ReferenceEquals(defaultBody, switchNode.Default)) {
            return switchNode;
        }
        return switchNode with {
            Value = (ValueNode)value,
            Sections = sections ?? switchNode.Sections,
            Default = (BlockNode)defaultBody
        };
    }

}
