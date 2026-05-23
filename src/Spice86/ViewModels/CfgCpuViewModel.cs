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
using Spice86.Core.Emulator.StateSerialization.ControlFlow;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.ViewModels.DataModels;
using Spice86.ViewModels.Enums;
using Spice86.ViewModels.Services;
using Spice86.ViewModels.TextPresentation;

using System.Diagnostics;

public partial class CfgCpuViewModel : ViewModelBase {
    private readonly List<NodeTableEntry> _tableNodesList = new();
    private readonly IUIDispatcher _uiDispatcher;
    private readonly ExecutionContextManager _executionContextManager;
    private readonly NodeToString _nodeToString;
    private readonly AstFormattedTextTokensRenderer _textOffsetsRenderer;
    private readonly CfgBlockGraphExporter _graphExporter;

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
        AsmRenderingConfig asmRenderingConfig,
        CfgBlockGraphExporter graphExporter) {
        _nodeToString = nodeToString;
        _textOffsetsRenderer = new AstFormattedTextTokensRenderer(asmRenderingConfig);
        _uiDispatcher = uiDispatcher;
        _executionContextManager = executionContextManager;
        _graphExporter = graphExporter;
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
            ICfgNode? nodeRoot = _executionContextManager.ExecutingNode;
            if (nodeRoot is null) {
                return null;
            }

            CfgBlockGraph exportedGraph = _graphExporter.ExportFromNode(nodeRoot, null, null);
            foreach (CfgBlockGraphNode graphNode in exportedGraph.Blocks) {
                CfgBlock block = graphNode.Block;
                foreach (ICfgNode instruction in block.Instructions) {
                    string nodeText =
                        $"{_nodeToString.ToHeaderString(instruction)} {_nodeToString.ToAssemblyString(instruction)}";
                    if (nodeText.Contains(searchText, StringComparison.OrdinalIgnoreCase)) {
                        return (ICfgNode)block;
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
                ICfgNode? executingNode = _executionContextManager.ExecutingNode;
                CfgBlockGraph exportedGraph = _graphExporter.ExportFromNode(startNode, executingNode, MaxNodesToDisplay);

                Graph currentGraph = new();
                HashSet<int> blockIdsWithEdges = new();
                Dictionary<int, CfgGraphNode> graphNodeCache = new();
                Dictionary<string, ICfgNode> localSearchableNodes = new();
                List<NodeTableEntry> localTableNodesList = new();

                // Build UI graph nodes from exported blocks
                foreach (CfgBlockGraphNode exportedNode in exportedGraph.Blocks) {
                    CfgBlock block = exportedNode.Block;
                    GetOrCreateBlockGraphNode(block, exportedNode.IsExecutingBlock, graphNodeCache);

                    localTableNodesList.Add(CreateBlockHeaderTableEntry(block, exportedNode.IsExecutingBlock));
                    foreach (ICfgNode instruction in block.Instructions) {
                        string searchableText =
                            $"{_nodeToString.ToHeaderString(instruction)} - {_nodeToString.ToAssemblyString(instruction)}";
                        localSearchableNodes[searchableText] = instruction;
                        localTableNodesList.Add(CreateTableEntry(instruction));
                    }
                }

                // Build UI edges from exported edges
                foreach (CfgBlockGraphEdge exportedEdge in exportedGraph.Edges) {
                    bool isFromExecuting = graphNodeCache.TryGetValue(exportedEdge.From.Id, out CfgGraphNode? fromNode)
                        && fromNode.IsExecuting;
                    bool isToExecuting = graphNodeCache.TryGetValue(exportedEdge.To.Id, out CfgGraphNode? toNode)
                        && toNode.IsExecuting;
                    currentGraph.Edges.Add(CreateBlockEdge(
                        exportedEdge.From, exportedEdge.To, exportedEdge.BridgeNode,
                        isFromExecuting, isToExecuting, graphNodeCache));
                    blockIdsWithEdges.Add(exportedEdge.From.Id);
                    blockIdsWithEdges.Add(exportedEdge.To.Id);
                }

                // AvaloniaGraphControl only renders nodes that appear as edge endpoints.
                // A block with no predecessors or successors (e.g. the very first block
                // still being constructed) would be silently invisible. Add a self-loop
                // edge for every such isolated block so the graph panel renders it.
                foreach (KeyValuePair<int, CfgGraphNode> entry in graphNodeCache) {
                    if (!blockIdsWithEdges.Contains(entry.Key)) {
                        currentGraph.Edges.Add(new Edge(entry.Value, entry.Value,
                            new CfgGraphEdgeLabel { EdgeType = CfgEdgeType.IsolatedNodeLoop }, Edge.Symbol.None, Edge.Symbol.None));
                    }
                }

                long localNumberOfBlocks = exportedGraph.Blocks.Length;

                await _uiDispatcher.InvokeAsync(() => {
                    _searchableNodes = new Dictionary<string, ICfgNode>(localSearchableNodes);

                    _tableNodesList.Clear();
                    _tableNodesList.AddRange(localTableNodesList);

                    Graph = currentGraph;
                    IsLoading = false;
                    NumberOfNodes = localNumberOfBlocks;
                    StatusMessage = $"Graph generated with {localNumberOfBlocks} blocks";

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

        ICfgNode? nodeRoot = _executionContextManager.ExecutingNode;
        if (nodeRoot is null) {
            return;
        }

        await RegenerateGraphFromNodeAsync(nodeRoot);
    }

    private Edge CreateBlockEdge(CfgBlock fromBlock, CfgBlock toBlock, ICfgNode bridgeSuccessor,
        bool isFromExecuting, bool isToExecuting,
        Dictionary<int, CfgGraphNode> graphNodeCache) {
        // Edge label and CfgEdgeType are derived from the source block's terminator.
        // The bridge node identifies which entry of the terminator's
        // SuccessorsPerType / SuccessorsPerSignature map produced this cross-block edge.
        string labelText = string.Empty;
        CfgEdgeType edgeType;

        ICfgNode terminator = fromBlock.Terminator;
        switch (terminator) {
            case SelectorNode selectorNode: {
                    Signature? signature = selectorNode.SuccessorsPerSignature
                        .FirstOrDefault(x => x.Value.Id == bridgeSuccessor.Id).Key;
                    labelText = signature?.ToString() ?? string.Empty;
                    edgeType = CfgEdgeType.Selector;
                    break;
                }
            case CfgInstruction cfgInstruction: {
                    edgeType = DetermineEdgeType(cfgInstruction);
                    List<InstructionSuccessorType> keys = cfgInstruction
                        .SuccessorsPerType
                        .Where(kvp => kvp.Value.Contains(bridgeSuccessor))
                        .Select(kvp => kvp.Key)
                        .ToList();
                    labelText = string.Join(", ", keys);
                    if (keys.Contains(InstructionSuccessorType.CallToReturn) ||
                        keys.Contains(InstructionSuccessorType.CallToMisalignedReturn)) {
                        edgeType = CfgEdgeType.CallToReturn;
                    } else if (keys.Contains(InstructionSuccessorType.CpuFault)) {
                        edgeType = CfgEdgeType.CpuFault;
                    }
                    break;
                }
            default:
                edgeType = CfgEdgeType.Normal;
                break;
        }

        CfgGraphNode fromGraphNode = GetOrCreateBlockGraphNode(fromBlock, isFromExecuting, graphNodeCache);
        CfgGraphNode toGraphNode = GetOrCreateBlockGraphNode(toBlock, isToExecuting, graphNodeCache);

        CfgGraphEdgeLabel edgeLabel = new() { Text = labelText, EdgeType = edgeType };
        return new Edge(fromGraphNode, toGraphNode, edgeLabel);
    }

    private static CfgEdgeType DetermineEdgeType(ICfgNode node) {
        return node switch {
            CfgInstruction instr when instr.IsJump => CfgEdgeType.Jump,
            CfgInstruction instr when instr.IsCall => CfgEdgeType.Call,
            CfgInstruction instr when instr.IsReturn => CfgEdgeType.Return,
            SelectorNode => CfgEdgeType.Selector,
            _ => CfgEdgeType.Normal
        };
    }

    private CfgGraphNode GetOrCreateBlockGraphNode(CfgBlock block, bool isExecuting,
        Dictionary<int, CfgGraphNode> cache) {
        if (cache.TryGetValue(block.Id, out CfgGraphNode? existing)) {
            return existing;
        }

        CfgGraphNode graphNode = CreateGraphNode(block, isExecuting);
        cache[block.Id] = graphNode;
        return graphNode;
    }

    private CfgGraphNode CreateGraphNode(CfgBlock block, bool isExecuting) {
        List<FormattedTextToken> textOffsets = [];

        textOffsets.Add(new() { Text = block.Address.ToString(), Kind = FormatterTextKind.FunctionAddress });
        textOffsets.Add(new() { Text = " / ", Kind = FormatterTextKind.Punctuation });
        textOffsets.Add(new() { Text = block.Id.ToString(), Kind = FormatterTextKind.Number });
        textOffsets.Add(new() { Text = Environment.NewLine, Kind = FormatterTextKind.Text });

        // The currently executing instruction may be inside this block. When so, the
        // per-instruction line gets a 🔴 marker so the user can pinpoint which instruction
        // is about to execute. The block itself gets a red border (via IsExecuting) instead
        // of an in-title dot.
        ICfgNode? executingNode = _executionContextManager.ExecutingNode;
        int executingIndex = -1;
        if (executingNode is not null) {
            for (int i = 0; i < block.Instructions.Count; i++) {
                if (block.Instructions[i].Id == executingNode.Id) {
                    executingIndex = i;
                    break;
                }
            }
        }

        // Walk the block's instructions and render each through the AST renderer.
        // SelectorNode's DisplayAst delegates to the active variant (or its terminator's),
        // so we render it using the same renderer for consistency.
        IReadOnlyList<ICfgNode> instructions = block.Instructions;
        for (int i = 0; i < instructions.Count; i++) {
            ICfgNode instruction = instructions[i];
            IVisitableAstNode ast = instruction.DisplayAst;
            textOffsets.AddRange(ast.Accept(_textOffsetsRenderer));
            if (i == executingIndex) {
                textOffsets.Add(new() { Text = " \U0001f534", Kind = FormatterTextKind.Text });
            }
            if (i < instructions.Count - 1) {
                textOffsets.Add(new() { Text = Environment.NewLine, Kind = FormatterTextKind.Text });
            }
        }

        // In-discovery blocks render with a trailing "..." marker so the user can see
        // at a glance that the block's terminator is not final. The dashed outline that
        // complements this indicator is applied by the AXAML view via the
        // IsDiscoveryComplete flag set on the CfgGraphNode below.
        if (!block.IsDiscoveryComplete) {
            textOffsets.Add(new() { Text = Environment.NewLine, Kind = FormatterTextKind.Text });
            textOffsets.Add(new() { Text = "\u2026", Kind = FormatterTextKind.Text });
        }

        return new CfgGraphNode {
            NodeId = block.Id,
            TextOffsets = textOffsets,
            IsExecuting = isExecuting,
            IsLive = block.IsLive,
            IsDiscoveryComplete = block.IsDiscoveryComplete
        };
    }

    private NodeTableEntry CreateTableEntry(ICfgNode node) {
        bool isExecuting = node.Id == _executionContextManager.ExecutingNode?.Id;

        AvaloniaList<NodeTableEntry> predecessors = new(node.Predecessors.Select(predecessor => new NodeTableEntry {
            Address = $"0x{predecessor.Address}",
            Assembly = _nodeToString.ToAssemblyString(predecessor),
            Node = predecessor
        }));

        AvaloniaList<NodeTableEntry> successors = new(node.Successors.Select(successor => new NodeTableEntry {
            Address = $"0x{successor.Address}",
            Assembly = _nodeToString.ToAssemblyString(successor),
            Node = successor
        }));

        return new NodeTableEntry {
            Address = $"0x{node.Address}",
            Assembly = _nodeToString.ToAssemblyString(node),
            Type = node.GetType().Name,
            Predecessors = predecessors,
            Successors = successors,
            IsExecuting = isExecuting,
            Node = node
        };
    }

    /// <summary>
    /// Builds the synthetic "CfgBlock" header row inserted at the top of each block's listing
    /// in the table view. Predecessors / successors are the addresses of the neighbouring
    /// CfgBlocks (derived from the underlying instruction-level edges via the
    /// <c>ContainingBlock</c> back-pointers). Selecting this row navigates to the block.
    /// </summary>
    private NodeTableEntry CreateBlockHeaderTableEntry(CfgBlock block, bool isExecutingBlock) {
        AvaloniaList<NodeTableEntry> predecessors = new();
        HashSet<int> seenPredecessorBlockIds = new();
        foreach (ICfgNode predecessor in block.Predecessors) {
            CfgBlock? predecessorBlock = predecessor.ContainingBlock;
            if (predecessorBlock is null || !seenPredecessorBlockIds.Add(predecessorBlock.Id)) {
                continue;
            }
            predecessors.Add(new NodeTableEntry {
                Address = $"0x{predecessorBlock.Address}",
                Assembly = _nodeToString.ToAssemblyString(predecessorBlock),
                Type = nameof(CfgBlock),
                Node = predecessorBlock
            });
        }

        AvaloniaList<NodeTableEntry> successors = new();
        HashSet<int> seenSuccessorBlockIds = new();
        foreach (ICfgNode successor in block.Successors) {
            CfgBlock? successorBlock = successor.ContainingBlock;
            if (successorBlock is null || !seenSuccessorBlockIds.Add(successorBlock.Id)) {
                continue;
            }
            successors.Add(new NodeTableEntry {
                Address = $"0x{successorBlock.Address}",
                Assembly = _nodeToString.ToAssemblyString(successorBlock),
                Type = nameof(CfgBlock),
                Node = successorBlock
            });
        }

        return new NodeTableEntry {
            Address = $"0x{block.Address}",
            Assembly = _nodeToString.ToAssemblyString(block),
            Type = nameof(CfgBlock),
            Predecessors = predecessors,
            Successors = successors,
            IsExecuting = isExecutingBlock,
            Node = block
        };
    }

    public record NodeTableEntry {
        public string Address { get; init; } = string.Empty;
        public string Assembly { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public AvaloniaList<NodeTableEntry> Predecessors { get; init; } = new();
        public AvaloniaList<NodeTableEntry> Successors { get; init; } = new();

        public bool IsExecuting { get; init; }
        public ICfgNode? Node { get; init; }

        public string PredecessorsText => string.Join(Environment.NewLine, Predecessors.Select(p => p.Address));
        public string SuccessorsText => string.Join(Environment.NewLine, Successors.Select(s => s.Address));
    }
}