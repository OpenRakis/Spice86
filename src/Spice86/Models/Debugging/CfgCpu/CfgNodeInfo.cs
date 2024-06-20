namespace Spice86.Models.Debugging.CfgCpu;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

using System;
using System.Collections.Generic;

internal partial class CfgNodeInfo : ObservableObject, ICfgNode {

    public CfgNodeInfo() {
        Predecessors = new HashSet<ICfgNode>();
        Successors = new HashSet<ICfgNode>();
        Address = new SegmentedAddress(0, 0);
        IsAssembly = false;
    }

    public CfgNodeInfo(ICfgNode nodeToExecuteNextAccordingToGraph) {
        Predecessors = nodeToExecuteNextAccordingToGraph.Predecessors;
        Successors = nodeToExecuteNextAccordingToGraph.Successors;
        Address = nodeToExecuteNextAccordingToGraph.Address;
        IsAssembly = nodeToExecuteNextAccordingToGraph.IsAssembly;
    }

    [ObservableProperty]
    private HashSet<ICfgNode> _predecessors;
    [ObservableProperty]
    private HashSet<ICfgNode> _successors;
    [ObservableProperty]
    private SegmentedAddress _address;
    [ObservableProperty]
    private bool _isAssembly;

    public void UpdateSuccessorCache() {
        throw new NotSupportedException();
    }

    public void Execute(InstructionExecutionHelper helper) {
        throw new NotSupportedException();
    }
}
