namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;

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
using Spice86.ViewModels.Services;

using System.Diagnostics;

public partial class CfgCpuViewModel : ViewModelBase {
    private readonly List<NodeTableEntry> _tableNodesList = new();
    private readonly IUIDispatcher _uiDispatcher;
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

    [ObservableProperty] private AvaloniaList<string> _nodeEntries = new();

    [ObservableProperty] private string? _selectedNodeEntry;

    [ObservableProperty] private bool _autoFollow = false;
    
    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private string _tableFilter = string.Empty;
    
    [ObservableProperty] private AvaloniaList<NodeTableEntry> _tableNodes = new();
    
    [ObservableProperty] private NodeTableEntry? _selectedTableNode;
    
    [ObservableProperty] private int _selectedTabIndex;

    public CfgCpuViewModel(Configuration configuration,
        IUIDispatcher uiDispatcher,
        ExecutionContextManager executionContextManager,
        IPauseHandler pauseHandler) {
        _uiDispatcher = uiDispatcher;
        _executionContextManager = executionContextManager;
        _performanceMeasurer = new PerformanceMeasurer();
        IsCfgCpuEnabled = configuration.CfgCpu;
        AutoFollow = true;

        pauseHandler.Paused += () => {
            if (AutoFollow) {
                _uiDispatcher.Post(() => {
                    UpdateGraphCommand.Execute(null);
                });
            }
        };
    }

    public AutoCompleteFilterPredicate<object?> NodeFilter => (search, item) =>
        string.IsNullOrWhiteSpace(search) ||
        item is string nodeText && nodeText.Contains(search, StringComparison.OrdinalIgnoreCase);

    public AutoCompleteSelector<object>? NodeItemSelector { get; } = (_, item) => {
        return item?.ToString() ?? "Unknown";
    };

    partial void OnTableFilterChanged(string value) {
        UpdateTableNodes();
    }

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
    
    [RelayCommand]
    private async Task NavigateToTableNode(NodeTableEntry? node) {
        if (node?.Node != null) {
            StatusMessage = $"Navigating to: {_nodeToString.ToHeaderString(node.Node)}";
            IsLoading = true;
            await RegenerateGraphFromNodeAsync(node.Node);
            // Switch to graph view
            SelectedTabIndex = 0;
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

                _searchableNodes.Clear();
                _tableNodesList.Clear();

                while (queue.Count > 0 && localNumberOfNodes < MaxNodesToDisplay) {
                    ICfgNode node = queue.Dequeue();
                    if (visitedNodes.Contains(node)) {
                        continue;
                    }

                    visitedNodes.Add(node);
                    stopwatch.Restart();

                    string nodeText = FormatNodeText(node, node.Id == _executionContextManager.CurrentExecutionContext?.LastExecuted?.Id);

                    string searchableText = $"{_nodeToString.ToHeaderString(node)} - {_nodeToString.ToAssemblyString(node)}";
                    _searchableNodes[searchableText] = node;

                    _tableNodesList.Add(CreateTableEntry(node));

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

                await _uiDispatcher.InvokeAsync(() => {
                    Graph = currentGraph;
                    IsLoading = false;
                    NumberOfNodes = localNumberOfNodes;
                    AverageNodeTime = averageNodeTime;
                    StatusMessage = $"Graph generated with {localNumberOfNodes} nodes";

                    NodeEntries = new AvaloniaList<string>(_searchableNodes.Keys.OrderBy(k => k));
                    
                    TableNodes = new AvaloniaList<NodeTableEntry>(_tableNodesList);
                    UpdateTableNodes();
                });
            });
        } finally {
            IsLoading = false;
        }
    }
    
    private void UpdateTableNodes() {
        if (string.IsNullOrWhiteSpace(TableFilter)) {
            TableNodes = new AvaloniaList<NodeTableEntry>(_tableNodesList);
            return;
        }
        
        string filter = TableFilter;

        TableNodes = new AvaloniaList<NodeTableEntry>(
            _tableNodesList.Where(n => 
                (n.Assembly.Contains(filter, StringComparison.InvariantCultureIgnoreCase)) || 
                n.Address.Contains(filter, StringComparison.InvariantCultureIgnoreCase) ||
                n.Type.Contains(filter, StringComparison.InvariantCultureIgnoreCase))
        );
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

        ICfgNode? lastExecutedNode = _executionContextManager.CurrentExecutionContext?.LastExecuted;
        bool isNodeLastExecuted = node.Id == lastExecutedNode?.Id;
        bool isSuccessorLastExecuted = successor.Id == lastExecutedNode?.Id;

        string nodeText = FormatNodeText(node, isNodeLastExecuted);
        string successorText = FormatNodeText(successor, isSuccessorLastExecuted);

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
        string headerText = _nodeToString.ToHeaderString(node);
        string assemblyText = _nodeToString.ToAssemblyString(node);

        string prefix = "";

        if (isLastExecuted) {
            prefix += "🔴 last run ";
        }

        if (node is IJumpInstruction) {
            prefix += "→ jump ";
        } else if (node is ICallInstruction) {
            prefix += "⟱ call ";
        } else if (node is IReturnInstruction) {
            prefix += "⟰ return ";
        } else if (node is SelectorNode) {
            prefix += "☰ selector ";
        }

        return $"{prefix}{headerText}{Environment.NewLine}{assemblyText}";
    }

    private static (int, int) GenerateEdgeKey(ICfgNode node, ICfgNode successor)
        => (node.Id, successor.Id);
        
    private NodeTableEntry CreateTableEntry(ICfgNode node) {
        string nodeType = "Instruction";
        if (node is IJumpInstruction) {
            nodeType = "Jump";
        } else if (node is ICallInstruction) {
            nodeType = "Call";
        } else if (node is IReturnInstruction) {
            nodeType = "Return";
        } else if (node is SelectorNode) {
            nodeType = "Selector";
        }
        
        bool isLastExecuted = node.Id == _executionContextManager.CurrentExecutionContext?.LastExecuted?.Id;
        
        return new NodeTableEntry {
            Address = $"0x{node.Address}",
            Assembly = _nodeToString.ToAssemblyString(node),
            Type = nodeType,
            PredecessorsCount = node.Predecessors.Count,
            SuccessorsCount = node.Successors.Count,
            IsLastExecuted = isLastExecuted,
            Node = node
        };
    }

    public record NodeTableEntry {
        public string Address { get; init; } = string.Empty;
        public string Assembly { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public int PredecessorsCount { get; init; }
        public int SuccessorsCount { get; init; }
        public bool IsLastExecuted { get; init; }
        public ICfgNode? Node { get; init; }
    }
}