namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary>
/// The flag read census classification on hand-built IR methods: a read sees an ALU, flag-register,
/// plain, join or entry setter at adjacent, same-block or other-block distance, and the blocker is
/// may-write, memory operand, operand changed or propagatable.
/// </summary>
public sealed class IrFlagReadCensusClassifierTest {
    private static readonly IrValues Eax = IrSsaValues.MaskOf(IrSsaValue.Eax);
    private static readonly IrValues Zero = IrSsaValues.MaskOf(IrSsaValue.Zero);

    [Fact]
    public void ReadRightAfterAnAluSetterIsAdjacentAndPropagatable() {
        // Arrange
        (IrMethod method, BlockNode body) = MethodWithFlagRead(
            AluSetter(new IrEffects(IrValues.None, Zero, IrValues.None, IrTokens.None,
                IrTokens.None, false, false)));

        // Act
        FlagReadCensus census = Classify(method, body);

        // Assert
        census.Total.Should().Be(1);
        census.Count(FlagSetterKind.AluSetter).Should().Be(1);
        census.Count(FlagSetterDistance.AdjacentSetter).Should().Be(1);
        census.Count(FlagPropagationBlocker.Propagatable).Should().Be(1);
    }

    [Fact]
    public void SetterEarlierInTheBlockIsSameBlock() {
        // Arrange
        (IrMethod method, BlockNode body) = MethodWithFlagRead(
            AluSetter(new IrEffects(IrValues.None, Zero, IrValues.None, IrTokens.None,
                IrTokens.None, false, false)),
            new IrStatement(new CommentNode("filler"), Instruction(1),
                new IrEffects(IrValues.None, IrValues.None, IrValues.None, IrTokens.None,
                    IrTokens.None, false, false)));

        // Act
        FlagReadCensus census = Classify(method, body);

        // Assert
        census.Count(FlagSetterKind.AluSetter).Should().Be(1);
        census.Count(FlagSetterDistance.SameBlockSetter).Should().Be(1);
    }

    [Fact]
    public void ASetterReadingMemoryIsMemoryOperand() {
        // Arrange
        BinaryOperationNode read = new(DataType.UINT8,
            new RegisterNode(DataType.UINT8, 0), BinaryOperation.ASSIGN,
            new AbsolutePointerNode(DataType.UINT8, new ConstantNode(DataType.UINT16, 0)));
        IrStatement setter = new(read, Instruction(0),
            new IrEffects(IrValues.None, Zero, IrValues.None, IrTokens.None, IrTokens.None,
                false, false));
        (IrMethod method, BlockNode body) = MethodWithFlagRead(setter);

        // Act
        FlagReadCensus census = Classify(method, body);

        // Assert
        census.Count(FlagSetterKind.PlainSetter).Should().Be(1);
        census.Count(FlagPropagationBlocker.MemoryOperand).Should().Be(1);
    }

    [Fact]
    public void ASetterOperandChangedBetweenIsOperandChanged() {
        // Arrange: the setter reads EAX; a statement before the read writes EAX
        (IrMethod method, BlockNode body) = MethodWithFlagRead(
            AluSetter(new IrEffects(Eax, Zero, IrValues.None, IrTokens.None, IrTokens.None,
                false, false)),
            new IrStatement(new CommentNode("mov"), Instruction(1),
                new IrEffects(IrValues.None, Eax, IrValues.None, IrTokens.None, IrTokens.None,
                    false, false)));

        // Act
        FlagReadCensus census = Classify(method, body);

        // Assert
        census.Count(FlagPropagationBlocker.OperandChanged).Should().Be(1);
    }

    [Fact]
    public void APartialFlagDefinitionIsMayWrite() {
        // Arrange: the setter may-write Zero (a shift by CL keeps the flag when CL is 0)
        (IrMethod method, BlockNode body) = MethodWithFlagRead(
            AluSetter(new IrEffects(Zero, IrValues.None, Zero, IrTokens.None, IrTokens.None,
                false, false)));

        // Act
        FlagReadCensus census = Classify(method, body);

        // Assert
        census.Count(FlagPropagationBlocker.MayWrite).Should().Be(1);
    }

    [Fact]
    public void AFlagRegisterSetterIsFlagRegisterSetter() {
        // Arrange: a sahf-like assignment to the flag register
        BinaryOperationNode assign = new(DataType.UINT16,
            new FlagRegisterNode(DataType.UINT16), BinaryOperation.ASSIGN,
            new ConstantNode(DataType.UINT16, 0));
        IrStatement setter = new(assign, Instruction(0),
            new IrEffects(IrValues.None, IrValues.StatusFlags, IrValues.None, IrTokens.None,
                IrTokens.None, false, false));
        (IrMethod method, BlockNode body) = MethodWithFlagRead(setter);

        // Act
        FlagReadCensus census = Classify(method, body);

        // Assert
        census.Count(FlagSetterKind.FlagRegisterSetter).Should().Be(1);
    }

    [Fact]
    public void ASetterInAnotherBlockIsOtherBlock() {
        // Arrange: entry writes Zero then jumps to a block reading it
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock readBlock = Block(1);
        entry.Statements.Add(AluSetter(new IrEffects(IrValues.None, Zero, IrValues.None,
            IrTokens.None, IrTokens.None, false, false), instruction));
        entry.Terminator = new IrJump(instruction, new IrEdge(readBlock, false));
        BinaryOperationNode read = FlagRead();
        readBlock.Statements.Add(IrStatement.Create(read, instruction));
        readBlock.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, entry)], [instruction], entry, readBlock);

        // Act
        FlagReadCensus census = Classify(method, new BlockNode(read));

        // Assert
        census.Count(FlagSetterKind.AluSetter).Should().Be(1);
        census.Count(FlagSetterDistance.OtherBlockSetter).Should().Be(1);
    }

    [Fact]
    public void AReadWithNoSetterIsEntrySetter() {
        // Arrange
        (IrMethod method, BlockNode body) = MethodWithFlagRead();

        // Act
        FlagReadCensus census = Classify(method, body);

        // Assert
        census.Count(FlagSetterKind.EntrySetter).Should().Be(1);
        census.Count(FlagSetterDistance.NoSingleSetter).Should().Be(1);
    }

    [Fact]
    public void AReadAfterAJoinIsJoinSetter() {
        // Arrange: both arms write Zero, the join reads it
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock trueArm = Block(1);
        IrBlock falseArm = Block(2);
        IrBlock join = Block(3);
        entry.Terminator = new IrBranch(instruction, new CpuFlagNode(Flags.Carry),
            new IrEdge(trueArm, false), new IrEdge(falseArm, false));
        trueArm.Statements.Add(AluSetter(new IrEffects(IrValues.None, Zero, IrValues.None,
            IrTokens.None, IrTokens.None, false, false), instruction));
        falseArm.Statements.Add(AluSetter(new IrEffects(IrValues.None, Zero, IrValues.None,
            IrTokens.None, IrTokens.None, false, false), instruction));
        trueArm.Terminator = new IrJump(instruction, new IrEdge(join, false));
        falseArm.Terminator = new IrJump(instruction, new IrEdge(join, false));
        BinaryOperationNode read = FlagRead();
        join.Statements.Add(IrStatement.Create(read, instruction));
        join.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, entry)], [instruction],
            entry, trueArm, falseArm, join);

        // Act
        FlagReadCensus census = Classify(method, new BlockNode(read));

        // Assert
        census.Count(FlagSetterKind.JoinSetter).Should().Be(1);
        census.Count(FlagSetterDistance.NoSingleSetter).Should().Be(1);
    }

    /// <summary>A straight-line method: <paramref name="setters"/>, then a statement reading the
    /// Zero flag; the body holds the flag-reading node so the census sees the read.</summary>
    private static (IrMethod, BlockNode) MethodWithFlagRead(params IrStatement[] setters) {
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.AddRange(setters);
        BinaryOperationNode read = FlagRead();
        block.Statements.Add(IrStatement.Create(read, instruction));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        return (method, new BlockNode(read));
    }

    private static FlagReadCensus Classify(IrMethod method, BlockNode body) {
        IrSsa ssa = IrSsa.Build(method);
        return IrFlagReadCensusClassifier.Classify(method, ssa, body);
    }

    private static BinaryOperationNode FlagRead() =>
        new(DataType.UINT8, new RegisterNode(DataType.UINT8, (int)RegisterIndex.AxIndex),
            BinaryOperation.ASSIGN, new CpuFlagNode(Flags.Zero));

    private static IrStatement AluSetter(IrEffects effects) => AluSetter(effects, Instruction(0));

    private static IrStatement AluSetter(IrEffects effects, ICfgNode origin) =>
        new(new AluStatementNode(
                new MethodCallNode("Alu16", "Sub",
                    new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex),
                    new ConstantNode(DataType.UINT16, 1)),
                FlagEffect.None, AluOperation.Sub, BitWidth.WORD_16),
            origin, effects);

    private static IrMethod Method(IrEntry[] entries, ICfgNode[] nodes, params IrBlock[] blocks) =>
        new("M", [.. blocks], entries, new HashSet<ICfgNode>(nodes),
            new Dictionary<ICfgNode, IrInstructionShape>());

    private static IrEntry Entry(ICfgNode node, IrBlock block) => new(node, block);

    private static IrBlock Block(int id) => new(id);

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));

    private static ReturnNode ReturnValue() => new(new ConstantNode(DataType.UINT16, 0));
}
