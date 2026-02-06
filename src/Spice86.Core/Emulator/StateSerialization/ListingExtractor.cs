namespace Spice86.Core.Emulator.StateSerialization;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

public class ListingExtractor {
    private readonly NodeToString _nodeToString;

    public ListingExtractor(NodeToString nodeToString) {
        _nodeToString = nodeToString;
    }

    public List<string> ToAssemblyListing(CfgCpu cpu) {
        List<ICfgNode> nodes = DumpInOrder(cpu);
        
        List<string> res = new();
        foreach (ICfgNode node in nodes) {
            res.Add(_nodeToString.ToAssemblyStringWithAddress(node));
        }
        return res;
    }

    private List<ICfgNode> DumpInOrder(CfgCpu cpu) {
        Dictionary<SegmentedAddress, ISet<CfgInstruction>> executionContextEntryPoints = cpu.ExecutionContextManager.ExecutionContextEntryPoints;
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