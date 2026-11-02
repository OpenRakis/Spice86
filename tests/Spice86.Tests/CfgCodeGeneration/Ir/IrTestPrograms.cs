namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Microsoft.CodeAnalysis;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;

/// <summary>
/// Shared builders for the IR tests: instruction parsing, CFG block and partition construction,
/// generation through <see cref="CfgCSharpGenerator"/> with censuses and verification on, and the
/// generated-source compile check.
/// </summary>
internal static class IrTestPrograms {
    private static readonly SegmentedAddress Segment = new(0xF000, 0x0000);

    /// <summary>Parses <paramref name="bytes"/> into one instruction at <paramref name="offset"/>.</summary>
    public static CfgInstruction Parse(TestInstructionHelper helper, ushort offset, params byte[] bytes) =>
        helper.WriteAndParse(new SegmentedAddress(Segment.Segment, offset),
            writer => writer.WriteBytes(bytes));

    /// <summary>One CFG block holding <paramref name="instructions"/> in order.</summary>
    public static CfgBlock Block(int id, params CfgInstruction[] instructions) {
        CfgBlock block = new(id, instructions[0]);
        foreach (CfgInstruction instruction in instructions) {
            instruction.ContainingBlock = block;
            if (!Equals(instruction, instructions[0])) {
                block.Append(instruction);
            }
        }
        return block;
    }

    /// <summary>Parses and links straight-line instructions into one block.</summary>
    public static CfgBlock Linear(TestInstructionHelper helper,
        params (ushort Offset, byte[] Bytes)[] instructions) {
        CfgInstruction[] parsed = instructions
            .Select(instruction => Parse(helper, instruction.Offset, instruction.Bytes))
            .ToArray();
        return Linear(parsed);
    }

    /// <summary>Links already-parsed straight-line instructions into one block.</summary>
    public static CfgBlock Linear(params CfgInstruction[] instructions) {
        Chain(instructions);
        return Block(1, instructions);
    }

    /// <summary>Wires <paramref name="instructions"/> as straight-line Normal successors in order.</summary>
    public static CfgInstruction[] Chain(params CfgInstruction[] instructions) {
        for (int i = 0; i + 1 < instructions.Length; i++) {
            instructions[i].Successors.Add(instructions[i + 1]);
            instructions[i].SuccessorsPerType[InstructionSuccessorType.Normal] =
                new HashSet<ICfgNode> { instructions[i + 1] };
        }
        return instructions;
    }

    /// <summary>Wires the Normal successors of <paramref name="jump"/> to
    /// <paramref name="fallthrough"/> and <paramref name="taken"/>.</summary>
    public static void Branch(CfgInstruction jump, CfgInstruction fallthrough, CfgInstruction taken) {
        jump.Successors.Add(fallthrough);
        jump.Successors.Add(taken);
        jump.SuccessorsPerType[InstructionSuccessorType.Normal] =
            new HashSet<ICfgNode> { fallthrough, taken };
    }

    /// <summary>Every IR node <paramref name="method"/> holds: statement subgraphs and terminator
    /// expressions.</summary>
    public static IEnumerable<IVisitableAstNode> Nodes(IrMethod method) => method.Blocks.SelectMany(
        block => block.Statements.SelectMany(statement => IrAstChildren.Descendants(statement.Node))
            .Concat(IrAstChildren.RootsOf(block.Terminator)));

    /// <summary>Lifts the one-partition program of <paramref name="blocks"/> entered at the first
    /// block.</summary>
    public static IrMethod Lift(params CfgBlock[] blocks) =>
        LiftProgram([Partition(1, blocks)], []).Single().Method;

    /// <summary>Lifts an explicitly partitioned program.</summary>
    public static IReadOnlyList<LiftedMethod> LiftProgram(CfgCodePartition[] partitions,
        CfgCodePartitionTransfer[] transfers) =>
        new CfgCSharpGenerator().LiftMethods(new CfgPartitionedProgram {
            Partitions = partitions,
            Transfers = transfers
        });

    /// <summary>The single function-entry declaration of a one-partition program.</summary>
    public static CfgCodePartitionEntry[] SingleEntry(CfgBlock block) => [
        new CfgCodePartitionEntry { Node = block.Entry, Kind = CfgCodePartitionEntryKind.FunctionEntry }
    ];

    /// <summary>One observed partition over <paramref name="blocks"/> entered at the first block.</summary>
    public static CfgCodePartition Partition(int id, params CfgBlock[] blocks) =>
        new() {
            Id = id,
            Kind = CfgCodePartitionKind.Observed,
            Name = id == 1 ? "test" : "handler",
            Blocks = blocks,
            Entries = SingleEntry(blocks[0])
        };

    /// <summary>Generates the source of a one-partition program with explicit entries.</summary>
    public static string Generate(CfgCodePartitionEntry[] entries, params CfgBlock[] blocks) =>
        Generate(entries, [], blocks);

    /// <summary>Generates the source of a main partition followed by <paramref name="extraPartitions"/>.</summary>
    public static string Generate(CfgCodePartitionEntry[] entries, CfgCodePartition[] extraPartitions,
        params CfgBlock[] blocks) =>
        Generate(entries, extraPartitions, _ => [], blocks);

    /// <summary>Generates the source of a main partition with explicit transfers plus extra partitions.</summary>
    public static string Generate(CfgCodePartitionEntry[] entries, CfgCodePartition[] extraPartitions,
        Func<CfgCodePartition, CfgCodePartitionTransfer[]> transfers, params CfgBlock[] blocks) {
        CfgCodePartition partition = new() {
            Id = 1,
            Kind = CfgCodePartitionKind.Observed,
            Name = "test",
            Blocks = blocks,
            Entries = entries
        };
        return GenerateProgram([partition, .. extraPartitions], transfers(partition));
    }

    /// <summary>Generates the source of an explicitly partitioned program.</summary>
    public static string GenerateProgram(CfgCodePartition[] partitions,
        CfgCodePartitionTransfer[] transfers) {
        CfgPartitionedProgram program = new() {
            Partitions = partitions,
            Transfers = transfers
        };
        return new CfgCSharpGenerator(collectCensuses: true, verify: true).Generate(program).SourceText;
    }

    /// <summary>The generated source compiles.</summary>
    public static void AssertCompiles(string source) {
        GeneratedCompilation compilation = new GeneratedOverrideCompiler().Compile(source);
        compilation.EmitResult.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the IR pipeline's output must compile");
    }
}
