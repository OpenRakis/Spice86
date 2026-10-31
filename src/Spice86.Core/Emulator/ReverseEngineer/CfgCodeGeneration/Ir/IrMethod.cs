namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using System.Linq;

/// <summary>The IR of one generated method: flat blocks in layout order, entries, and the covered CFG nodes.</summary>
internal sealed class IrMethod {
    /// <summary>Creates a method.</summary>
    public IrMethod(string name, List<IrBlock> blocks, IReadOnlyList<IrEntry> entries,
        IReadOnlySet<ICfgNode> methodNodes, IReadOnlyDictionary<ICfgNode, IrInstructionShape> shapes) {
        Name = name;
        Blocks = blocks;
        Entries = entries;
        MethodNodes = methodNodes;
        Shapes = shapes;
    }

    /// <summary>The generated method name.</summary>
    public string Name { get; }

    /// <summary>The blocks, in layout order.</summary>
    public List<IrBlock> Blocks { get; }

    /// <summary>The method entries, in entry order.</summary>
    public IReadOnlyList<IrEntry> Entries { get; }

    /// <summary>The CFG nodes the method covers.</summary>
    public IReadOnlySet<ICfgNode> MethodNodes { get; }

    /// <summary>The instruction shape tag per node; a missing key is <see cref="IrInstructionShape.None"/>.</summary>
    public IReadOnlyDictionary<ICfgNode, IrInstructionShape> Shapes { get; }

    /// <summary>The method-unique local variable suffix of each instruction, for pass-created temporaries.</summary>
    public Dictionary<ICfgNode, string> LocalSuffixes { get; } = [];

    /// <summary>The method-scope temporaries the passes create, printed at the top of the method.</summary>
    public List<VariableDeclarationNode> MethodVariables { get; } = [];

    /// <summary>
    /// The CFG successor edges of a block that ends in <see cref="IrExit"/>: the CFG records where an exit
    /// goes (a <c>ret</c> to a covered address is a back edge) but the IR terminator cannot hold the edge.
    /// They only drive loop detection and the structurizer's arm gathering - never SSA, verification or
    /// emission - so they live in the structuring graph <see cref="ToStructuringGraph"/> builds.
    /// </summary>
    public Dictionary<IrBlock, List<IrBlock>> StructuralSuccessors { get; } = [];

    /// <summary>The blocks every incoming edge of stays inside one instruction's control flow
    /// (computed in one pass over all edges).</summary>
    public HashSet<IrBlock> BlocksEnteredOnlyThroughInsideEdges() {
        HashSet<IrBlock> insideOnly = [];
        HashSet<IrBlock> fromOutside = [];
        foreach (IrBlock block in Blocks) {
            foreach (IrEdge edge in block.Terminator.Edges) {
                if (edge.InsideInstruction) {
                    insideOnly.Add(edge.Target);
                } else {
                    fromOutside.Add(edge.Target);
                }
            }
        }
        insideOnly.ExceptWith(fromOutside);
        return insideOnly;
    }

    /// <summary>
    /// The block graph of the method: successors from the terminators' edges, deduplicated and in edge
    /// order; every fallthrough <c>null</c>; stable order by block id; entry blocks in entry order,
    /// deduplicated.
    /// </summary>
    public BlockGraph<IrBlock> ToBlockGraph() {
        Dictionary<IrBlock, IReadOnlyList<IrBlock>> successorsByBlock = new();
        foreach (IrBlock block in Blocks) {
            List<IrBlock> successors = [];
            foreach (IrEdge edge in block.Terminator.Edges.Where(edge => !successors.Contains(edge.Target))) {
                successors.Add(edge.Target);
            }
            successorsByBlock[block] = successors;
        }
        List<IrBlock> entryBlocks = [];
        foreach (IrEntry entry in Entries.Where(entry => !entryBlocks.Contains(entry.Block))) {
            entryBlocks.Add(entry.Block);
        }
        Dictionary<IrBlock, IrBlock?> fallthroughByBlock = new();
        foreach (IrBlock block in Blocks) {
            fallthroughByBlock[block] = null;
        }
        return new BlockGraph<IrBlock>(Blocks, entryBlocks, successorsByBlock, fallthroughByBlock,
            Comparer<IrBlock>.Create((first, second) => first.Id.CompareTo(second.Id)),
            block => $"IR block {block.Id} at {block.EntryOrigin.Address}");
    }

    /// <summary>
    /// <see cref="ToBlockGraph"/> plus <see cref="StructuralSuccessors"/>: the block graph the
    /// structurizer and its natural loops run on.
    /// </summary>
    public BlockGraph<IrBlock> ToStructuringGraph() {
        BlockGraph<IrBlock> graph = ToBlockGraph();
        if (StructuralSuccessors.Count == 0) {
            return graph;
        }
        Dictionary<IrBlock, IReadOnlyList<IrBlock>> successorsByBlock = new();
        foreach (IrBlock block in Blocks) {
            List<IrBlock> successors = [.. graph.GetSuccessors(block)];
            if (StructuralSuccessors.TryGetValue(block, out List<IrBlock>? structural)) {
                successors.AddRange(structural.Where(target => !successors.Contains(target)));
            }
            successorsByBlock[block] = successors;
        }
        return new BlockGraph<IrBlock>(Blocks, [.. graph.EntryBlocks], successorsByBlock,
            Blocks.ToDictionary(block => block, block => (IrBlock?)null),
            Comparer<IrBlock>.Create((first, second) => first.Id.CompareTo(second.Id)),
            block => $"IR block {block.Id} at {block.EntryOrigin.Address}");
    }
}
