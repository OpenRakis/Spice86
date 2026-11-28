namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.Emulator.StateSerialization;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>Each check of <see cref="CfgInvariants"/> is shown able to fail on a broken graph built
/// by hand, and each failure message names the golden key.</summary>
public sealed class CfgInvariantsTest {
    private const string Key = "cfg-test";

    private static CfgBlockInfo Block(int id, string entry, int[] pred, int[] succ, bool? dead = null) =>
        new() { Id = id, Entry = entry, Term = "ret", Pred = pred, Succ = succ, Asm = [], Dead = dead };

    /// <summary>The graph every test starts from: entry block 0 at the only entry point,
    /// 0 -> 1 -> 2, links in both directions, one partition holding 0, 1, 2 with entry block 0.</summary>
    private static CfgCpuGraph ValidGraph(CfgBlockInfo[]? blocks = null, CfgPartitionInfo[]? partitions = null,
        CfgPartitionEntryInfo[]? partitionEntries = null) => new() {
        CurrentContextDepth = 0,
        CurrentContextEntryPoint = "F000:0000",
        TotalEntryPoints = 1,
        EntryPointAddresses = ["F000:0000"],
        Blocks = blocks ?? [
            Block(0, "F000:0000", [], [1]),
            Block(1, "F000:0002", [0], [2]),
            Block(2, "F000:0004", [1], [])
        ],
        Partitions = partitions ?? [
            new CfgPartitionInfo {
                Id = 1, Kind = "function", Name = "f",
                Blocks = [0, 1, 2],
                Entries = partitionEntries ?? [new CfgPartitionEntryInfo {
                    Block = 0, Address = "F000:0000", Kind = "entry"
                }]
            }
        ],
        Truncated = false
    };

    private static void AssertInvalid(CfgCpuGraph graph, string expected) =>
        ((Action)(() => CfgInvariants.AssertValid(Key, graph))).Should().Throw<Exception>()
            .WithMessage($"*{Key}*invariant {expected}*");

    [Fact]
    public void ValidGraphPasses() {
        Action check = () => CfgInvariants.AssertValid(Key, ValidGraph());
        check.Should().NotThrow();
    }

    [Fact]
    public void DuplicateBlockIdFails() {
        CfgBlockInfo[] blocks = ValidGraph().Blocks;
        CfgCpuGraph graph = ValidGraph([.. blocks, Block(1, "F000:0006", [], [])]);
        AssertInvalid(graph, "1*block 1*");
    }

    [Fact]
    public void MissingPredecessorLinkFails() {
        CfgCpuGraph graph = ValidGraph([
            Block(0, "F000:0000", [], [1]),
            Block(1, "F000:0002", [], [2]),
            Block(2, "F000:0004", [1], [])
        ]);
        AssertInvalid(graph, "2*block 1*");
    }

    [Fact]
    public void MissingSuccessorLinkFails() {
        CfgCpuGraph graph = ValidGraph([
            Block(0, "F000:0000", [], [1]),
            Block(1, "F000:0002", [0], []),
            Block(2, "F000:0004", [1], [])
        ]);
        AssertInvalid(graph, "2*block 2*");
    }

    [Fact]
    public void BlockOutsideEveryPartitionFails() {
        CfgCpuGraph graph = ValidGraph(partitions: [
            new CfgPartitionInfo {
                Id = 1, Kind = "function", Name = "f", Blocks = [0, 1],
                Entries = [new CfgPartitionEntryInfo { Block = 0, Address = "F000:0000", Kind = "entry" }]
            }
        ]);
        AssertInvalid(graph, "3*block 2*");
    }

    [Fact]
    public void UnknownPartitionBlockFails() {
        CfgCpuGraph graph = ValidGraph(partitions: [
            new CfgPartitionInfo {
                Id = 1, Kind = "function", Name = "f", Blocks = [0, 1, 2, 7],
                Entries = [new CfgPartitionEntryInfo { Block = 0, Address = "F000:0000", Kind = "entry" }]
            }
        ]);
        AssertInvalid(graph, "3*block 7*");
    }

    [Fact]
    public void DeadBlockOutsidePartitionsPasses() {
        CfgCpuGraph graph = ValidGraph(
            [
                Block(0, "F000:0000", [], [1]),
                Block(1, "F000:0002", [0], [2]),
                Block(2, "F000:0004", [1], [], dead: true)
            ],
            [
                new CfgPartitionInfo {
                    Id = 1, Kind = "function", Name = "f", Blocks = [0, 1],
                    Entries = [new CfgPartitionEntryInfo { Block = 0, Address = "F000:0000", Kind = "entry" }]
                }
            ]);
        Action check = () => CfgInvariants.AssertValid(Key, graph);
        check.Should().NotThrow();
    }

    [Fact]
    public void NullPartitionsSkipsInvariant3() {
        CfgCpuGraph graph = ValidGraph(partitions: null);
        Action check = () => CfgInvariants.AssertValid(Key, graph);
        check.Should().NotThrow();
    }

    [Fact]
    public void UnreachableBlockFails() {
        CfgCpuGraph graph = ValidGraph([
            Block(0, "F000:0000", [], [1]),
            Block(1, "F000:0002", [0, 3], [2]),
            Block(2, "F000:0004", [1], []),
            Block(3, "F000:0006", [], [1])
        ], [
            new CfgPartitionInfo {
                Id = 1, Kind = "function", Name = "f", Blocks = [0, 1, 2, 3],
                Entries = [new CfgPartitionEntryInfo { Block = 0, Address = "F000:0000", Kind = "entry" }]
            }
        ]);
        AssertInvalid(graph, "4*block 3*");
    }

    [Fact]
    public void PartitionEntryMakesBlockReachable() {
        CfgCpuGraph graph = ValidGraph([
            Block(0, "F000:0000", [], [1]),
            Block(1, "F000:0002", [0, 3], [2]),
            Block(2, "F000:0004", [1], []),
            Block(3, "F000:0006", [], [1])
        ], [
            new CfgPartitionInfo {
                Id = 1, Kind = "function", Name = "f", Blocks = [0, 1, 2, 3],
                Entries = [
                    new CfgPartitionEntryInfo { Block = 0, Address = "F000:0000", Kind = "entry" },
                    new CfgPartitionEntryInfo { Block = 3, Address = "F000:0006", Kind = "entry" }
                ]
            }
        ]);
        Action check = () => CfgInvariants.AssertValid(Key, graph);
        check.Should().NotThrow();
    }
}
