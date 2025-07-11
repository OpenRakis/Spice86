﻿namespace Spice86.ViewModels;

using Avalonia.Threading;

using AvaloniaGraphControl;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Diagnostics;
using Spice86.Shared.Emulator.Memory;

using System.Diagnostics;

public partial class CfgCpuViewModel : ViewModelBase {
    private readonly ExecutionContextManager _executionContextManager;
    private readonly PerformanceMeasurer _performanceMeasurer;
    private readonly NodeToString _nodeToString = new();

    [ObservableProperty] private int _maxNodesToDisplay = 200;

    [ObservableProperty] private Graph? _graph;

    [ObservableProperty] private long _numberOfNodes;

    [ObservableProperty] private long _averageNodeTime;

    [ObservableProperty] private bool _isCfgCpuEnabled;

    public CfgCpuViewModel(Configuration configuration, ExecutionContextManager executionContextManager, IPauseHandler pauseHandler) {
        _executionContextManager = executionContextManager;
        _performanceMeasurer = new PerformanceMeasurer();
        IsCfgCpuEnabled = configuration.CfgCpu;

        pauseHandler.Paused += () => UpdateGraphCommand.Execute(null);
    }

    [RelayCommand]
    private async Task UpdateGraph() {
        Graph = null;
        await UpdateCurrentGraphAsync();
    }

    partial void OnMaxNodesToDisplayChanging(int value) => Graph = null;

    private async Task UpdateCurrentGraphAsync() {
        if (Graph is not null) {
            return;
        }
        
        await Task.Run(async () => {
            ICfgNode? nodeRoot = _executionContextManager.CurrentExecutionContext?.LastExecuted;
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
                    (int, int) edgeKey = GenerateEdgeKey(node, successor);
                    if (!existingEdges.Contains(edgeKey)) {
                        currentGraph.Edges.Add(CreateEdge(node, successor));
                        existingEdges.Add(edgeKey);
                    }

                    if (!visitedNodes.Contains(successor)) {
                        queue.Enqueue(successor);
                    }
                }

                foreach (ICfgNode predecessor in node.Predecessors) {
                    (int, int) edgeKey = GenerateEdgeKey(predecessor, node);
                    if (!existingEdges.Contains(edgeKey)) {
                        currentGraph.Edges.Add(CreateEdge(predecessor, node));
                        existingEdges.Add(edgeKey);
                    }

                    if (!visitedNodes.Contains(predecessor)) {
                        queue.Enqueue(predecessor);
                    }
                }

                stopwatch.Stop();
                _performanceMeasurer.UpdateValue(stopwatch.ElapsedMilliseconds);
                localNumberOfNodes++;
            }

            long averageNodeTime = _performanceMeasurer.ValuePerMillisecond;

            await Dispatcher.UIThread.InvokeAsync(() => {
                Graph = currentGraph;
                NumberOfNodes = localNumberOfNodes;
                AverageNodeTime = averageNodeTime;
            });
        });
    }

    private Edge CreateEdge(ICfgNode node, ICfgNode successor) {
        string label = string.Empty;
        switch (node) {
            case CfgInstruction cfgInstruction: {
                SegmentedAddress nextAddress = new SegmentedAddress(cfgInstruction.Address.Segment,
                    (ushort)(cfgInstruction.Address.Offset + cfgInstruction.Length));
                if (successor.Address != nextAddress) {
                    // Not direct successor, jump or call
                    label = "not contiguous";
                }

                break;
            }
            case SelectorNode selectorNode: {
                Discriminator discriminator = selectorNode.SuccessorsPerDiscriminator
                    .FirstOrDefault(x => x.Value == successor).Key;
                label = discriminator.ToString();
                break;
            }
        }

        return new Edge(GenerateNodeText(node), GenerateNodeText(successor), label);
    }

    private static (int, int) GenerateEdgeKey(ICfgNode node, ICfgNode successor)
        => (node.Id, successor.Id);

    private string GenerateNodeText(ICfgNode node) => $"{_nodeToString.ToHeaderString(node)}{Environment.NewLine}{_nodeToString.ToAssemblyString(node)}";
}