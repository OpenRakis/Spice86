namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary>
/// A well-formed method verifies; each verifier rule gets a smallest method breaking only it.
/// </summary>
public sealed class IrVerifierTest {
    [Fact]
    public void WellFormedMethod_Verifies() {
        // Arrange
        IrMethod method = WellFormedMethod();

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void DuplicateBlockId_FailsWellFormed() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock first = StartingBlock(0, instruction);
        first.Terminator = new IrExit(instruction, ReturnValue());
        IrBlock second = StartingBlock(0, instruction);
        second.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method(entries: [Entry(instruction, first)], nodes: [instruction], first, second);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*well formed*");
    }

    [Fact]
    public void MissingTerminator_FailsWellFormed() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = StartingBlock(0, instruction);
        IrMethod method = Method(entries: [Entry(instruction, block)], nodes: [instruction], block);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*well formed*");
    }

    [Fact]
    public void EdgeTargetOutsideMethod_FailsWellFormed() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = StartingBlock(0, instruction);
        block.Terminator = new IrJump(instruction, new IrEdge(new IrBlock(9), false));
        IrMethod method = Method(entries: [Entry(instruction, block)], nodes: [instruction], block);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*well formed*");
    }

    [Fact]
    public void ForbiddenStatementNode_FailsWellFormed() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = StartingBlock(0, instruction);
        block.Statements.Add(new IrStatement(new BreakNode(), instruction, IrEffects.None));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method(entries: [Entry(instruction, block)], nodes: [instruction], block);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*well formed*");
    }

    [Fact]
    public void StoredEffectsDifferingFromComputed_FailsStoredEffects() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = StartingBlock(0, instruction);
        block.Statements.Add(new IrStatement(new CommentNode("wrong"), instruction, IrEffects.Unknown));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method(entries: [Entry(instruction, block)], nodes: [instruction], block);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*stored effects*");
    }

    [Fact]
    public void InsideEdgeToOtherOrigin_FailsInstructionEdges() {
        // Arrange
        CfgInstruction first = Instruction(0);
        CfgInstruction second = Instruction(1);
        IrBlock block = StartingBlock(0, first);
        IrBlock continuation = new(1);
        continuation.Terminator = new IrExit(second, ReturnValue());
        block.Terminator = new IrJump(first, new IrEdge(continuation, true));
        IrMethod method = Method(entries: [Entry(first, block)], nodes: [first, second],
            block, continuation);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*instruction edges*");
    }

    [Fact]
    public void InstructionEdgeToNonStartingBlock_FailsInstructionEdges() {
        // Arrange
        CfgInstruction first = Instruction(0);
        CfgInstruction second = Instruction(1);
        first.Successors.Add(second);
        IrBlock block = StartingBlock(0, first);
        IrBlock target = new(1);
        target.Statements.Add(Statement(new CommentNode("mid"), second));
        target.Terminator = new IrExit(second, ReturnValue());
        block.Terminator = new IrJump(first, new IrEdge(target, false));
        IrMethod method = Method(entries: [Entry(first, block)], nodes: [first, second], block, target);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*instruction edges*");
    }

    [Fact]
    public void InstructionEdgeToNonSuccessor_FailsInstructionEdges() {
        // Arrange
        CfgInstruction first = Instruction(0);
        CfgInstruction second = Instruction(1);
        IrBlock block = StartingBlock(0, first);
        IrBlock target = StartingBlock(1, second);
        target.Terminator = new IrExit(second, ReturnValue());
        block.Terminator = new IrJump(first, new IrEdge(target, false));
        IrMethod method = Method(entries: [Entry(first, block)], nodes: [first, second], block, target);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*instruction edges*");
    }

    [Fact]
    public void MisplacedEventCheck_FailsEventChecks() {
        // Arrange
        CfgInstruction first = Instruction(0);
        CfgInstruction second = Instruction(1);
        IrBlock entry = StartingBlock(0, first);
        entry.Terminator = new IrExit(first, ReturnValue());
        IrBlock misplaced = new(1);
        misplaced.Statements.Add(Statement(new CheckExternalEventsNode(second.Address), second));
        misplaced.Statements.Add(Statement(new CommentNode("after"), second));
        misplaced.Terminator = new IrExit(second, ReturnValue());
        IrMethod method = Method(entries: [Entry(first, entry)], nodes: [first, second],
            entry, misplaced);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*event checks*");
    }

    [Fact]
    public void FaultHandlerInsideMethod_FailsFaults() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        CfgInstruction handler = Instruction(1);
        instruction.SuccessorsPerType[InstructionSuccessorType.CpuFault] = new HashSet<ICfgNode> { handler };
        IrBlock block = StartingBlock(0, instruction);
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method(entries: [Entry(instruction, block)], nodes: [instruction, handler], block);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*faults*");
    }

    [Fact]
    public void UndeclaredTemporary_FailsTemporaries() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = StartingBlock(0, instruction);
        block.Statements.Add(Statement(new VariableReferenceNode(DataType.UINT16, "tmp"), instruction));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method(entries: [Entry(instruction, block)], nodes: [instruction], block);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*temporary*no declaration*");
    }

    [Fact]
    public void TwiceDeclaredTemporary_FailsTemporaries() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = StartingBlock(0, instruction);
        block.Statements.Add(Statement(Declaration("tmp"), instruction));
        block.Statements.Add(Statement(Declaration("tmp"), instruction));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method(entries: [Entry(instruction, block)], nodes: [instruction], block);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*temporary*more than once*");
    }

    [Fact]
    public void TemporaryReadBeforeDeclaration_FailsTemporaries() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = StartingBlock(0, instruction);
        block.Statements.Add(Statement(new VariableReferenceNode(DataType.UINT16, "tmp"), instruction));
        block.Statements.Add(Statement(Declaration("tmp"), instruction));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method(entries: [Entry(instruction, block)], nodes: [instruction], block);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*temporary*read before*");
    }

    [Fact]
    public void MethodScopedTemporaryRead_Verifies() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = StartingBlock(0, instruction);
        block.Statements.Add(Statement(new MethodScopedVariableReferenceNode(DataType.UINT16, "tmp"), instruction));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method(entries: [Entry(instruction, block)], nodes: [instruction], block);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void EntryNotStartingInstruction_FailsEntries() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = new(0);
        block.Statements.Add(Statement(new CommentNode("mid"), instruction));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method(entries: [Entry(instruction, block)], nodes: [instruction], block);

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*entries*");
    }

    [Fact]
    public void OpaqueStatement_FailsVerification() {
        // Arrange
        IrMethod method = WellFormedMethod();
        CfgInstruction origin = Instruction(9);
        method.Blocks[0].Statements.Add(Statement(
            new OpaqueNode(new CommentNode("untyped")), origin));

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*opaque*");
    }

    /// <summary>
    /// Two instructions: the first lowers to a start block, an inner branch and a join; the second block
    /// starts with an event check and ends on an exit.
    /// </summary>
    [Fact]
    public void ConditionalValueWithPureArms_Verifies() {
        // Arrange: AX = (CarryFlag ? -2 : 2) - the direction-step shape.
        IrMethod method = WellFormedMethod();
        CfgInstruction origin = Instruction(9);
        method.Blocks[0].Statements.Add(Statement(new BinaryOperationNode(DataType.UINT16,
            new RegisterNode(DataType.UINT16, 0), BinaryOperation.ASSIGN,
            new ConditionalValueNode(DataType.INT16, new CpuFlagNode(Flags.Direction),
                new ConstantNode(DataType.INT16, unchecked((ulong)(ushort)-2)),
                new ConstantNode(DataType.INT16, 2))), origin));

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ConditionalValueWithAMemoryReadArm_FailsConditionalArms() {
        // Arrange: AX = (CarryFlag ? [DS:0] : 0) - a select over a memory read is not pure.
        IrMethod method = WellFormedMethod();
        CfgInstruction origin = Instruction(9);
        method.Blocks[0].Statements.Add(Statement(new BinaryOperationNode(DataType.UINT16,
            new RegisterNode(DataType.UINT16, 0), BinaryOperation.ASSIGN,
            new ConditionalValueNode(DataType.UINT16, new CpuFlagNode(Flags.Carry),
                new SegmentedPointerNode(DataType.UINT16,
                    new SegmentRegisterNode(0), null,
                    new ConstantNode(DataType.UINT16, 0)),
                new ConstantNode(DataType.UINT16, 0))), origin));

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*conditional arms*");
    }

    [Theory]
    [InlineData(BinaryOperation.ASSIGN)]
    [InlineData(BinaryOperation.DIVIDE)]
    [InlineData(BinaryOperation.MODULO)]
    public void ConditionalValueWithAnImpureOperatorArm_FailsConditionalArms(BinaryOperation operation) {
        // Arrange: an arm that writes (ASSIGN) or can fault (DIVIDE, MODULO) is not pure.
        IrMethod method = WellFormedMethod();
        CfgInstruction origin = Instruction(9);
        method.Blocks[0].Statements.Add(Statement(new BinaryOperationNode(DataType.UINT16,
            new RegisterNode(DataType.UINT16, 0), BinaryOperation.ASSIGN,
            new ConditionalValueNode(DataType.UINT16, new CpuFlagNode(Flags.Carry),
                new BinaryOperationNode(DataType.UINT16,
                    new RegisterNode(DataType.UINT16, 0), operation,
                    new ConstantNode(DataType.UINT16, 1)),
                new ConstantNode(DataType.UINT16, 0))), origin));

        // Act
        Action act = () => IrVerifier.Verify(method);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*conditional arms*");
    }

    private static IrMethod WellFormedMethod() {
        CfgInstruction first = Instruction(0);
        CfgInstruction second = Instruction(1);
        first.Successors.Add(second);

        IrBlock entry = StartingBlock(0, first);
        IrBlock whenTrue = new(1);
        IrBlock whenFalse = new(2);
        IrBlock join = new(3);
        IrBlock secondBlock = StartingBlock(4, second);
        secondBlock.Statements.Insert(0, Statement(new CheckExternalEventsNode(second.Address), second));

        entry.Terminator = new IrBranch(first, new CpuFlagNode(Flags.Carry),
            new IrEdge(whenTrue, true), new IrEdge(whenFalse, true));
        whenTrue.Terminator = new IrJump(first, new IrEdge(join, true));
        whenFalse.Terminator = new IrJump(first, new IrEdge(join, true));
        join.Terminator = new IrJump(first, new IrEdge(secondBlock, false));
        secondBlock.Terminator = new IrExit(second, ReturnValue());

        return Method(entries: [Entry(first, entry)], nodes: [first, second],
            entry, whenTrue, whenFalse, join, secondBlock);
    }

    private static IrMethod Method(IrEntry[] entries, ICfgNode[] nodes, params IrBlock[] blocks) =>
        new("M", [.. blocks], entries, new HashSet<ICfgNode>(nodes),
            new Dictionary<ICfgNode, IrInstructionShape>());

    private static IrEntry Entry(ICfgNode node, IrBlock block) => new(node, block);

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));

    private static IrStatement Statement(IVisitableAstNode node, ICfgNode origin) =>
        IrStatement.Create(node, origin);

    private static IrBlock StartingBlock(int id, ICfgNode origin) {
        IrBlock block = new(id);
        block.Statements.Add(Statement(new InstructionStartNode("nop"), origin));
        return block;
    }

    private static VariableDeclarationNode Declaration(string name) =>
        new(DataType.UINT16, name, new ConstantNode(DataType.UINT16, 0));

    private static ReturnNode ReturnValue() => new(new ConstantNode(DataType.UINT16, 0));
}
