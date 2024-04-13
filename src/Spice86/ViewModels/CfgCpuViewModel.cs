namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using AvaloniaGraphControl;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.Shared.Diagnostics;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

public partial class CfgCpuViewModel : ViewModelBase, IInternalDebugger {
    private readonly IPerformanceMeasurer? _performanceMeasurer = new PerformanceMeasurer();
    private ExecutionContext? ExecutionContext { get; set; }

    [ObservableProperty]
    private Graph? _graph;
    
    [ObservableProperty]
    private long _numberOfNodes = 0;

    [ObservableProperty]
    private long _averageNodeTime = 0;

    private readonly IPauseStatus? _pauseStatus;

    public CfgCpuViewModel() {
        if(!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }

    public CfgCpuViewModel(IUIDispatcherTimerFactory dispatcherTimerFactory, IPerformanceMeasurer performanceMeasurer, IPauseStatus pauseStatus) {
        _pauseStatus = pauseStatus;
        _performanceMeasurer = performanceMeasurer;
        dispatcherTimerFactory.StartNew(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdateCurrentGraph);
    }

    private void UpdateCurrentGraph(object? sender, EventArgs e)
    {
        if (_pauseStatus?.IsPaused is false or null)
        {
            return;
        }
        ICfgNode? nodeRoot = ExecutionContext?.LastExecuted;
        if (nodeRoot is null)
        {
            return;
        }

        NumberOfNodes = 0;
        Graph currentGraph = new();
        Queue<ICfgNode> queue = new();
        queue.Enqueue(nodeRoot);
        HashSet<ICfgNode> visitedNodes = new();
        var stopwatch = Stopwatch.StartNew();
        while (queue.Count > 0)
        {
            ICfgNode node = queue.Dequeue();
            if (visitedNodes.Contains(node))
            {
                continue;
            }
            visitedNodes.Add(node);
            stopwatch.Restart();
            foreach (ICfgNode successor in node.Successors)
            {
                currentGraph.Edges.Add(new Edge(
                    $"{node.Address} {Environment.NewLine} {node.GetType().Name}",
                    $"{successor.Address} {Environment.NewLine} {successor.GetType().Name}"));
                if (!visitedNodes.Contains(successor))
                {
                    queue.Enqueue(successor);
                }
            }
            foreach (ICfgNode predecessor in node.Predecessors)
            {
                currentGraph.Edges.Add(new Edge(
                    $"{predecessor.Address} {Environment.NewLine} {predecessor.GetType().Name}",
                    $"{node.Address} {Environment.NewLine} {node.GetType().Name}"));
                if (!visitedNodes.Contains(predecessor))
                {
                    queue.Enqueue(predecessor);
                }
            }
            stopwatch.Stop();
            _performanceMeasurer?.UpdateValue(stopwatch.ElapsedMilliseconds);
            NumberOfNodes++;
        }
        Graph = currentGraph;
        if (_performanceMeasurer is not null) {
            AverageNodeTime = _performanceMeasurer.ValuePerMillisecond;
        }
    }

    public void Visit<T>(T component) where T : IDebuggableComponent {
        ExecutionContext ??= component as ExecutionContext;
    }

    public bool NeedsToVisitEmulator => ExecutionContext is null;
}
