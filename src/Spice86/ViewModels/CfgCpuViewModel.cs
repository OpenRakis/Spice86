namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using AvaloniaGraphControl;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Infrastructure;

public partial class CfgCpuViewModel : ViewModelBase, IInternalDebugger {
    private ExecutionContext? ExecutionContext { get; set; }

    [ObservableProperty]
    private Graph? _graph;

    public CfgCpuViewModel(IUIDispatcherTimerFactory dispatcherTimerFactory) {
        dispatcherTimerFactory.StartNew(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdateCurrentGraph);
    }

    private void UpdateCurrentGraph(object? sender, EventArgs e)
    {
        ICfgNode? nodeRoot = ExecutionContext?.LastExecuted;
        if (nodeRoot?.Predecessors.Count is null or 0) {
            return;
        }
        Graph currentGraph = new();
        // Flatten the recursive Predecessors structure into an array
        List<ICfgNode> flattenedPredecessors = new();
        Stack<ICfgNode> stack = new();
        stack.Push(nodeRoot);
        ICfgNode? previousNode = nodeRoot;
        while (stack.Count > 0) {
            ICfgNode node = stack.Pop();
            flattenedPredecessors.Add(node);
            if (previousNode != null) {
                currentGraph.Edges.Add(new Edge(
                    $"{node.Address} {Environment.NewLine} {node.GetType()}",
                    $"{previousNode.Address} {Environment.NewLine} {previousNode.GetType()}"));
            }
            foreach (ICfgNode predecessor in node.Predecessors) {
                stack.Push(predecessor);
            }
            previousNode = node;
        }
        Graph = currentGraph;
    }

    public void Visit<T>(T component) where T : IDebuggableComponent {
        ExecutionContext ??= component as ExecutionContext;
    }

    public bool NeedsToVisitEmulator => ExecutionContext is null;
}
