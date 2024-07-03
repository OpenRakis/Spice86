namespace Spice86.ViewModels;

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

    public CfgCpuViewModel(IUIDispatcherTimerFactory dispatcherTimerFactory, IPerformanceMeasurer performanceMeasurer, IPauseStatus pauseStatus) {
        _pauseStatus = pauseStatus;
        _pauseStatus.PropertyChanged += (sender, args) => {
            if (args.PropertyName == nameof(IPauseStatus.IsPaused) && !_pauseStatus.IsPaused) {
                Graph = null;
            }
        };
        _performanceMeasurer = performanceMeasurer;
        dispatcherTimerFactory.StartNew(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdateCurrentGraph);
    }

    private async void UpdateCurrentGraph(object? sender, EventArgs e) {
        if (_pauseStatus?.IsPaused is false or null) {
            return;
        }
        if (Graph is not null) {
            return;
        }

        await Task.Run(async () => {
            ICfgNode? nodeRoot = ExecutionContext?.LastExecuted;
            if (nodeRoot is null) {
                return;
            }

            long localNumberOfNodes = 0;
            Graph currentGraph = new();
            Queue<ICfgNode> queue = new();
            queue.Enqueue(nodeRoot);
            HashSet<ICfgNode> visitedNodes = new();
            HashSet<(string, string)> existingEdges = new();
            Stopwatch stopwatch = new();
            while (queue.Count > 0) {
                ICfgNode node = queue.Dequeue();
                if (visitedNodes.Contains(node)) {
                    continue;
                }
                visitedNodes.Add(node);
                stopwatch.Restart();
                foreach (ICfgNode successor in node.Successors) {
                    var edgeKey = ($"{node.Address}", $"{successor.Address}");
                    if (!existingEdges.Contains(edgeKey)) {
                        currentGraph.Edges.Add(new Edge(
                            $"{node.Address} {Environment.NewLine} {node.GetType().Name}",
                            $"{successor.Address} {Environment.NewLine} {successor.GetType().Name}"));
                        existingEdges.Add(edgeKey);
                    }
                    if (!visitedNodes.Contains(successor)) {
                        queue.Enqueue(successor);
                    }
                }
                foreach (ICfgNode predecessor in node.Predecessors) {
                    var edgeKey = ($"{predecessor.Address}", $"{node.Address}");
                    if (!existingEdges.Contains(edgeKey)) {
                        currentGraph.Edges.Add(new Edge(
                            $"{predecessor.Address} {Environment.NewLine} {predecessor.GetType().Name}",
                            $"{node.Address} {Environment.NewLine} {node.GetType().Name}"));
                        existingEdges.Add(edgeKey);
                    }
                    if (!visitedNodes.Contains(predecessor)) {
                        queue.Enqueue(predecessor);
                    }
                }
                stopwatch.Stop();
                _performanceMeasurer?.UpdateValue(stopwatch.ElapsedMilliseconds);
                localNumberOfNodes++;
            }

            long averageNodeTime = _performanceMeasurer is not null ? _performanceMeasurer.ValuePerMillisecond : 0;

            await Dispatcher.UIThread.InvokeAsync(() => {
                Graph = currentGraph;
                NumberOfNodes = localNumberOfNodes;
                AverageNodeTime = averageNodeTime;
            });
        }).ConfigureAwait(false);
    }

    public void Visit<T>(T component) where T : IDebuggableComponent {
        ExecutionContext ??= component as ExecutionContext;
    }

    public bool NeedsToVisitEmulator => ExecutionContext is null;
}
