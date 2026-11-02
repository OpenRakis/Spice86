namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Linq.Expressions;

using Xunit;

using SelfModifyingSelectorNode = Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying.SelectorNode;

/// <summary>
/// <see cref="GeneratorIrRoundTrip"/> rebuilds an instruction's execution AST through the IR pipeline.
/// These tests check the rebuilt AST keeps the shape the JIT can execute (no goto/label/continue) and
/// that the <see cref="AstExpressionBuilder"/> compiles it.
/// </summary>
public class GeneratorIrRoundTripTest {
    private readonly Memory _memory;
    private readonly State _state;
    private readonly InstructionParser _parser;
    private readonly GeneratorIrRoundTrip _rewriter = new();

    public GeneratorIrRoundTripTest() {
        _memory = new Memory(new AddressReadWriteBreakpoints(), new Ram(0x100000), new A20Gate(),
            new RealModeMmu8086(), false);
        _state = new State(CpuModel.INTEL_80286);
        _parser = new InstructionParser(_memory, _state, new SequentialIdAllocator());
    }

    private CfgInstruction Parse(SegmentedAddress address, params byte[] opcode) {
        uint physical = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        for (int i = 0; i < opcode.Length; i++) {
            _memory.UInt8[physical + i] = opcode[i];
        }
        return _parser.ParseInstructionAt(address);
    }

    private IVisitableAstNode RoundTrip(CfgInstruction instruction) {
        IVisitableAstNode rebuilt;
        try {
            rebuilt = _rewriter.Rewrite(instruction, instruction.ExecutionAst);
        } catch (NotSupportedException exception) {
            throw new NotSupportedException(exception.Message, exception);
        }
        rebuilt.Should().NotBeSameAs(instruction.ExecutionAst);
        Collect(rebuilt).Where(node =>
                node is GotoNode or LabelNode or ContinueNode or InstructionBoundaryNode)
            .Should().BeEmpty();
        Compile(rebuilt);
        return rebuilt;
    }

    /// <summary>Compiles the rebuilt AST exactly like the JIT path does.</summary>
    private static void Compile(IVisitableAstNode ast) {
        AstExpressionBuilder builder = new(expandAluCalls: true);
        Expression expression = ast.Accept(builder);
        Expression<CfgNodeExecutionAction<InstructionExecutionHelper>> action =
            builder.ToActionWithHelper(expression);
        action.Compile(preferInterpretation: true);
    }

    private static List<IVisitableAstNode> Collect(IVisitableAstNode node) {
        List<IVisitableAstNode> nodes = [node];
        IEnumerable<IVisitableAstNode> children = node switch {
            BlockNode block => block.Statements,
            IfElseNode ifElse => [ifElse.Condition, ifElse.TrueCase, ifElse.FalseCase],
            WhileNode loop => [loop.Condition, loop.Body],
            WhileWithExitNode loop => [loop.Condition, loop.Body, loop.ExitCondition],
            SwitchNode dispatch => dispatch.Sections.Select(section => (IVisitableAstNode)section.Body)
                .Append(dispatch.Default).Prepend(dispatch.Value),
            ReturnNode @return => [@return.Value],
            FailAsUntestedNode fail => fail.Parts.Select(part => part.Value).OfType<ValueNode>(),
            VariableDeclarationNode declaration => [declaration.Initializer],
            BinaryOperationNode binary => [binary.Left, binary.Right],
            UnaryOperationNode unary => [unary.Value],
            TypeConversionNode conversion => [conversion.Value],
            ExplicitCastNode cast => [cast.Value],
            SegmentedPointerNode pointer => pointer.DefaultSegment == null
                ? [pointer.Segment, pointer.Offset]
                : [pointer.Segment, pointer.DefaultSegment, pointer.Offset],
            AbsolutePointerNode pointer => [pointer.AbsoluteAddress],
            SegmentedAddressNode address => [address.Segment, address.Offset],
            MethodCallNode call => call.Arguments,
            MethodCallValueNode call => call.CallNode.Arguments,
            _ => []
        };
        foreach (IVisitableAstNode child in children) {
            nodes.AddRange(Collect(child));
        }
        return nodes;
    }

    [Fact]
    public void RepMovsb_RoundTripsToWhileWithExit() {
        CfgInstruction instruction = Parse(new SegmentedAddress(0x1000, 0x0000), 0xF3, 0xA4);

        IVisitableAstNode rebuilt = RoundTrip(instruction);

        Collect(rebuilt).OfType<WhileNode>().Should().HaveCount(1);
    }

    [Fact]
    public void RepeCmpsb_RoundTripsToWhileWithExit() {
        CfgInstruction instruction = Parse(new SegmentedAddress(0x1000, 0x0010), 0xF3, 0xA6);

        IVisitableAstNode rebuilt = RoundTrip(instruction);

        Collect(rebuilt).OfType<WhileWithExitNode>().Should().HaveCount(1);
    }

    [Fact]
    public void Jne_RoundTripsToConditionalExit() {
        CfgInstruction instruction = Parse(new SegmentedAddress(0x1000, 0x0020), 0x75, 0x10);

        IVisitableAstNode rebuilt = RoundTrip(instruction);

        Collect(rebuilt).OfType<IfElseNode>().Should().NotBeEmpty();
        Collect(rebuilt).OfType<JumpNearNode>().Should().HaveCount(1);
    }

    [Fact]
    public void Bsf_RoundTrips() {
        // bsf ax, bx
        CfgInstruction instruction = Parse(new SegmentedAddress(0x1000, 0x0030), 0x0F, 0xBC, 0xC3);

        IVisitableAstNode rebuilt = RoundTrip(instruction);

        rebuilt.Should().BeOfType<BlockNode>();
    }

    [Fact]
    public void Daa_RoundTrips() {
        CfgInstruction instruction = Parse(new SegmentedAddress(0x1000, 0x0040), 0x27);

        IVisitableAstNode rebuilt = RoundTrip(instruction);

        rebuilt.Should().BeOfType<BlockNode>();
    }

    [Fact]
    public void Cmpxchg_RoundTrips() {
        // cmpxchg bx, ax
        CfgInstruction instruction = Parse(new SegmentedAddress(0x1000, 0x0050), 0x0F, 0xB1, 0xC3);

        IVisitableAstNode rebuilt = RoundTrip(instruction);

        rebuilt.Should().BeOfType<BlockNode>();
    }

    [Fact]
    public void Enter_RoundTrips() {
        // enter 0x28, 0x03: a frame-nesting loop the IR pipeline structures as while (true) { ... if (e) break; }
        CfgInstruction instruction = Parse(new SegmentedAddress(0x1000, 0x0068), 0xC8, 0x28, 0x00, 0x03);

        IVisitableAstNode rebuilt = RoundTrip(instruction);

        Collect(rebuilt).OfType<WhileNode>().Should().HaveCount(1);
    }

    [Fact]
    public void Div_RoundTrips() {
        // div bx
        CfgInstruction instruction = Parse(new SegmentedAddress(0x1000, 0x0060), 0xF7, 0xF3);

        IVisitableAstNode rebuilt = RoundTrip(instruction);

        rebuilt.Should().BeOfType<BlockNode>();
    }

    [Fact]
    public void SelectorNode_IsReturnedUnchanged() {
        SelfModifyingSelectorNode selector = new(0, new SegmentedAddress(0x1000, 0x0070));
        BlockNode ast = Sequence(new MoveIpNextNode(new ConstantNode(DataType.UINT32, 0x70)));

        IVisitableAstNode rebuilt = _rewriter.Rewrite(selector, ast);

        rebuilt.Should().BeSameAs(ast);
    }

    [Fact]
    public void Movsb_RoundTripsToAConditionalDirectionStep() {
        // Arrange: movsb - the direction at the single instruction's entry is unknown, so the
        // round trip keeps the select.
        CfgInstruction instruction = Parse(new SegmentedAddress(0x1000, 0x0080), 0xA4);

        // Act
        IVisitableAstNode rebuilt = RoundTrip(instruction);

        // Assert
        Collect(rebuilt).OfType<ConditionalValueNode>().Should().NotBeEmpty();
    }

    private static BlockNode Sequence(params IVisitableAstNode[] statements) => new(statements);
}
