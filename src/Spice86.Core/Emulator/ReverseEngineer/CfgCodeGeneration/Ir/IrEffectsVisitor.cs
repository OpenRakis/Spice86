namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Computes the <see cref="IrEffects"/> of one IR statement or terminator expression. Nodes that are
/// not IR statements (structured control flow, runtime control-flow nodes) throw
/// <see cref="InvalidOperationException"/>. Unknown code reads and may write everything.
/// </summary>
internal sealed class IrEffectsVisitor : ICodeGenerationAstVisitor<IrEffects> {
    private static readonly IrEffectsVisitor Instance = new(true);

    /// <summary>Treats every call as unknown: an opaque statement's calls were not typed.</summary>
    private static readonly IrEffectsVisitor UntypedInstance = new(false);
    private static readonly IrEffects ReadMemory =
        new(IrValues.None, IrValues.None, IrValues.None, IrTokens.Memory, IrTokens.None, false, false);
    private static readonly IrEffects WriteMemory =
        new(IrValues.None, IrValues.None, IrValues.None, IrTokens.None, IrTokens.Memory, false, false);
    private static readonly IrEffects ReadWriteIo =
        new(IrValues.None, IrValues.None, IrValues.None, IrTokens.Io, IrTokens.Io, false, false);
    /// <summary>The registers a hardware interrupt handler can observe and clobber: every general and
    /// segment register except SP/ESP and SS (its iret restores the stack pointer). No status flag or
    /// DF: the iret restores FLAGS.</summary>
    private const IrValues InterruptHandlerRegisters = IrValues.Eax | IrValues.Ecx
        | IrValues.Edx | IrValues.Ebx | IrValues.Ebp | IrValues.Esi
        | IrValues.Edi | IrValues.Es | IrValues.Cs | IrValues.Ds
        | IrValues.Fs | IrValues.Gs;

    private static readonly IrEffects CheckEvents =
        new(InterruptHandlerRegisters, IrValues.None,
            InterruptHandlerRegisters, IrTokens.All, IrTokens.All, false, true);
    private static readonly IrEffects InterruptReturnUse =
        new(IrValues.Registers, IrValues.None, IrValues.None, IrTokens.All, IrTokens.None, false, false);
    private const IrValues PopAllRegisters16 = IrValues.Ax | IrValues.Cx | IrValues.Dx | IrValues.Bx
        | IrValues.Sp | IrValues.Bp | IrValues.Si | IrValues.Di;
    private const IrValues PopAllRegisters32 = IrValues.Eax | IrValues.Ecx | IrValues.Edx | IrValues.Ebx
        | IrValues.Esp | IrValues.Ebp | IrValues.Esi | IrValues.Edi;

    /// <summary>The effects of <paramref name="node"/>; <paramref name="mayFault"/> sets <see cref="IrEffects.MayFault"/>.</summary>
    public static IrEffects Of(IVisitableAstNode node, bool mayFault) {
        IrEffects effect = node.Accept(Instance);
        return mayFault
            ? new IrEffects(effect.Reads, effect.MustWrite, effect.MayWrite, effect.TokenReads,
                effect.TokenWrites, true, effect.DeliversInterrupts)
            : effect;
    }

    private readonly bool _typedCalls;

    private IrEffectsVisitor(bool typedCalls) {
        _typedCalls = typedCalls;
    }

    public IrEffects VisitRegisterNode(RegisterNode node) => ReadingRegisters(IrValueSets.Of(node));

    public IrEffects VisitSegmentRegisterNode(SegmentRegisterNode node) => ReadingRegisters(IrValueSets.Of(node));

    public IrEffects VisitBinaryOperationNode(BinaryOperationNode node) {
        if (node.BinaryOperation != BinaryOperation.ASSIGN) {
            return node.Left.Accept(this).Then(node.Right.Accept(this));
        }
        return Assign(node.Left, node.Right);
    }

    public IrEffects VisitUnaryOperationNode(UnaryOperationNode node) => node.Value.Accept(this);

    public IrEffects VisitConditionalValueNode(ConditionalValueNode node) =>
        node.Condition.Accept(this).Then(node.WhenTrue.Accept(this)).Then(node.WhenFalse.Accept(this));

    public IrEffects VisitTypeConversionNode(TypeConversionNode node) => node.Value.Accept(this);

    public IrEffects VisitExplicitCastNode(ExplicitCastNode node) => node.Value.Accept(this);

    public IrEffects VisitVariableDeclarationNode(VariableDeclarationNode node) => node.Initializer.Accept(this);

    public IrEffects VisitInstructionFieldNode(InstructionFieldNode node) => node.ResolvedNode.Accept(this);

    public IrEffects VisitSegmentedPointer(SegmentedPointerNode node) =>
        AddressPartsOfSegmented(node).Then(ReadMemory);

    public IrEffects VisitAbsolutePointerNode(AbsolutePointerNode node) =>
        node.AbsoluteAddress.Accept(this).Then(ReadMemory);

    public IrEffects VisitSegmentedAddressNode(SegmentedAddressNode node) =>
        node.Segment.Accept(this).Then(node.Offset.Accept(this));

    public IrEffects VisitCpuFlagNode(CpuFlagNode node) => ReadingFlags(node.FlagMask);

    public IrEffects VisitFlagRegisterNode(FlagRegisterNode node) =>
        new(IrValues.StatusFlags | IrValues.Direction, IrValues.None, IrValues.None, IrTokens.None, IrTokens.None, false, false);

    public IrEffects VisitConstantNode(ConstantNode node) => IrEffects.None;

    public IrEffects VisitNearAddressNode(NearAddressNode node) => IrEffects.None;

    public IrEffects VisitVariableReferenceNode(VariableReferenceNode node) => IrEffects.None;

    public IrEffects VisitSegmentVariableNode(SegmentVariableNode node) => IrEffects.None;

    public IrEffects VisitOffsetLiteralNode(OffsetLiteralNode node) => IrEffects.None;

    public IrEffects VisitInstructionPointerNode(InstructionPointerNode node) => IrEffects.None;

    public IrEffects VisitLoadOffsetParameterNode(LoadOffsetParameterNode node) => IrEffects.None;

    public IrEffects VisitMethodGroupNode(MethodGroupNode node) => IrEffects.None;

    public IrEffects VisitSegmentedAddressListNode(SegmentedAddressListNode node) => IrEffects.None;

    public IrEffects VisitSignatureFieldNode(SignatureFieldNode node) => IrEffects.None;

    public IrEffects VisitCommentNode(CommentNode node) => IrEffects.None;

    public IrEffects VisitInstructionStartNode(InstructionStartNode node) => IrEffects.None;

    public IrEffects VisitAluOperationNode(AluOperationNode node) =>
        ArgumentsOf(node.Call.CallNode).Then(OfFlagEffect(node.Flags));

    public IrEffects VisitAluStatementNode(AluStatementNode node) =>
        ArgumentsOf(node.Call).Then(OfFlagEffect(node.Flags));

    public IrEffects VisitDivisionNode(DivisionNode node) =>
        node.Division.Dividend.Accept(this).Then(node.Division.Divisor.Accept(this)).Then(WritingRegisters(
            IrValueSets.Of(node.Division.Low) | IrValueSets.Of(node.Division.High)));

    public IrEffects VisitTupleSwapNode(TupleSwapNode node) {
        IrValues registers = SwapRegisters(node.First) | SwapRegisters(node.Second);
        return new IrEffects(registers, registers,
            IrValues.None, IrTokens.None, IrTokens.None, false, false);
    }

    public IrEffects VisitOpaqueNode(OpaqueNode node) => node.Original.Accept(UntypedInstance).Then(IrEffects.Unknown);

    public IrEffects VisitMethodCallNode(MethodCallNode node) =>
        ArgumentsOf(node).Then(_typedCalls ? StatementCallEffect(node) : IrEffects.Unknown);

    public IrEffects VisitMethodCallValueNode(MethodCallValueNode node) =>
        ArgumentsOf(node.CallNode).Then(_typedCalls ? ValueCallEffect(node) : IrEffects.Unknown);

    private static IrEffects StatementCallEffect(MethodCallNode node) => node switch {
        StackCallNode { Operation: StackOperation.Push or StackOperation.PushAll } stack
            => Pushing(stack.Width),
        StackCallNode { Operation: StackOperation.PopAll } stack => PopAll(stack.Width),
        StackCallNode { Operation: StackOperation.Leave } stack => Leave(stack.Width),
        PortWriteCallNode => ReadWriteIo,
        BoundCheckCallNode => IrEffects.None,
        _ => IrEffects.Unknown
    };

    private static IrEffects ValueCallEffect(MethodCallValueNode node) => node switch {
        StackCallValueNode { Operation: StackOperation.Pop } stack
            => StackAccess(StackPointerOf(stack.Width) | IrValues.Ss, StackPointerOf(stack.Width),
                IrTokens.Memory, IrTokens.None),
        StackCallValueNode { Operation: StackOperation.Peek } stack
            => StackAccess(StackPointerOf(stack.Width) | IrValues.Ss, IrValues.None,
                IrTokens.Memory, IrTokens.None),
        PortReadCallNode => ReadWriteIo,
        BitScanCallNode => IrEffects.None,
        SignatureMatchesHelperNode => ReadMemory,
        _ => IrEffects.Unknown
    };

    private static IrEffects PopAll(BitWidth width) {
        IrValues popped = width == BitWidth.DWORD_32 ? PopAllRegisters32 : PopAllRegisters16;
        return StackAccess(StackPointerOf(width) | IrValues.Ss, popped,
            IrTokens.Memory, IrTokens.None);
    }

    private static IrEffects Leave(BitWidth width) {
        IrValues framePointer = width == BitWidth.DWORD_32 ? IrValues.Ebp : IrValues.Bp;
        return StackAccess(framePointer | IrValues.Ss, StackPointerOf(width) | framePointer,
            IrTokens.Memory, IrTokens.None);
    }

    public IrEffects VisitSpeculativeGuardNode(SpeculativeGuardNode node) => ReadMemory;

    /// <summary>A hardware interrupt handler can run at the check: it reads and may write every general
    /// and segment register except SP/ESP and SS (its iret restores the stack pointer), and no status
    /// flag or DF (the iret restores FLAGS). It touches both tokens.</summary>
    public IrEffects VisitCheckExternalEventsNode(CheckExternalEventsNode node) => CheckEvents;

    public IrEffects VisitReturnNode(ReturnNode node) => node.Value.Accept(this).Then(IrEffects.ExitUse);

    public IrEffects VisitFailAsUntestedNode(FailAsUntestedNode node) {
        IrEffects effect = IrEffects.None;
        foreach (UntestedMessagePart part in node.Parts) {
            if (part.Value is not { } value) {
                continue;
            }
            effect = effect.Then(value.Accept(this));
        }
        return effect;
    }

    public IrEffects VisitThrowNode(ThrowNode node) => IrEffects.ExitUse;

    /// <summary>The shadow write touches no machine value or token: the interpreter consumes it.</summary>
    public IrEffects VisitSetInterruptShadowNode(SetInterruptShadowNode node) => IrEffects.None;

    /// <summary>An interrupt return restores FLAGS from the stack: it uses every register, segment
    /// register and both tokens, but not the status flags or the direction flag.</summary>
    public IrEffects VisitInterruptReturnNode(InterruptReturnNode node) => InterruptReturnUse;

    public IrEffects VisitCyclicJumpNode(CyclicJumpNode node) => IrEffects.ExitUse;

    private IrEffects Assign(ValueNode left, IVisitableAstNode right) {
        if (left is RegisterNode or SegmentRegisterNode) {
            return right.Accept(this).Then(WritingRegisters(IrValueSets.Of((RegisterNode)left)));
        }
        if (left is SegmentedPointerNode or AbsolutePointerNode) {
            return AddressPartsOf(left).Then(right.Accept(this)).Then(WriteMemory);
        }
        if (left is CpuFlagNode flag) {
            return right.Accept(this).Then(WritingFlags(flag.FlagMask));
        }
        if (left is FlagRegisterNode) {
            return right.Accept(this).Then(new IrEffects(
                IrValues.None, IrValues.StatusFlags | IrValues.Direction, IrValues.None,
                IrTokens.None, IrTokens.None, false, false));
        }
        if (left is VariableReferenceNode) {
            return right.Accept(this);
        }
        return left.Accept(this).Then(right.Accept(this));
    }

    private IrEffects AddressPartsOf(ValueNode pointer) => pointer switch {
        SegmentedPointerNode segmented => AddressPartsOfSegmented(segmented),
        AbsolutePointerNode absolute => absolute.AbsoluteAddress.Accept(this),
        _ => IrEffects.None
    };

    private IrEffects AddressPartsOfSegmented(SegmentedPointerNode pointer) {
        IrEffects effect = pointer.Segment.Accept(this);
        if (pointer.DefaultSegment != null) {
            effect = effect.Then(pointer.DefaultSegment.Accept(this));
        }
        return effect.Then(pointer.Offset.Accept(this));
    }

    private IrEffects ArgumentsOf(MethodCallNode call) {
        IrEffects effect = IrEffects.None;
        foreach (IVisitableAstNode argument in call.Arguments) {
            effect = effect.Then(argument.Accept(this));
        }
        return effect;
    }

    private static IrEffects ReadingRegisters(IrValues registers) =>
        new(registers, IrValues.None, IrValues.None, IrTokens.None, IrTokens.None, false, false);

    private static IrEffects WritingRegisters(IrValues registers) =>
        new(IrValues.None, registers, IrValues.None, IrTokens.None, IrTokens.None, false, false);

    private static IrEffects ReadingFlags(ushort flagMask) =>
        new(FlagValuesOf(flagMask), IrValues.None, IrValues.None, IrTokens.None, IrTokens.None, false, false);

    private static IrEffects WritingFlags(ushort flagMask) =>
        new(IrValues.None, FlagValuesOf(flagMask), IrValues.None, IrTokens.None, IrTokens.None, false, false);

    private static IrValues FlagValuesOf(ushort flagMask) {
        IrValues values = IrValueSets.FromStatusFlags((StatusFlags)flagMask & StatusFlags.All);
        if ((flagMask & Flags.Direction) != 0) {
            values |= IrValues.Direction;
        }
        return values;
    }

    private static IrEffects OfFlagEffect(FlagEffect flags) =>
        new(IrValueSets.FromStatusFlags(flags.Reads), IrValueSets.FromStatusFlags(flags.MustWrite),
            IrValueSets.FromStatusFlags(flags.MayWrite), IrTokens.None, IrTokens.None, false, false);

    private static IrValues StackPointerOf(BitWidth width) =>
        width == BitWidth.DWORD_32 ? IrValues.Esp : IrValues.Sp;

    private static IrEffects StackAccess(IrValues reads, IrValues mustWrite, IrTokens tokenReads,
        IrTokens tokenWrites) =>
        new(reads, mustWrite, IrValues.None, tokenReads, tokenWrites, false, false);

    private static IrEffects Pushing(BitWidth width) =>
        StackAccess(StackPointerOf(width) | IrValues.Ss, StackPointerOf(width),
            IrTokens.None, IrTokens.Memory);

    private static IrValues SwapRegisters(ValueNode node) =>
        node is RegisterNode register ? IrValueSets.Of(register) : IrValues.None;

    private static InvalidOperationException NotAnIrStatement(IVisitableAstNode node) =>
        new($"{node.GetType().Name} is not an IR statement.");

    public IrEffects VisitBlockNode(BlockNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitIfElseNode(IfElseNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitWhileNode(WhileNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitWhileWithExitNode(WhileWithExitNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitGotoNode(GotoNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitLabelNode(LabelNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitEntryDispatcherLabelNode(EntryDispatcherLabelNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitBreakNode(BreakNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitContinueNode(ContinueNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitSwitchNode(SwitchNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitInfiniteLoopNode(InfiniteLoopNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitDoWhileNode(DoWhileNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitPreTestLoopNode(PreTestLoopNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitLocalDeclarationNode(LocalDeclarationNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitCpuFaultTryCatchNode(CpuFaultTryCatchNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitInstructionBoundaryNode(InstructionBoundaryNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitInstructionNode(InstructionNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitJumpNearNode(JumpNearNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitJumpFarNode(JumpFarNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitCallNearNode(CallNearNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitCallFarNode(CallFarNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitMoveIpNextNode(MoveIpNextNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitHltNode(HltNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitSelectorNode(SelectorNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitInvalidInstructionNode(InvalidInstructionNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitCpuidNode(CpuidNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitInterruptCallNode(InterruptCallNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitReturnNearNode(ReturnNearNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitReturnFarNode(ReturnFarNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitReturnInterruptNode(ReturnInterruptNode node) => throw NotAnIrStatement(node);
    public IrEffects VisitCallbackNode(CallbackNode node) => throw NotAnIrStatement(node);
}
