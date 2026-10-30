namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Spice86.Tests.CfgCpu;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The IR expression propagation on real parsed instructions: a single-use register write folds into
/// its reader, while a redefined operand between writer and reader, a second reader, a store or port
/// access crossing a moved memory read, a faulting instruction between them, a segment-register writer
/// and a reader whose store address reuses the merged register all refuse.
/// </summary>
public sealed class IrExpressionPropagationTest {
    private static readonly int Bx = (int)RegisterIndex.BxIndex;
    private static readonly int Cx = (int)RegisterIndex.CxIndex;
    private static readonly int Es = (int)SegmentRegisterIndex.EsIndex;

    private static IrMethod Propagated(params CfgBlock[] blocks) {
        IrMethod method = Lift(blocks);
        IrPassPipeline.Run(method, IrPass.ExpressionPropagation, verify: true);
        return method;
    }

    private static BinaryOperationNode? AssignmentTo(IrStatement statement, int registerIndex) =>
        statement.Node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: RegisterNode { RegisterIndex: int index }
        } assignment && index == registerIndex
            ? assignment
            : null;

    private static bool IsMergedShiftAssign(BinaryOperationNode assignment) =>
        assignment.Right is BinaryOperationNode {
            BinaryOperation: BinaryOperation.RIGHT_SHIFT,
            Left: BinaryOperationNode { BinaryOperation: BinaryOperation.PLUS }
        };

    private static bool IsUnmergedShiftAssign(BinaryOperationNode assignment) =>
        assignment.Right is BinaryOperationNode {
            BinaryOperation: BinaryOperation.RIGHT_SHIFT,
            Left: RegisterNode
        };

    private static bool IsAddOfRegister(BinaryOperationNode assignment, int registerIndex) =>
        assignment.Right is BinaryOperationNode {
            BinaryOperation: BinaryOperation.PLUS,
            Right: RegisterNode { RegisterIndex: int index }
        } && index == registerIndex;

    private static bool IsMemoryReadAssign(BinaryOperationNode assignment) =>
        assignment.Right is SegmentedPointerNode;

    private static bool IsRegisterReadAssign(BinaryOperationNode assignment) =>
        assignment.Right is RegisterNode;

    private static bool ReadsRegister(BinaryOperationNode assignment, int registerIndex) =>
        assignment.Right is RegisterNode { RegisterIndex: int index } && index == registerIndex;

    private static bool IsStoreAssign(IVisitableAstNode node) =>
        node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: SegmentedPointerNode
        };

    private static bool IsAddAssign(BinaryOperationNode assignment) =>
        assignment.Right is BinaryOperationNode { BinaryOperation: BinaryOperation.PLUS };

    private static bool IsNestedAddAssign(BinaryOperationNode assignment) =>
        assignment.Right is BinaryOperationNode {
            BinaryOperation: BinaryOperation.PLUS,
            Left: BinaryOperationNode { BinaryOperation: BinaryOperation.PLUS }
        };

    private static bool IsSimpleAddAssign(BinaryOperationNode assignment) =>
        assignment.Right is BinaryOperationNode {
            BinaryOperation: BinaryOperation.PLUS,
            Left: RegisterNode
        };

    private static List<BinaryOperationNode> AssignmentsTo(IrMethod method, int registerIndex) =>
        [.. method.Blocks.SelectMany(block => block.Statements)
            .Select(statement => AssignmentTo(statement, registerIndex))
            .OfType<BinaryOperationNode>()];

    /// <summary>&lt;body&gt;; cmp cx,2; ja +1; nop; ret - the body instructions' flags die at the
    /// cmp, the carry of the cmp stays live through the ja.</summary>
    private static CfgBlock[] WithFlagConsumer(TestInstructionHelper helper,
        params CfgInstruction[] body) {
        CfgInstruction last = body[^1];
        ushort offset = (ushort)(last.Address.Offset + last.Length);
        CfgInstruction cmp = Parse(helper, offset, 0x83, 0xF9, 0x02);
        CfgInstruction ja = Parse(helper, (ushort)(offset + 3), 0x77, 0x01);
        CfgInstruction fallthrough = Parse(helper, (ushort)(offset + 5), 0x90);
        CfgInstruction ret = Parse(helper, (ushort)(offset + 6), 0xC3);
        CfgBlock first = Linear([.. body, cmp, ja]);
        Branch(ja, fallthrough, ret);
        CfgBlock fallthroughBlock = Block(2, fallthrough);
        CfgBlock retBlock = Block(3, ret);
        Chain(fallthrough, ret);
        return [first, fallthroughBlock, retBlock];
    }

    [Fact]
    public void SingleUseAddFoldsIntoTheShift() {
        // Arrange: nop / add bx,1 / shr bx,1 - BX read exactly once, both lowered
        TestInstructionHelper helper = new();
        CfgBlock[] blocks = WithFlagConsumer(helper,
            Parse(helper, 0x0000, 0x90),
            Parse(helper, 0x0001, 0x83, 0xC3, 0x01),
            Parse(helper, 0x0004, 0xD1, 0xEB));

        // Act
        IrMethod method = Propagated(blocks);

        // Assert
        AssignmentsTo(method, Bx).Should().ContainSingle(assignment =>
            IsMergedShiftAssign(assignment), "the add folds into the shift");
    }

    [Fact]
    public void OperandRedefinedBetweenWriterAndReaderRefuses() {
        // Arrange: nop / add bx,cx / mov cx,2 / shr bx,1 - the moved BX + CX would read the new CX
        TestInstructionHelper helper = new();
        CfgBlock[] blocks = WithFlagConsumer(helper,
            Parse(helper, 0x0000, 0x90),
            Parse(helper, 0x0001, 0x03, 0xD9),
            Parse(helper, 0x0003, 0xB9, 0x02, 0x00),
            Parse(helper, 0x0006, 0xD1, 0xEB));

        // Act
        IrMethod method = Propagated(blocks);

        // Assert
        AssignmentsTo(method, Bx).Should().Contain(assignment =>
                IsAddOfRegister(assignment, Cx), "the add stays a statement")
            .And.Contain(assignment => IsUnmergedShiftAssign(assignment),
                "the shift reads the register, not a merged expression");
    }

    [Fact]
    public void TwoReadersRefuseTheMerge() {
        // Arrange: nop / add bx,1 / mov cx,bx / shr bx,1 - BX's version has two live uses
        TestInstructionHelper helper = new();
        CfgBlock[] blocks = WithFlagConsumer(helper,
            Parse(helper, 0x0000, 0x90),
            Parse(helper, 0x0001, 0x83, 0xC3, 0x01),
            Parse(helper, 0x0004, 0x8B, 0xCB),
            Parse(helper, 0x0006, 0xD1, 0xEB));

        // Act
        IrMethod method = Propagated(blocks);

        // Assert
        AssignmentsTo(method, Cx).Should().Contain(assignment =>
            ReadsRegister(assignment, Bx),
            "the add's version feeds two readers and stays");
        AssignmentsTo(method, Bx).Should().Contain(assignment => IsUnmergedShiftAssign(assignment));
    }

    [Fact]
    public void StoreBetweenMemoryReadAndReaderRefuses() {
        // Arrange: nop / mov bx,[si] / mov [di],ax / shr bx,1 - the store may write the read address
        TestInstructionHelper helper = new();
        CfgBlock[] blocks = WithFlagConsumer(helper,
            Parse(helper, 0x0000, 0x90),
            Parse(helper, 0x0001, 0x8B, 0x1C),
            Parse(helper, 0x0003, 0x89, 0x05),
            Parse(helper, 0x0005, 0xD1, 0xEB));

        // Act
        IrMethod method = Propagated(blocks);

        // Assert
        AssignmentsTo(method, Bx).Should().Contain(assignment =>
                IsMemoryReadAssign(assignment), "the memory read stays its own statement")
            .And.Contain(assignment => IsUnmergedShiftAssign(assignment),
                "the shift is not merged across the store");
    }

    [Fact]
    public void PortAccessBetweenMemoryReadAndReaderRefuses() {
        // Arrange: nop / mov bx,[si] / out 0x20,al / shr bx,1 - a port call cannot move a read
        TestInstructionHelper helper = new();
        CfgBlock[] blocks = WithFlagConsumer(helper,
            Parse(helper, 0x0000, 0x90),
            Parse(helper, 0x0001, 0x8B, 0x1C),
            Parse(helper, 0x0003, 0xE6, 0x20),
            Parse(helper, 0x0005, 0xD1, 0xEB));

        // Act
        IrMethod method = Propagated(blocks);

        // Assert
        AssignmentsTo(method, Bx).Should().Contain(assignment =>
                IsMemoryReadAssign(assignment), "the memory read stays its own statement")
            .And.Contain(assignment => IsUnmergedShiftAssign(assignment),
                "the shift is not merged across the port access");
    }

    [Fact]
    public void FaultingInstructionBetweenRefuses() {
        // Arrange: nop / add bx,1 / div cl (with a CPU-fault edge) / shr bx,1
        TestInstructionHelper helper = new();
        CfgInstruction nopEntry = Parse(helper, 0x0000, 0x90);
        CfgInstruction add = Parse(helper, 0x0001, 0x83, 0xC3, 0x01);
        CfgInstruction div = Parse(helper, 0x0004, 0xF6, 0xF1);
        CfgInstruction shr = Parse(helper, 0x0006, 0xD1, 0xEB);
        CfgInstruction faultHandler = Parse(helper, 0x0100, 0x90);
        div.SuccessorsPerType[InstructionSuccessorType.CpuFault] =
            new HashSet<ICfgNode> { faultHandler };
        CfgBlock[] blocks = WithFlagConsumer(helper, nopEntry, add, div, shr);
        CfgCodePartition main = Partition(1, blocks);
        CfgCodePartition handlerPartition = Partition(2, [Block(9, faultHandler)]);
        CfgCodePartitionTransfer faultTransfer = new() {
            FromPartition = main,
            ToPartition = handlerPartition,
            FromNode = div,
            TargetNode = faultHandler,
            Kind = CfgCodePartitionTransferKind.CpuFault
        };

        // Act
        IrMethod method = LiftProgram([main, handlerPartition], [faultTransfer]).First().Method;
        IrPassPipeline.Run(method, IrPass.ExpressionPropagation, verify: true);

        // Assert
        AssignmentsTo(method, Bx).Should().Contain(assignment =>
            IsUnmergedShiftAssign(assignment), "the merge cannot cross a faulting instruction");
    }

    [Fact]
    public void SegmentRegisterWriteIsNeverAMergeWriter() {
        // Arrange: nop / mov es,ax / mov bx,es / ret - segment targets are refused
        TestInstructionHelper helper = new();
        CfgBlock block = Linear(
            Parse(helper, 0x0000, 0x90),
            Parse(helper, 0x0001, 0x8E, 0xC0),
            Parse(helper, 0x0003, 0x8C, 0xC3),
            Parse(helper, 0x0005, 0xC3));

        // Act
        IrMethod method = Propagated(block);

        // Assert
        AssignmentsTo(method, Es).Should().ContainSingle(assignment =>
            IsRegisterReadAssign(assignment), "the ES write stays a statement");
        AssignmentsTo(method, Bx).Should().Contain(assignment => ReadsRegister(assignment, Es));
    }

    [Fact]
    public void ReaderWhoseStoreAddressReusesTheRegisterRefuses() {
        // Arrange: nop / add bx,1 / mov [bx],bx - the address would compute from the new BX
        TestInstructionHelper helper = new();
        CfgBlock[] blocks = WithFlagConsumer(helper,
            Parse(helper, 0x0000, 0x90),
            Parse(helper, 0x0001, 0x83, 0xC3, 0x01),
            Parse(helper, 0x0004, 0x89, 0x1F));

        // Act
        IrMethod method = Propagated(blocks);

        // Assert
        AssignmentsTo(method, Bx).Should().Contain(assignment => IsAddAssign(assignment),
            "the store address reads the same register and refuses the merge");
        Nodes(method).Should().Contain(node => IsStoreAssign(node));
    }

    [Fact]
    public void MergeChainStopsAtTheChainLimit() {
        // Arrange: nop / add bx,1 x4 - three merge into one statement, the fourth stays
        TestInstructionHelper helper = new();
        CfgBlock[] blocks = WithFlagConsumer(helper,
            Parse(helper, 0x0000, 0x90),
            Parse(helper, 0x0001, 0x83, 0xC3, 0x01),
            Parse(helper, 0x0004, 0x83, 0xC3, 0x01),
            Parse(helper, 0x0007, 0x83, 0xC3, 0x01),
            Parse(helper, 0x000A, 0x83, 0xC3, 0x01));

        // Act
        IrMethod method = Propagated(blocks);

        // Assert
        AssignmentsTo(method, Bx).Should().Contain(assignment =>
                IsNestedAddAssign(assignment), "three adds merge into one statement")
            .And.Contain(assignment => IsSimpleAddAssign(assignment),
                "the fourth add stays a statement");
    }
}
