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
    /// <summary>The AST nodes <paramref name="terminator"/> embeds: a branch condition, a switch value or an exit transfer.</summary>
    public static IEnumerable<IVisitableAstNode> RootsOf(IrTerminator terminator) =>
        terminator switch {
            IrBranch branch => Descendants(branch.Condition),
            IrSwitch switchNode => Descendants(switchNode.Value),
            IrExit exit => Descendants(exit.Transfer),
            _ => []
        };

    /// <summary><paramref name="node"/> and every descendant, in pre-order. The walk runs on an explicit
    /// stack so deeply nested statements cannot overflow the thread's stack.</summary>
    public static IEnumerable<IVisitableAstNode> Descendants(IVisitableAstNode node) {
        yield return node;
        Stack<IEnumerator<IVisitableAstNode>> pending = new();
        pending.Push(ChildrenOf(node).GetEnumerator());
        try {
            while (pending.Count > 0) {
                IEnumerator<IVisitableAstNode> top = pending.Peek();
                if (!top.MoveNext()) {
                    pending.Pop().Dispose();
                    continue;
                }
                IVisitableAstNode child = top.Current;
                yield return child;
                pending.Push(ChildrenOf(child).GetEnumerator());
            }
        } finally {
            while (pending.Count > 0) {
                pending.Pop().Dispose();
            }
        }
    }

    /// <summary>The direct children of <paramref name="node"/>.</summary>
    /// <exception cref="NotSupportedException"><paramref name="node"/> is a type the walker does not know.</exception>
    public static IEnumerable<IVisitableAstNode> ChildrenOf(IVisitableAstNode node) => node switch {
        BinaryOperationNode binary => Yield(binary.Left, binary.Right),
        UnaryOperationNode unary => Yield(unary.Value),
        TypeConversionNode conversion => Yield(conversion.Value),
        ExplicitCastNode cast => Yield(cast.Value),
        SegmentedPointerNode pointer => Yield(pointer.Segment, pointer.DefaultSegment, pointer.Offset),
        AbsolutePointerNode pointer => Yield(pointer.AbsoluteAddress),
        SegmentedAddressNode address => Yield(address.Segment, address.Offset),
        InstructionFieldNode field => Yield(field.ResolvedNode),
        VariableDeclarationNode declaration => Yield(declaration.Initializer),
        ReturnNode returnNode => Yield(returnNode.Value),
        MethodCallNode call => call.Arguments,
        MethodCallValueNode callValue => callValue.CallNode.Arguments,
        DivisionNode division => Yield(division.Division.Dividend, division.Division.Divisor,
            division.Division.Low, division.Division.High),
        TupleSwapNode swap => Yield(swap.First, swap.Second),
        AluOperationNode aluOperation => aluOperation.Call.CallNode.Arguments,
        AluStatementNode aluStatement => aluStatement.Call.Arguments,
        ConditionalValueNode conditional => Yield(conditional.Condition, conditional.WhenTrue,
            conditional.WhenFalse),
        OpaqueNode opaque => Yield(opaque.Original),
        FailAsUntestedNode fail => fail.Parts.Select(part => part.Value).Where(value => value != null).Cast<IVisitableAstNode>(),
        BlockNode block => block.Statements,
        IfElseNode ifElse => Yield(ifElse.Condition, ifElse.TrueCase, ifElse.FalseCase),
        PreTestLoopNode preTestLoop => Yield(preTestLoop.Condition, preTestLoop.Body),
        WhileNode whileNode => Yield(whileNode.Condition, whileNode.Body),
        WhileWithExitNode whileWithExit => Yield(whileWithExit.Condition, whileWithExit.Body, whileWithExit.ExitCondition),
        DoWhileNode doWhile => Yield(doWhile.Body, doWhile.Condition),
        InfiniteLoopNode infiniteLoop => Yield(infiniteLoop.Body),
        SwitchNode switchNode => switchNode.Sections.Select(section => (IVisitableAstNode)section.Body)
            .Append(switchNode.Value).Append(switchNode.Default),
        CpuFaultTryCatchNode cpuFault => Yield(cpuFault.Body),
        InstructionBoundaryNode boundary => Yield(boundary.Body),
        InstructionNode instruction => instruction.Parameters,
        MoveIpNextNode moveIp => Yield(moveIp.NextIp),
        ConstantNode or CpuFlagNode or FlagRegisterNode or RegisterNode or VariableReferenceNode
            or InstructionPointerNode or LoadOffsetParameterNode or SegmentVariableNode
            or OffsetLiteralNode or SignatureFieldNode or CommentNode or InstructionStartNode
            or CheckExternalEventsNode or SpeculativeGuardNode or BreakNode or ContinueNode
            or GotoNode or LabelNode or EntryDispatcherLabelNode or CyclicJumpNode
            or MethodGroupNode or LocalDeclarationNode or InterruptReturnNode or SelectorNode
            or SetInterruptShadowNode or ThrowNode or CfgInstructionNode => [],
        _ => throw new NotSupportedException(
            $"{node.GetType().Name} has no AST children enumeration."),
    };

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
            c => binary with { Left = (ValueNode)c[0]!, Right = (ValueNode)c[1]! }),
        UnaryOperationNode unary => Map(node, map, [unary.Value],
            c => unary with { Value = (ValueNode)c[0]! }),
        TypeConversionNode conversion => Map(node, map, [conversion.Value],
            c => conversion with { Value = (ValueNode)c[0]! }),
        ExplicitCastNode cast => Map(node, map, [cast.Value],
            c => cast with { Value = c[0]! }),
        SegmentedPointerNode pointer => Map(node, map,
            [pointer.Segment, pointer.DefaultSegment, pointer.Offset],
            c => pointer with {
                Segment = (ValueNode)c[0]!, DefaultSegment = (ValueNode?)c[1], Offset = (ValueNode)c[2]! }),
        AbsolutePointerNode pointer => Map(node, map, [pointer.AbsoluteAddress],
            c => pointer with { AbsoluteAddress = (ValueNode)c[0]! }),
        SegmentedAddressNode address => Map(node, map, [address.Segment, address.Offset],
            c => address with { Segment = (ValueNode)c[0]!, Offset = (ValueNode)c[1]! }),
        VariableDeclarationNode declaration => Map(node, map, [declaration.Initializer],
            c => declaration with { Initializer = (ValueNode)c[0]! }),
        ReturnNode returnNode => Map(node, map, [returnNode.Value],
            c => returnNode with { Value = c[0]! }),
        MethodCallNode call => MapList(call, map, call.Arguments, call.WithArguments),
        MethodCallValueNode callValue => MapList(callValue, map, callValue.CallNode.Arguments,
            callValue.WithArguments),
        DivisionNode division => Map(node, map,
            [division.Division.Dividend, division.Division.Divisor,
                division.Division.Low, division.Division.High],
            c => division with {
                Division = division.Division with {
                    Dividend = (ValueNode)c[0]!, Divisor = (ValueNode)c[1]!,
                    Low = (RegisterNode)c[2]!, High = (RegisterNode)c[3]! } }),
        TupleSwapNode swap => Map(node, map, [swap.First, swap.Second],
            c => swap with { First = (ValueNode)c[0]!, Second = (ValueNode)c[1]! }),
        AluOperationNode aluOperation => MapList(aluOperation, map,
            aluOperation.Call.CallNode.Arguments,
            args => aluOperation with { Call = aluOperation.Call.WithArguments(args) }),
        AluStatementNode aluStatement => MapList(aluStatement, map, aluStatement.Call.Arguments,
            args => aluStatement with { Call = aluStatement.Call.WithArguments(args) }),
        ConditionalValueNode conditional => Map(node, map,
            [conditional.Condition, conditional.WhenTrue, conditional.WhenFalse],
            c => conditional with {
                Condition = (ValueNode)c[0]!, WhenTrue = (ValueNode)c[1]!, WhenFalse = (ValueNode)c[2]! }),
        OpaqueNode opaque => Map(node, map, [opaque.Original],
            c => opaque with { Original = c[0]! }),
        FailAsUntestedNode fail => MapParts(fail, map),
        BlockNode block => MapList(block, map, block.Statements,
            statements => new BlockNode(statements)),
        IfElseNode ifElse => Map(node, map, [ifElse.Condition, ifElse.TrueCase, ifElse.FalseCase],
            c => new IfElseNode((ValueNode)c[0]!, c[1]!, c[2]!)),
        PreTestLoopNode preTestLoop => Map(node, map, [preTestLoop.Condition, preTestLoop.Body],
            c => preTestLoop with { Condition = (ValueNode)c[0]!, Body = (BlockNode)c[1]! }),
        WhileNode whileNode => Map(node, map, [whileNode.Condition, whileNode.Body],
            c => new WhileNode((ValueNode)c[0]!, (BlockNode)c[1]!)),
        WhileWithExitNode whileWithExit => Map(node, map,
            [whileWithExit.Condition, whileWithExit.Body, whileWithExit.ExitCondition],
            c => whileWithExit with {
                Condition = (ValueNode)c[0]!, Body = (BlockNode)c[1]!, ExitCondition = (ValueNode)c[2]! }),
        DoWhileNode doWhile => Map(node, map, [doWhile.Body, doWhile.Condition],
            c => doWhile with { Body = (BlockNode)c[0]!, Condition = (ValueNode)c[1]! }),
        InfiniteLoopNode infiniteLoop => Map(node, map, [infiniteLoop.Body],
            c => infiniteLoop with { Body = (BlockNode)c[0]! }),
        SwitchNode switchNode => MapSwitch(switchNode, map),
        CpuFaultTryCatchNode cpuFault => Map(node, map, [cpuFault.Body],
            c => cpuFault with { Body = (BlockNode)c[0]! }),
        InstructionBoundaryNode boundary => Map(node, map, [boundary.Body],
            c => boundary with { Body = (BlockNode)c[0]! }),
        InstructionNode instruction => MapList(instruction, map, instruction.Parameters,
            parameters => instruction with { Parameters = parameters }),
        MoveIpNextNode moveIp => Map(node, map, [moveIp.NextIp],
            c => moveIp with { NextIp = c[0]! }),
        InstructionFieldNode or ConstantNode or CpuFlagNode or FlagRegisterNode or RegisterNode
            or VariableReferenceNode or InstructionPointerNode or LoadOffsetParameterNode
            or SegmentVariableNode or OffsetLiteralNode or SignatureFieldNode or CommentNode
            or InstructionStartNode or CheckExternalEventsNode or SpeculativeGuardNode or BreakNode
            or ContinueNode or GotoNode or LabelNode or EntryDispatcherLabelNode or CyclicJumpNode
            or MethodGroupNode or LocalDeclarationNode or InterruptReturnNode or SelectorNode
            or SetInterruptShadowNode or ThrowNode or CfgInstructionNode => node,
        _ => throw new NotSupportedException(
            $"{node.GetType().Name} has no AST children mapping."),
    };

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

    private static IEnumerable<IVisitableAstNode> Yield(params IVisitableAstNode?[] nodes) =>
        nodes.OfType<IVisitableAstNode>();
}
