namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
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
        new(CpuValues.None, CpuValues.None, CpuValues.None, IrTokens.Memory, IrTokens.None, false, false);
    private static readonly IrEffects WriteMemory =
        new(CpuValues.None, CpuValues.None, CpuValues.None, IrTokens.None, IrTokens.Memory, false, false);
    private static readonly IrEffects ReadWriteIo =
        new(CpuValues.None, CpuValues.None, CpuValues.None, IrTokens.Io, IrTokens.Io, false, false);
    private static readonly IrEffects ReadFpuControlWord =
        new(CpuValues.FpuControlWord, CpuValues.None, CpuValues.None, IrTokens.None, IrTokens.None, false, false);
    private static readonly IrEffects WriteFpuControlWord =
        new(CpuValues.None, CpuValues.FpuControlWord, CpuValues.None, IrTokens.None, IrTokens.None, false, false);
    private static readonly IrEffects InterruptReturnUse =
        new(CpuValues.Registers, CpuValues.None, CpuValues.None, IrTokens.All, IrTokens.None, false, false);
    private const CpuValues PopAllRegisters16 = CpuValues.Ax | CpuValues.Cx | CpuValues.Dx | CpuValues.Bx
        | CpuValues.Sp | CpuValues.Bp | CpuValues.Si | CpuValues.Di;
    private const CpuValues PopAllRegisters32 = CpuValues.Eax | CpuValues.Ecx | CpuValues.Edx | CpuValues.Ebx
        | CpuValues.Esp | CpuValues.Ebp | CpuValues.Esi | CpuValues.Edi;

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
        new(CpuValues.StatusFlags | CpuValues.Direction, CpuValues.None, CpuValues.None, IrTokens.None, IrTokens.None, false, false);

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

    public IrEffects VisitMultiplicationNode(MultiplicationNode node) {
        AluOperation operation = node.Signed ? AluOperation.Imul : AluOperation.Mul;
        if (!AluFlagTable.TryGetEffect(operation, node.Width, (ulong?)null, out FlagEffect effect)) {
            effect = FlagEffect.Unknown;
        }
        return node.Left.Accept(this).Then(node.Right.Accept(this)).Then(OfFlagEffect(effect))
            .Then(WritingRegisters(IrValueSets.Of(node.Low) | IrValueSets.Of(node.High)));
    }

    public IrEffects VisitTupleSwapNode(TupleSwapNode node) {
        CpuValues registers = SwapRegisters(node.First) | SwapRegisters(node.Second);
        return new IrEffects(registers, registers,
            CpuValues.None, IrTokens.None, IrTokens.None, false, false);
    }

    public IrEffects VisitOpaqueNode(OpaqueNode node) => node.Original.Accept(UntypedInstance).Then(IrEffects.Unknown);

    public IrEffects VisitMethodCallNode(MethodCallNode node) =>
        ArgumentsOf(node).Then(_typedCalls ? StatementCallEffect(node) : IrEffects.Unknown);

    public IrEffects VisitMethodCallValueNode(MethodCallValueNode node) =>
        ArgumentsOf(node.CallNode).Then(_typedCalls ? ValueCallEffect(node) : IrEffects.Unknown);

    private static IrEffects StatementCallEffect(MethodCallNode node) => node switch {
        ProgramInterruptCallHelperNode call => ProgramInterruptCall(call),
        StackCallNode { Operation: StackOperation.Push or StackOperation.PushAll } stack
            => Pushing(stack.Width),
        StackCallNode { Operation: StackOperation.PopAll } stack => PopAll(stack.Width),
        StackCallNode { Operation: StackOperation.Leave } stack => Leave(stack.Width),
        PortWriteCallNode => ReadWriteIo,
        BoundCheckCallNode => IrEffects.None,
        InterruptServiceCallNode service => ServiceContractEffects.Of(service),
        FpuControlCallNode => WriteFpuControlWord,
        _ => IrEffects.Unknown
    };

    private static IrEffects ValueCallEffect(MethodCallValueNode node) => node switch {
        StackCallValueNode { Operation: StackOperation.Pop } stack
            => StackAccess(StackPointerOf(stack.Width) | CpuValues.Ss, StackPointerOf(stack.Width),
                IrTokens.Memory, IrTokens.None),
        StackCallValueNode { Operation: StackOperation.Peek } stack
            => StackAccess(StackPointerOf(stack.Width) | CpuValues.Ss, CpuValues.None,
                IrTokens.Memory, IrTokens.None),
        PortReadCallNode => ReadWriteIo,
        BitScanCallNode => IrEffects.None,
        SignatureMatchesHelperNode => ReadMemory,
        FpuControlWordNode => ReadFpuControlWord,
        _ => IrEffects.Unknown
    };

    /// <summary>A direct call to a program interrupt handler: it reads the handler's uses, SP / SS /
    /// CS for the pushed frame, the pushed FLAGS image (status flags and DF) and the interrupt
    /// effect's reads for a hardware interrupt it can deliver; it may write the handler's writes,
    /// every status flag and DF (a handler can patch the stacked FLAGS image before its iret) and
    /// the effect's writes. Nothing is a must-write: an iret restores what it read.</summary>
    private static IrEffects ProgramInterruptCall(ProgramInterruptCallHelperNode node) =>
        new(node.Handler.Reads | CpuValues.Sp | CpuValues.Ss | CpuValues.Cs | CpuValues.StatusFlags
                | CpuValues.Direction | node.HardwareInterruptEffect.Reads,
            CpuValues.None,
            node.Handler.Writes | CpuValues.StatusFlags | CpuValues.Direction | node.HardwareInterruptEffect.Writes,
            IrTokens.All, IrTokens.All, false, true);

    private static IrEffects PopAll(BitWidth width) {
        CpuValues popped = width == BitWidth.DWORD_32 ? PopAllRegisters32 : PopAllRegisters16;
        return StackAccess(StackPointerOf(width) | CpuValues.Ss, popped,
            IrTokens.Memory, IrTokens.None);
    }

    private static IrEffects Leave(BitWidth width) {
        CpuValues framePointer = width == BitWidth.DWORD_32 ? CpuValues.Ebp : CpuValues.Bp;
        return StackAccess(framePointer | CpuValues.Ss, StackPointerOf(width) | framePointer,
            IrTokens.Memory, IrTokens.None);
    }

    public IrEffects VisitSpeculativeGuardNode(SpeculativeGuardNode node) => ReadMemory;

    /// <summary>A hardware interrupt handler can run at the check: it reads the effect's
    /// <see cref="CallEffect.Reads"/> and may write its
    /// <see cref="CallEffect.Writes"/>, and no status flag or DF (the iret restores
    /// FLAGS). It touches both tokens.</summary>
    public IrEffects VisitCheckExternalEventsNode(CheckExternalEventsNode node) =>
        new(node.HardwareInterruptEffect.Reads, CpuValues.None, node.HardwareInterruptEffect.Writes,
            IrTokens.All, IrTokens.All, false, true);

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
                CpuValues.None, CpuValues.StatusFlags | CpuValues.Direction, CpuValues.None,
                IrTokens.None, IrTokens.None, false, false));
        }
        if (left is FpuControlWordNode) {
            return right.Accept(this).Then(WriteFpuControlWord);
        }
        if (left is VariableReferenceNode or RegisterLocalNode) {
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
        // DefaultSegment is rendering information (the printed segment prefix); the access
        // reads the memory through Segment only.
        IrEffects effect = pointer.Segment.Accept(this);
        return effect.Then(pointer.Offset.Accept(this));
    }

    private IrEffects ArgumentsOf(MethodCallNode call) {
        IrEffects effect = IrEffects.None;
        foreach (IVisitableAstNode argument in call.Arguments) {
            effect = effect.Then(argument.Accept(this));
        }
        return effect;
    }

    private static IrEffects ReadingRegisters(CpuValues registers) =>
        new(registers, CpuValues.None, CpuValues.None, IrTokens.None, IrTokens.None, false, false);

    private static IrEffects WritingRegisters(CpuValues registers) =>
        new(CpuValues.None, registers, CpuValues.None, IrTokens.None, IrTokens.None, false, false);

    private static IrEffects ReadingFlags(ushort flagMask) =>
        new(FlagValuesOf(flagMask), CpuValues.None, CpuValues.None, IrTokens.None, IrTokens.None, false, false);

    private static IrEffects WritingFlags(ushort flagMask) =>
        new(CpuValues.None, FlagValuesOf(flagMask), CpuValues.None, IrTokens.None, IrTokens.None, false, false);

    private static CpuValues FlagValuesOf(ushort flagMask) {
        CpuValues values = IrValueSets.FromStatusFlags((StatusFlags)flagMask & StatusFlags.All);
        if ((flagMask & Flags.Direction) != 0) {
            values |= CpuValues.Direction;
        }
        return values;
    }

    private static IrEffects OfFlagEffect(FlagEffect flags) =>
        new(IrValueSets.FromStatusFlags(flags.Reads), IrValueSets.FromStatusFlags(flags.MustWrite),
            IrValueSets.FromStatusFlags(flags.MayWrite), IrTokens.None, IrTokens.None, false, false);

    private static CpuValues StackPointerOf(BitWidth width) =>
        width == BitWidth.DWORD_32 ? CpuValues.Esp : CpuValues.Sp;

    private static IrEffects StackAccess(CpuValues reads, CpuValues mustWrite, IrTokens tokenReads,
        IrTokens tokenWrites) =>
        new(reads, mustWrite, CpuValues.None, tokenReads, tokenWrites, false, false);

    private static IrEffects Pushing(BitWidth width) =>
        StackAccess(StackPointerOf(width) | CpuValues.Ss, StackPointerOf(width),
            IrTokens.None, IrTokens.Memory);

    private static CpuValues SwapRegisters(ValueNode node) =>
        node is RegisterNode register ? IrValueSets.Of(register) : CpuValues.None;

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
    public IrEffects VisitRegisterLocalNode(RegisterLocalNode node) => IrEffects.None;
    public IrEffects VisitRegisterWriteBackNode(RegisterWriteBackNode node) => Assign(node.Register, node.Local);
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
