namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;

using System.Linq;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// Locks the lifter contract on real parsed instructions: <c>rep movsb</c> flattens to an inside-edge
/// loop, <c>jne</c> flattens to a branch whose edges leave the instruction, a division is tagged and its
/// statements are fault-capable, and temporaries carry the instruction's local suffix.
/// </summary>
public class IrLifterTest {

    private static IrMethod Lift(params CfgBlock[] blocks) {
        CfgCodePartition partition = new() {
            Id = 1,
            Kind = CfgCodePartitionKind.Observed,
            Name = "test",
            Blocks = blocks,
            Entries = [
                new CfgCodePartitionEntry {
                    Node = blocks[0].Entry,
                    Kind = CfgCodePartitionEntryKind.FunctionEntry
                }
            ]
        };
        CfgPartitionedProgram program = new() {
            Partitions = [partition],
            Transfers = []
        };
        return new CfgCSharpGenerator(collectCensuses: true, verify: true).LiftMethods(program).Should().ContainSingle().Subject.Method;
    }

    private static IEnumerable<IrStatement> AllStatements(IrMethod method) =>
        method.Blocks.SelectMany(block => block.Statements);

    [Fact]
    public void RepMovsbLiftsToInsideEdgeLoop() {
        TestInstructionHelper helper = new();
        CfgInstruction rep = Parse(helper, 0x0000, 0xF3, 0xA4);
        CfgInstruction nop = Parse(helper, 0x0002, 0x90);
        rep.Successors.Add(nop);
        rep.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { nop };
        CfgBlock block = Block(1, rep, nop);

        IrMethod method = Lift(block);

        IrVerifier.Verify(method);
        IrBlock header = method.Blocks.Single(block => block.Terminator is IrBranch);
        IrBranch loop = (IrBranch)header.Terminator;
        loop.WhenTrue.InsideInstruction.Should().BeTrue("the loop body is a continuation of the same instruction");
        IrBlock body = loop.WhenTrue.Target;
        body.Terminator.Should().BeOfType<IrJump>()
            .Which.Edge.Should().Match<IrEdge>(edge => edge.InsideInstruction && ReferenceEquals(edge.Target, header),
                "the loop body must jump back to the header inside the instruction");
        loop.WhenFalse.Target.StartsInstruction.Should().BeTrue(
            "jump threading sends the loop exit straight to the next instruction's block");
        loop.WhenFalse.InsideInstruction.Should().BeFalse();
    }

    [Fact]
    public void JneLiftsToBranchToInstructionStarts() {
        TestInstructionHelper helper = new();
        CfgInstruction jne = Parse(helper, 0x0000, 0x75, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0002, 0x90);
        CfgInstruction taken = Parse(helper, 0x0004, 0x90);
        jne.Successors.Add(fallthrough);
        jne.Successors.Add(taken);
        jne.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { fallthrough, taken };
        fallthrough.Successors.Add(taken);
        CfgBlock jneBlock = Block(1, jne);
        CfgBlock fallthroughBlock = Block(2, fallthrough);
        CfgBlock takenBlock = Block(3, taken);

        IrMethod method = Lift(jneBlock, fallthroughBlock, takenBlock);

        IrVerifier.Verify(method);
        IrBranch branch = method.Blocks.Select(block => block.Terminator).OfType<IrBranch>()
            .Should().ContainSingle().Subject;
        branch.Edges.Should().AllSatisfy(edge => edge.InsideInstruction.Should().BeFalse(),
            "jump threading removes the trampoline arm blocks");
        branch.Edges.Should().AllSatisfy(edge => edge.Target.StartsInstruction.Should().BeTrue());
        branch.Edges.Select(edge => edge.Target.EntryOrigin).Should().BeEquivalentTo([fallthrough, taken]);
    }

    [Fact]
    public void DivWithFaultEdgeHasFaultCapableDivisionStatements() {
        TestInstructionHelper helper = new();
        CfgInstruction div = Parse(helper, 0x0000, 0xF7, 0xF3);
        CfgInstruction nop = Parse(helper, 0x0002, 0x90);
        CfgInstruction faultHandler = Parse(helper, 0x0100, 0x90);
        div.Successors.Add(nop);
        div.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { nop };
        div.SuccessorsPerType[InstructionSuccessorType.CpuFault] = new HashSet<ICfgNode> { faultHandler };
        CfgBlock block = Block(1, div, nop);

        IrMethod method = Lift(block);

        IrVerifier.Verify(method);
        method.Shapes[div].Should().Be(IrInstructionShape.Division);
        AllStatements(method).Should().Contain(statement => statement.Node is DivisionNode);
        AllStatements(method).Should().Contain(statement =>
            statement.Node is DivisionNode && statement.Effects.MayFault,
            "body statements of a fault-capable instruction are marked MayFault");
        AllStatements(method)
            .Where(statement => statement.Node is CheckExternalEventsNode or SpeculativeGuardNode
                or InstructionStartNode or CommentNode)
            .Should().AllSatisfy(statement => statement.Effects.MayFault.Should().BeFalse());
    }

    [Theory]
    [InlineData(new byte[] { 0x93 }, (int)IrInstructionShape.Exchange)] // xchg ax, bx
    [InlineData(new byte[] { 0xC4, 0x1E, 0x00, 0x00 }, (int)IrInstructionShape.LoadFarPointer)] // les bx, [0]
    [InlineData(new byte[] { 0xF3, 0xA6 }, (int)IrInstructionShape.RepCompare)] // repe cmpsb
    [InlineData(new byte[] { 0xF7, 0xF3 }, (int)IrInstructionShape.Division)] // div bx
    [InlineData(new byte[] { 0x90 }, (int)IrInstructionShape.None)] // nop
    public void InstructionShapeIsTagged(byte[] bytes, int expected) {
        TestInstructionHelper helper = new();
        CfgInstruction instruction = Parse(helper, 0x0000, bytes);
        CfgBlock block = Block(1, instruction);

        IrMethod method = Lift(block);

        method.Shapes[instruction].Should().Be((IrInstructionShape)expected);
    }

    [Fact]
    public void TemporariesCarryTheInstructionLocalSuffix() {
        TestInstructionHelper helper = new();
        CfgInstruction repneScasw = Parse(helper, 0x0000, 0xF2, 0xAF);
        CfgBlock block = Block(1, repneScasw);

        IrMethod method = Lift(block);

        AllStatements(method).Select(statement => statement.Node).OfType<VariableDeclarationNode>()
            .Select(declaration => declaration.VariableName)
            .Should().OnlyContain(name => name.EndsWith("_0000"),
                "a temporary is renamed with the offset-hex suffix of its instruction");
    }
}
