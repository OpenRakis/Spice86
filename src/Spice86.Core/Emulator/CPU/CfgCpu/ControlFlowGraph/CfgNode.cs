namespace Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

public abstract class CfgNode : ICfgNode {
    private static int _nextId;
    public CfgNode(SegmentedAddress address) {
        Address = address;
        Id = _nextId++;
    }

    public int Id { get; }
    public HashSet<ICfgNode> Predecessors { get; } = new();
    public HashSet<ICfgNode> Successors { get; } = new();
    public SegmentedAddress Address { get; }

    public abstract bool IsAssembly { get; }
    
    public abstract void UpdateSuccessorCache();

    public abstract void Execute(InstructionExecutionHelper helper);
}