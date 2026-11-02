namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;

using System.Collections.Generic;
using System.Linq;

using Xunit;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;
using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// Locks the lowering of IR blocks into the structurizer's input: a lifted <c>rep movsb</c> method
/// structures and prints as <c>while (CX != 0) { ... }</c> with no <c>goto</c> left, and a lifted
/// <c>jne</c> lowers to an <c>if</c> holding two explicit gotos before structuring.
/// </summary>
public class IrBlockLoweringTest {
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

    private static NaturalLoopForest<IrBlock> Loops(IrMethod method) {
        BlockGraph<IrBlock> graph = method.ToBlockGraph();
        DepthFirstOrdering<IrBlock> traversal = new([.. graph.EntryBlocks, .. graph.Blocks], graph.GetSuccessors);
        return NaturalLoopForest<IrBlock>.Build(graph, traversal.RetreatingEdges);
    }

    private static HashSet<IGotoTarget> EntryTargets(IrMethod method) =>
        [.. method.Entries.Select(entry => (IGotoTarget)entry.Block)];

    private static string Print(IVisitableAstNode node) {
        CSharpSourceWriter writer = new();
        new CSharpStatementPrinter(writer, new CSharpExpressionPrinter(new Dictionary<ushort, string> { [0xF000] = "es" }),
            target => target is IrBlock block ? $"b{block.Id}"
                : throw new InvalidOperationException($"Goto target {target} is not an IR block.")).Print(node);
        return writer.ToString();
    }

    [Fact]
    public void RepMovsbStructuresAndPrintsAsAWhileLoopWithoutGotos() {
        // Arrange
        TestInstructionHelper helper = new();
        CfgInstruction rep = Parse(helper, 0x0000, 0xF3, 0xA4);
        CfgInstruction nop = Parse(helper, 0x0002, 0x90);
        rep.Successors.Add(nop);
        rep.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { nop };
        IrMethod method = Lift(Block(1, rep, nop));
        IReadOnlyList<LoweredBlock<IrBlock>> lowered = IrBlockLowering.Lower(method, method.Blocks);

        // Act
        BlockNode structured = MethodStructurizer.Structure(lowered, Loops(method), method.Name,
            EntryTargets(method), new Dictionary<GotoNode, GotoKeepReason>(ReferenceEqualityComparer.Instance),
            placeLoopExits: true, allowLocalsDeclaredInBlock: true, localsDeclaredInDominatingBlocks: false);
        BlockNode body = LabelPlacer.Place(FallthroughGotoRemover.Remove(structured),
            GotoTargetCollector.Collect(structured).Labels);

        // Assert
        string printed = Print(body);
        printed.Should().Contain("while (CX != 0)");
        printed.Should().NotContain("goto ");
        printed.Should().NotContain("break;");
    }

    [Fact]
    public void GotosLandingOnAnInstructionWithInnerControlFlowGetDistinctLabels() {
        // Arrange: rep movsb is reached through the jne fallthrough, a backward jump after it, and a
        // second entry via the jne arm, so both its start block and its loop header are goto targets
        // sharing one entry instruction.
        TestInstructionHelper helper = new();
        CfgInstruction jne = Parse(helper, 0x0000, 0x75, 0x06);
        CfgInstruction mid = Parse(helper, 0x0002, 0x90);
        CfgInstruction rep = Parse(helper, 0x0003, 0xF3, 0xA4);
        CfgInstruction nop = Parse(helper, 0x0005, 0x90);
        CfgInstruction back = Parse(helper, 0x0006, 0xEB, 0xFB);
        CfgInstruction skip = Parse(helper, 0x0008, 0xEB, 0xF9);
        jne.Successors.Add(mid);
        jne.Successors.Add(skip);
        jne.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { mid, skip };
        mid.Successors.Add(rep);
        mid.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { rep };
        rep.Successors.Add(nop);
        rep.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { nop };
        nop.Successors.Add(back);
        nop.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { back };
        back.Successors.Add(rep);
        back.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { rep };
        skip.Successors.Add(rep);
        skip.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { rep };
        IrMethod method = Lift(Block(1, jne), Block(2, mid), Block(3, rep, nop, back), Block(4, skip));
        IReadOnlyList<LoweredBlock<IrBlock>> lowered = IrBlockLowering.Lower(method, method.Blocks);

        // Act
        BlockNode structured = MethodStructurizer.Structure(lowered, Loops(method), method.Name,
            EntryTargets(method), new Dictionary<GotoNode, GotoKeepReason>(ReferenceEqualityComparer.Instance),
            placeLoopExits: true, allowLocalsDeclaredInBlock: true, localsDeclaredInDominatingBlocks: false);
        // Labels are collected before fallthrough removal so every targeted block gets one; the check
        // is that each label lands before the statement its target anchors, not that gotos remain.
        BlockNode body = LabelPlacer.Place(FallthroughGotoRemover.Remove(structured),
            GotoTargetCollector.Collect(structured).Labels);

        // Assert
        string printed = Print(body);
        printed.Should().Contain("while (CX != 0)");
        printed.Should().MatchRegex(@"(?m)^b\d+:\s*while \(true\)");
        printed.Should().MatchRegex(@"(?m)^b\d+:\s*while \(CX != 0\)");
    }

    [Fact]
    public void JneLowersToAnIfWithTwoGotos() {
        // Arrange
        TestInstructionHelper helper = new();
        CfgInstruction jne = Parse(helper, 0x0000, 0x75, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0002, 0x90);
        CfgInstruction taken = Parse(helper, 0x0004, 0x90);
        jne.Successors.Add(fallthrough);
        jne.Successors.Add(taken);
        jne.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { fallthrough, taken };
        fallthrough.Successors.Add(taken);
        IrMethod method = Lift(Block(1, jne), Block(2, fallthrough), Block(3, taken));

        // Act
        IReadOnlyList<LoweredBlock<IrBlock>> lowered = IrBlockLowering.Lower(method, method.Blocks);

        // Assert
        IfElseNode ifElse = lowered.SelectMany(block => block.Body.Statements)
            .OfType<InstructionBoundaryNode>().SelectMany(boundary => boundary.Body.Statements)
            .OfType<IfElseNode>().Should().ContainSingle().Subject;
        ifElse.TrueCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle()
            .Which.Should().BeOfType<GotoNode>();
        ifElse.FalseCase.Should().BeOfType<BlockNode>().Which.Statements.Should().ContainSingle()
            .Which.Should().BeOfType<GotoNode>();
    }
}
