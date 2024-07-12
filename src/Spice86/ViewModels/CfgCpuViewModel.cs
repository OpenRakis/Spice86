namespace Spice86.ViewModels;

using Avalonia.Threading;

using AvaloniaGraphControl;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Infrastructure;
using Spice86.Interfaces;
using Spice86.Shared.Diagnostics;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

public partial class CfgCpuViewModel : ViewModelBase, IInternalDebugger {
    private readonly IPerformanceMeasurer? _performanceMeasurer = new PerformanceMeasurer();
    private ExecutionContext? ExecutionContext { get; set; }

    [ObservableProperty]
    private int _maxNodesToDisplay = 200;

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

    partial void OnMaxNodesToDisplayChanging(int value) {
        Graph = null;
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
            HashSet<(int, int)> existingEdges = new();
            Stopwatch stopwatch = new();
            while (queue.Count > 0 && localNumberOfNodes < MaxNodesToDisplay) {
                ICfgNode node = queue.Dequeue();
                if (visitedNodes.Contains(node)) {
                    continue;
                }
                visitedNodes.Add(node);
                stopwatch.Restart();
                foreach (ICfgNode successor in node.Successors) {
                    var edgeKey = GenerateEdgeKey(node, successor);
                    if (!existingEdges.Contains(edgeKey)) {
                        currentGraph.Edges.Add(CreateEdge(node, successor));
                        existingEdges.Add(edgeKey);
                    }
                    if (!visitedNodes.Contains(successor)) {
                        queue.Enqueue(successor);
                    }
                }
                foreach (ICfgNode predecessor in node.Predecessors) {
                    var edgeKey = GenerateEdgeKey(predecessor, node);
                    if (!existingEdges.Contains(edgeKey)) {
                        currentGraph.Edges.Add(CreateEdge(predecessor, node));
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

    private Edge CreateEdge(ICfgNode node, ICfgNode successor) {
        string label = string.Empty;
        if (node is CfgInstruction cfgInstruction) {
            SegmentedAddress nextAddress = new SegmentedAddress(cfgInstruction.Address.Segment, (ushort)(cfgInstruction.Address.Offset + cfgInstruction.Length));
            if (successor.Address.ToPhysical() != nextAddress.ToPhysical()) {
                // Not direct successor, jump or call
                label = "not contiguous";
            }
        }
        if (node is DiscriminatedNode discriminatedNode) {
            Discriminator discriminator = discriminatedNode.SuccessorsPerDiscriminator.FirstOrDefault(x => x.Value == successor).Key;
            label = discriminator.ToString();
        }
        return new Edge(GenerateNodeText(node), GenerateNodeText(successor), label);
    }

    private (int, int) GenerateEdgeKey(ICfgNode node, ICfgNode successor) {
        return (node.Id, successor.Id);
    }

    private string GenerateNodeText(ICfgNode node) {
        return $"{node.Address} / {node.Id} {Environment.NewLine} {node.GetType().Name}";
    }

    public void Visit<T>(T component) where T : IDebuggableComponent {
        ExecutionContext ??= component as ExecutionContext;
    }

    public bool NeedsToVisitEmulator => ExecutionContext is null;
}
