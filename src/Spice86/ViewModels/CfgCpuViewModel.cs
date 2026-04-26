namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;

using AvaloniaGraphControl;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Iced.Intel;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.ViewModels.Services;
using Spice86.ViewModels.TextPresentation;

using System.Diagnostics;

public partial class CfgCpuViewModel : ViewModelBase {
    private readonly List<NodeTableEntry> _tableNodesList = new();
    private readonly IUIDispatcher _uiDispatcher;
    private readonly ExecutionContextManager _executionContextManager;
    private readonly NodeToString _nodeToString;
    private readonly AstFormattedTextTokensRenderer _textOffsetsRenderer;

    // Collection of searchable nodes for AutoCompleteBox
    private Dictionary<string, ICfgNode> _searchableNodes = new();

    [ObservableProperty] private int _maxNodesToDisplay = 20;

    [ObservableProperty] private Graph? _graph;

    [ObservableProperty] private long _numberOfNodes;

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

    public CfgCpuViewModel(IUIDispatcher uiDispatcher,
        ExecutionContextManager executionContextManager,
        IPauseHandler pauseHandler,
        NodeToString nodeToString,
        AsmRenderingConfig asmRenderingConfig) {
        _nodeToString = nodeToString;
        _textOffsetsRenderer = new AstFormattedTextTokensRenderer(asmRenderingConfig);
        _uiDispatcher = uiDispatcher;
        _executionContextManager = executionContextManager;
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
        FilterTableNodes();
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
                Dictionary<int, CfgGraphNode> graphNodeCache = new();
                Dictionary<string, ICfgNode> localSearchableNodes = new();
                List<NodeTableEntry> localTableNodesList = new();

                while (queue.Count > 0 && localNumberOfNodes < MaxNodesToDisplay) {
                    ICfgNode node = queue.Dequeue();
                    if (visitedNodes.Contains(node)) {
                        continue;
                    }

                    visitedNodes.Add(node);

                    bool isLastExecuted =
                        node.Id == _executionContextManager.CurrentExecutionContext?.LastExecuted?.Id;
                    GetOrCreateGraphNode(node, isLastExecuted, graphNodeCache);

                    string searchableText =
                        $"{_nodeToString.ToHeaderString(node)} - {_nodeToString.ToAssemblyString(node)}";
                    localSearchableNodes[searchableText] = node;

                    localTableNodesList.Add(CreateTableEntry(node));

                    foreach (ICfgNode successor in node.Successors) {
                        (int, int) edgeKey = GenerateEdgeKey(node, successor);
                        if (!existingEdges.Contains(edgeKey)) {
                            currentGraph.Edges.Add(CreateEdge(node, successor, graphNodeCache));
                            existingEdges.Add(edgeKey);
                        }

                        if (!visitedNodes.Contains(successor)) {
                            queue.Enqueue(successor);
                        }
                    }

                    foreach (ICfgNode predecessor in node.Predecessors) {
                        (int, int) edgeKey = GenerateEdgeKey(predecessor, node);
                        if (!existingEdges.Contains(edgeKey)) {
                            currentGraph.Edges.Add(CreateEdge(predecessor, node, graphNodeCache));
                            existingEdges.Add(edgeKey);
                        }

                        if (!visitedNodes.Contains(predecessor)) {
                            queue.Enqueue(predecessor);
                        }
                    }

                    localNumberOfNodes++;
                }


                await _uiDispatcher.InvokeAsync(() => {
                    _searchableNodes = new Dictionary<string, ICfgNode>(localSearchableNodes);

                    _tableNodesList.Clear();
                    _tableNodesList.AddRange(localTableNodesList);

                    Graph = currentGraph;
                    IsLoading = false;
                    NumberOfNodes = localNumberOfNodes;
                    StatusMessage = $"Graph generated with {localNumberOfNodes} nodes";

                    NodeEntries.Clear();
                    NodeEntries.AddRange(_searchableNodes.Keys.OrderBy(k => k));

                    FilterTableNodes();
                });
            });
        } finally {
            IsLoading = false;
        }
    }

    private void FilterTableNodes() {
        if (string.IsNullOrWhiteSpace(TableFilter)) {
            TableNodes.Clear();
            TableNodes.AddRange(_tableNodesList);
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

    private Edge CreateEdge(ICfgNode node, ICfgNode successor, Dictionary<int, CfgGraphNode> graphNodeCache) {
        string labelText = string.Empty;
        CfgEdgeType edgeType = DetermineEdgeType(node);

        ICfgNode? lastExecutedNode = _executionContextManager.CurrentExecutionContext?.LastExecuted;
        bool isNodeLastExecuted = node.Id == lastExecutedNode?.Id;
        bool isSuccessorLastExecuted = successor.Id == lastExecutedNode?.Id;

        CfgGraphNode nodeGraphNode = GetOrCreateGraphNode(node, isNodeLastExecuted, graphNodeCache);
        CfgGraphNode successorGraphNode = GetOrCreateGraphNode(successor, isSuccessorLastExecuted, graphNodeCache);

        switch (node) {
            case CfgInstruction cfgInstruction:
                List<InstructionSuccessorType> keys = cfgInstruction
                    .SuccessorsPerType
                    .Where(kvp => kvp.Value.Contains(successor))
                    .Select(kvp => kvp.Key)
                    .ToList();
                labelText = string.Join(", ", keys);
                // Refine edge type based on successor relationship
                if (keys.Contains(InstructionSuccessorType.CallToReturn) ||
                    keys.Contains(InstructionSuccessorType.CallToMisalignedReturn)) {
                    edgeType = CfgEdgeType.CallToReturn;
                } else if (keys.Contains(InstructionSuccessorType.CpuFault)) {
                    edgeType = CfgEdgeType.CpuFault;
                }
                break;
            case SelectorNode selectorNode: {
                    Signature? signature = selectorNode.SuccessorsPerSignature
                        .FirstOrDefault(x => x.Value.Id == successor.Id).Key;
                    labelText = signature?.ToString() ?? "";
                    edgeType = CfgEdgeType.Selector;
                    break;
                }
        }

        CfgGraphEdgeLabel edgeLabel = new() { Text = labelText, EdgeType = edgeType };
        return new Edge(nodeGraphNode, successorGraphNode, edgeLabel);
    }

    private static CfgNodeType ClassifyNodeType(ICfgNode node) {
        return node switch {
            CfgInstruction instr when instr.IsJump => CfgNodeType.Jump,
            CfgInstruction instr when instr.IsCall => CfgNodeType.Call,
            CfgInstruction instr when instr.IsReturn => CfgNodeType.Return,
            SelectorNode => CfgNodeType.Selector,
            _ => CfgNodeType.Instruction
        };
    }

    private static CfgEdgeType DetermineEdgeType(ICfgNode node) {
        return ClassifyNodeType(node) switch {
            CfgNodeType.Jump => CfgEdgeType.Jump,
            CfgNodeType.Call => CfgEdgeType.Call,
            CfgNodeType.Return => CfgEdgeType.Return,
            CfgNodeType.Selector => CfgEdgeType.Selector,
            _ => CfgEdgeType.Normal
        };
    }

    private CfgGraphNode GetOrCreateGraphNode(ICfgNode node, bool isLastExecuted,
        Dictionary<int, CfgGraphNode> cache) {
        if (cache.TryGetValue(node.Id, out CfgGraphNode? existing)) {
            return existing;
        }

        CfgGraphNode graphNode = CreateGraphNode(node, isLastExecuted);
        cache[node.Id] = graphNode;
        return graphNode;
    }

    private CfgGraphNode CreateGraphNode(ICfgNode node, bool isLastExecuted) {
        List<FormattedTextToken> textOffsets = [];

        // Prefix line
        if (isLastExecuted) {
            textOffsets.Add(new() { Text = "🔴 last run ", Kind = FormatterTextKind.Text });
        }

        CfgNodeType nodeType = ClassifyNodeType(node);
        switch (nodeType) {
            case CfgNodeType.Jump:
                textOffsets.Add(new() { Text = "\u2192 jump ", Kind = FormatterTextKind.Mnemonic });
                break;
            case CfgNodeType.Call:
                textOffsets.Add(new() { Text = "\u27b1 call ", Kind = FormatterTextKind.Mnemonic });
                break;
            case CfgNodeType.Return:
                textOffsets.Add(new() { Text = "\u27f0 return ", Kind = FormatterTextKind.Mnemonic });
                break;
            case CfgNodeType.Selector:
                textOffsets.Add(new() { Text = "\u2630 selector ", Kind = FormatterTextKind.Keyword });
                break;
        }

        // Header: address / id
        textOffsets.Add(new() { Text = node.Address.ToString(), Kind = FormatterTextKind.FunctionAddress });
        textOffsets.Add(new() { Text = " / ", Kind = FormatterTextKind.Punctuation });
        textOffsets.Add(new() { Text = node.Id.ToString(), Kind = FormatterTextKind.Number });
        textOffsets.Add(new() { Text = Environment.NewLine, Kind = FormatterTextKind.Text });

        // Assembly instruction (syntax-highlighted via AST renderer)
        InstructionNode ast = node.DisplayAst;
        textOffsets.AddRange(ast.Accept(_textOffsetsRenderer));

        return new CfgGraphNode {
            NodeId = node.Id,
            TextOffsets = textOffsets,
            IsLastExecuted = isLastExecuted,
            NodeType = nodeType
        };
    }

    private string FormatNodeText(ICfgNode node, bool isLastExecuted) {
        string headerText = _nodeToString.ToHeaderString(node);
        string assemblyText = _nodeToString.ToAssemblyString(node);

        string prefix = "";

        if (isLastExecuted) {
            prefix += "🔴 last run ";
        }

        switch (ClassifyNodeType(node)) {
            case CfgNodeType.Jump:
                prefix += "\u2192 jump ";
                break;
            case CfgNodeType.Call:
                prefix += "\u27b1 call ";
                break;
            case CfgNodeType.Return:
                prefix += "\u27f0 return ";
                break;
            case CfgNodeType.Selector:
                prefix += "\u2630 selector ";
                break;
        }

        return $"{prefix}{headerText}{Environment.NewLine}{assemblyText}";
    }

    private static (int, int) GenerateEdgeKey(ICfgNode node, ICfgNode successor)
        => (node.Id, successor.Id);

    private NodeTableEntry CreateTableEntry(ICfgNode node) {
        string nodeType = ClassifyNodeType(node).ToString();

        bool isLastExecuted = node.Id == _executionContextManager.CurrentExecutionContext?.LastExecuted?.Id;

        AvaloniaList<NodeTableEntry> predecessors = new();
        foreach (ICfgNode predecessor in node.Predecessors) {
            predecessors.Add(new NodeTableEntry {
                Address = $"0x{predecessor.Address}",
                Assembly = _nodeToString.ToAssemblyString(predecessor),
                Node = predecessor
            });
        }

        AvaloniaList<NodeTableEntry> successors = new();
        foreach (ICfgNode successor in node.Successors) {
            successors.Add(new NodeTableEntry {
                Address = $"0x{successor.Address}",
                Assembly = _nodeToString.ToAssemblyString(successor),
                Node = successor
            });
        }

        return new NodeTableEntry {
            Address = $"0x{node.Address}",
            Assembly = _nodeToString.ToAssemblyString(node),
            Type = nodeType,
            Predecessors = predecessors,
            Successors = successors,
            IsLastExecuted = isLastExecuted,
            Node = node
        };
    }

    public record NodeTableEntry {
        public string Address { get; init; } = string.Empty;
        public string Assembly { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public AvaloniaList<NodeTableEntry> Predecessors { get; init; } = new();
        public AvaloniaList<NodeTableEntry> Successors { get; init; } = new();

        public bool IsLastExecuted { get; init; }
        public ICfgNode? Node { get; init; }

        public string PredecessorsText => string.Join(Environment.NewLine, Predecessors.Select(p => p.Address));
        public string SuccessorsText => string.Join(Environment.NewLine, Successors.Select(s => s.Address));
    }
}