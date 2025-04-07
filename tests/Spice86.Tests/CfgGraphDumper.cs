namespace Spice86.Tests;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

public class CfgGraphDumper {
    public List<string> ToAssemblyListing(Machine machine) {
        List<ICfgNode> nodes = DumpInOrder(machine);
        AstBuilder astBuilder = new();
        AstInstructionRenderer renderer = new();
        List<string> res = new();
        foreach (ICfgNode node in nodes) {
            string address = node.Address.ToString();
            string instruction = renderer.VisitInstructionNode(node.ToInstructionAst(astBuilder));
            res.Add($"{address} {instruction}");
        }
        return res;
    }

    private List<ICfgNode> DumpInOrder(Machine machine) {
        Dictionary<SegmentedAddress, ISet<CfgInstruction>> executionContextEntryPoints = machine.CfgCpu.ExecutionContextManager.ExecutionContextEntryPoints;
        IEnumerable<CfgInstruction> startNodes = executionContextEntryPoints.Values.SelectMany(i => i);
        ISet<ICfgNode> allNodes = BrowseGraph(startNodes);
        return allNodes.OrderBy(node => node.Address.Linear).ThenByDescending(node => node.Id).ToList();
    }

    private ISet<ICfgNode> BrowseGraph(IEnumerable<CfgInstruction> startNodes) {
        Queue<ICfgNode> queue = new();
        foreach (CfgInstruction start in startNodes) {
            queue.Enqueue(start);
        }
        ISet<ICfgNode> visitedNodes = new HashSet<ICfgNode>();
        while (queue.Count > 0) {
            ICfgNode node = queue.Dequeue();
            visitedNodes.Add(node);
            foreach (ICfgNode successor in node.Successors.Where(successor => !visitedNodes.Contains(successor))) {
                queue.Enqueue(successor);
            }
        }
        return visitedNodes;
    }

}