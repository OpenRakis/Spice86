namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using AvaloniaGraphControl;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Diagnostics;
using Spice86.Shared.Emulator.Memory;

using System.Collections.ObjectModel;
using System.Diagnostics;

public partial class CfgCpuViewModel : ViewModelBase {
    private readonly ExecutionContextManager _executionContextManager;
    private readonly PerformanceMeasurer _performanceMeasurer;
    private readonly NodeToString _nodeToString = new();

    // Collection of searchable nodes for AutoCompleteBox
    private readonly Dictionary<string, ICfgNode> _searchableNodes = new();

    [ObservableProperty] private int _maxNodesToDisplay = 200;

    [ObservableProperty] private Graph? _graph;

    [ObservableProperty] private long _numberOfNodes;

    [ObservableProperty] private long _averageNodeTime;

    [ObservableProperty] private bool _isCfgCpuEnabled;

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private ObservableCollection<string> _nodeEntries = new();

    [ObservableProperty] private string? _selectedNodeEntry;

    [ObservableProperty] private bool _autoFollow = false;
    
    [ObservableProperty] private bool _isLoading;

    public CfgCpuViewModel(Configuration configuration,
        ExecutionContextManager executionContextManager,
        IPauseHandler pauseHandler) {
        _executionContextManager = executionContextManager;
        _performanceMeasurer = new PerformanceMeasurer();
        IsCfgCpuEnabled = configuration.CfgCpu;
        AutoFollow = true;

        pauseHandler.Paused += () => {
            if (AutoFollow) {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    UpdateGraphCommand.Execute(null);
                });
            }
        };
    }

    /// <summary>
    /// Filter function for the AutoCompleteBox
    /// </summary>
    public AutoCompleteFilterPredicate<object?> NodeFilter => (search, item) =>
        string.IsNullOrWhiteSpace(search) ||
        item is string nodeText && nodeText.Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Text selector function for the AutoCompleteBox
    /// </summary>
    public AutoCompleteSelector<object>? NodeItemSelector { get; } = (_, item) => {
        return item?.ToString() ?? "Unknown";
    };

    [RelayCommand]
    private async Task UpdateGraph() {
        Graph = null;
        await UpdateCurrentGraphAsync();
    }

    [RelayCommand]
    private async Task SearchNode() {
        if (string.IsNullOrWhiteSpace(SearchText)) {
            StatusMessage = "Please enter a search term";
            return;
        }

        try {
            IsLoading = true;
            ICfgNode? foundNode = await FindNodeByTextAsync(SearchText);
            if (foundNode != null) {
                StatusMessage = $"Found node: {_nodeToString.ToHeaderString(foundNode)}";
                await RegenerateGraphFromNodeAsync(foundNode);
                IsLoading = false;
            } else {
                StatusMessage = $"No node found matching: {SearchText}";
            }
        } finally {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToSelectedNode() {
        if (SelectedNodeEntry != null && _searchableNodes.TryGetValue(SelectedNodeEntry, out ICfgNode? node)) {
            StatusMessage = $"Navigating to: {_nodeToString.ToHeaderString(node)}";
            IsLoading = true;
            await RegenerateGraphFromNodeAsync(node);
            IsLoading = false;
        }
    }

    private async Task<ICfgNode?> FindNodeByTextAsync(string searchText) {
        return await Task.Run(() => {
            ICfgNode? nodeRoot = _executionContextManager.CurrentExecutionContext?.LastExecuted;
            if (nodeRoot == null) {
                return null;
            }

            Queue<ICfgNode> queue = new();
            queue.Enqueue(nodeRoot);
            HashSet<ICfgNode> visitedNodes = new();

            while (queue.Count > 0) {
                ICfgNode node = queue.Dequeue();
                if (visitedNodes.Contains(node)) {
                    continue;
                }

                visitedNodes.Add(node);

                string nodeText = $"{_nodeToString.ToHeaderString(node)} {_nodeToString.ToAssemblyString(node)}";
                if (nodeText.Contains(searchText, StringComparison.OrdinalIgnoreCase)) {
                    return node;
                }

                foreach (ICfgNode successor in node.Successors) {
                    if (!visitedNodes.Contains(successor)) {
                        queue.Enqueue(successor);
                    }
                }

                foreach (ICfgNode predecessor in node.Predecessors) {
                    if (!visitedNodes.Contains(predecessor)) {
                        queue.Enqueue(predecessor);
                    }
                }
            }

            return null;
        });
    }

    private async Task RegenerateGraphFromNodeAsync(ICfgNode startNode) {
        try {
            IsLoading = true;
            StatusMessage = "Generating graph...";
            
            await Task.Run(async () => {
                long localNumberOfNodes = 0;
                Graph currentGraph = new();
                Queue<ICfgNode> queue = new();
                queue.Enqueue(startNode);
                HashSet<ICfgNode> visitedNodes = new();
                HashSet<(int, int)> existingEdges = new();
                Stopwatch stopwatch = new();

                // Clear the searchable nodes
                _searchableNodes.Clear();

                while (queue.Count > 0 && localNumberOfNodes < MaxNodesToDisplay) {
                    ICfgNode node = queue.Dequeue();
                    if (visitedNodes.Contains(node)) {
                        continue;
                    }

                    visitedNodes.Add(node);
                    stopwatch.Restart();

                    // Format node text with visual indicators for node type
                    string nodeText = FormatNodeText(node, node.Id == _executionContextManager.CurrentExecutionContext?.LastExecuted?.Id);

                    // Add to searchable nodes
                    string searchableText = $"{_nodeToString.ToHeaderString(node)} - {_nodeToString.ToAssemblyString(node)}";
                    _searchableNodes[searchableText] = node;

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
                    IsLoading = false;
                    NumberOfNodes = localNumberOfNodes;
                    AverageNodeTime = averageNodeTime;
                    StatusMessage = $"Graph generated with {localNumberOfNodes} nodes";

                    // Update the node entries for the AutoCompleteBox
                    NodeEntries = new ObservableCollection<string>(_searchableNodes.Keys.OrderBy(k => k));
                });
            });
        } finally {
            IsLoading = false;
        }
    }

    partial void OnMaxNodesToDisplayChanging(int value) => Graph = null;

    private async Task UpdateCurrentGraphAsync() {
        if (Graph is not null && !AutoFollow) {
            return;
        }

        ICfgNode? nodeRoot = _executionContextManager.CurrentExecutionContext?.LastExecuted;
        if (nodeRoot is null) {
            return;
        }

        await RegenerateGraphFromNodeAsync(nodeRoot);
    }

    private Edge CreateEdge(ICfgNode node, ICfgNode successor) {
        string label = string.Empty;

        // Check if it's the current execution node
        ICfgNode? lastExecutedNode = _executionContextManager.CurrentExecutionContext?.LastExecuted;
        bool isNodeLastExecuted = node.Id == lastExecutedNode?.Id;
        bool isSuccessorLastExecuted = successor.Id == lastExecutedNode?.Id;

        // Format node text with visual indicators for node type
        string nodeText = FormatNodeText(node, isNodeLastExecuted);
        string successorText = FormatNodeText(successor, isSuccessorLastExecuted);

        // Add more detailed labels for different edge types
        switch (node) {
            case CfgInstruction cfgInstruction: {
                    SegmentedAddress nextAddress = new SegmentedAddress(cfgInstruction.Address.Segment,
                        (ushort)(cfgInstruction.Address.Offset + cfgInstruction.Length));

                    if (successor.Address != nextAddress) {
                        // Not direct successor - determine edge type
                        if (node is IJumpInstruction) {
                            label = "jump";
                        } else if (node is ICallInstruction) {
                            label = "call";
                        } else if (node is IReturnInstruction) {
                            label = "return";
                        } else {
                            label = "not contiguous";
                        }
                    }
                    break;
                }
            case SelectorNode selectorNode: {
                    Discriminator? discriminator = selectorNode.SuccessorsPerDiscriminator
                        .FirstOrDefault(x => x.Value == successor).Key;
                    label = discriminator?.ToString() ?? "";
                    break;
                }
        }

        return new Edge(nodeText, successorText, label);
    }

    private string FormatNodeText(ICfgNode node, bool isLastExecuted) {
        // Get the basic text representation
        string headerText = _nodeToString.ToHeaderString(node);
        string assemblyText = _nodeToString.ToAssemblyString(node);

        // Add visual indicators for node type and current execution node
        string prefix = "";

        // Mark last executed node
        if (isLastExecuted) {
            prefix += "🔴 last run ";
        }

        // Add node type indicators
        if (node is IJumpInstruction) {
            prefix += "→ jump ";  // Arrow for jump
        } else if (node is ICallInstruction) {
            prefix += "⟱ call ";  // Down arrow for call
        } else if (node is IReturnInstruction) {
            prefix += "⟰ return ";  // Up arrow for return
        } else if (node is SelectorNode) {
            prefix += "☰ selector ";  // Triple horizontal lines for selector
        }

        return $"{prefix}{headerText}{Environment.NewLine}{assemblyText}";
    }

    private static (int, int) GenerateEdgeKey(ICfgNode node, ICfgNode successor)
        => (node.Id, successor.Id);
}