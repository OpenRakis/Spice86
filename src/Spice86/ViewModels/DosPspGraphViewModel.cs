namespace Spice86.ViewModels;

using AvaloniaGraphControl;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;
using Spice86.ViewModels.DataModels;
using Spice86.ViewModels.Enums;
using Spice86.ViewModels.PropertiesMappers;
using Spice86.ViewModels.Services;
using Spice86.ViewModels.ValueViewModels.Debugging;

using System;
using System.Linq;

/// <summary>
/// DOS PSP parent chain graph view.
/// </summary>
public sealed partial class DosPspGraphViewModel : TimerRefreshViewModelBase {
    private readonly DosMemoryManager _memoryManager;
    private readonly DosSwappableDataArea _sda;
    private readonly IByteReaderWriter _memory;

    /// <inheritdoc />
    public override string Header => "DOS PSP Graph";

    [ObservableProperty]
    private Graph? _graph;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Initializes a new <see cref="DosPspGraphViewModel"/>.
    /// </summary>
    public DosPspGraphViewModel(DosMemoryManager memoryManager, DosSwappableDataArea sda, IByteReaderWriter memory,
        IPauseHandler pauseHandler) : base(400, pauseHandler) {
        _memoryManager = memoryManager;
        _sda = sda;
        _memory = memory;
    }

    /// <inheritdoc />
    protected override void RefreshCore() {
        ushort currentPsp = _sda.CurrentProgramSegmentPrefix;
        Dictionary<int, DosGraphNode> nodes = new();
        List<(int Child, int Parent)> links = new();
        Dictionary<ushort, List<ushort>> mcbHeaderSegmentsByPsp = new();
        Dictionary<ushort, int> mcbBytesByPsp = new();
        Dictionary<ushort, string> mcbOwnerNameByPsp = new();
        List<(ushort OwnerPspSegment, ushort HeaderSegment, int SizeBytes, string OwnerName)> mcbNodeInfos = new();

        foreach (DosMemoryControlBlock block in _memoryManager.EnumerateBlocks()) {
            if (!block.IsValid || block.IsFree) {
                continue;
            }

            ushort pspSegment = block.PspSegment;
            if (!mcbHeaderSegmentsByPsp.TryGetValue(pspSegment, out List<ushort>? headerSegments)) {
                headerSegments = new List<ushort>();
                mcbHeaderSegmentsByPsp[pspSegment] = headerSegments;
                mcbBytesByPsp[pspSegment] = 0;
            }

            headerSegments.Add((ushort)(block.DataBlockSegment - 1));
            mcbBytesByPsp[pspSegment] = mcbBytesByPsp[pspSegment] + block.AllocationSizeInBytes;

            mcbNodeInfos.Add((pspSegment, (ushort)(block.DataBlockSegment - 1), block.AllocationSizeInBytes, block.Owner));

            if (!mcbOwnerNameByPsp.ContainsKey(pspSegment)) {
                string owner = block.Owner.Trim();
                if (!string.IsNullOrEmpty(owner)) {
                    mcbOwnerNameByPsp[pspSegment] = owner;
                }
            }
        }

        HashSet<ushort> visited = new();
        ushort segment = currentPsp;
        int guard = 0;
        while (segment != 0 && guard < 64 && visited.Add(segment)) {
            DosProgramSegmentPrefix psp = new(_memory, MemoryUtils.ToPhysicalAddress(segment, 0));
            DosPspInfo info = new();
            string ownerName = string.Empty;
            if (mcbOwnerNameByPsp.TryGetValue(segment, out string? ownerNameFromMcb)) {
                ownerName = ownerNameFromMcb;
            }
            psp.CopyToDosPspInfo(info, segment, currentPsp, ownerName);

            int nodeId = segment;
            nodes[nodeId] = CreateNodeFromInfo(nodeId, info, segment, mcbHeaderSegmentsByPsp, mcbBytesByPsp);

            ushort parent = psp.ParentProgramSegmentPrefix;
            if (parent != 0 && parent != segment) {
                int parentId = parent;
                links.Add((nodeId, parentId));
                if (!nodes.ContainsKey(parentId)) {
                    nodes[parentId] = CreatePlaceholderNode(parentId);
                }
            }

            if (parent == segment) {
                break;
            }

            segment = parent;
            guard++;
        }

        HashSet<int> pspNodeIds = new(nodes.Keys);
        foreach ((ushort ownerPspSegment, ushort headerSegment, int sizeBytes, string ownerName) in mcbNodeInfos) {
            int ownerNodeId = ownerPspSegment;
            if (!nodes.ContainsKey(ownerNodeId)) {
                continue;
            }

            int mcbNodeId = GetMcbNodeId(headerSegment);
            if (!nodes.ContainsKey(mcbNodeId)) {
                nodes[mcbNodeId] = CreateMcbNode(mcbNodeId, headerSegment, ownerPspSegment, sizeBytes, ownerName);
            }

            links.Add((mcbNodeId, ownerNodeId));
        }

        Graph graph = new();
        HashSet<int> nodesWithEdges = new();
        foreach ((int childId, int parentId) in links) {
            if (!nodes.TryGetValue(childId, out DosGraphNode? childNode)) {
                continue;
            }
            if (!nodes.TryGetValue(parentId, out DosGraphNode? parentNode)) {
                continue;
            }

            string edgeText = "parent";
            if (pspNodeIds.Contains(parentId) && !pspNodeIds.Contains(childId)) {
                edgeText = "owned by";
            }
            CfgGraphEdgeLabel edgeLabel = new() { Text = edgeText, EdgeType = CfgEdgeType.Normal };
            graph.Edges.Add(new Edge(childNode, parentNode, edgeLabel));
            nodesWithEdges.Add(childId);
            nodesWithEdges.Add(parentId);
        }

        foreach (KeyValuePair<int, DosGraphNode> entry in nodes) {
            if (!nodesWithEdges.Contains(entry.Key)) {
                CfgGraphEdgeLabel edgeLabel = new() { Text = string.Empty, EdgeType = CfgEdgeType.IsolatedNodeLoop };
                graph.Edges.Add(new Edge(entry.Value, entry.Value, edgeLabel, Edge.Symbol.None, Edge.Symbol.None));
            }
        }

        Graph = graph;
        int pspNodesWithOwnedMcbs = pspNodeIds.Count(nodeId => mcbHeaderSegmentsByPsp.ContainsKey((ushort)nodeId));
        StatusMessage = $"PSP nodes: {pspNodeIds.Count} / MCB nodes: {mcbNodeInfos.Count} / Links: {links.Count} / PSPs with owned MCBs: {pspNodesWithOwnedMcbs}";
    }

    private static DosGraphNode CreateNodeFromInfo(int nodeId, DosPspInfo info, ushort pspSegment,
        IReadOnlyDictionary<ushort, List<ushort>> mcbHeaderSegmentsByPsp,
        IReadOnlyDictionary<ushort, int> mcbBytesByPsp) {
        string title = "PSP " + info.Segment;
        string ownerName = info.OwnerName;
        string subtitle = BuildMcbSummary(pspSegment, mcbHeaderSegmentsByPsp, mcbBytesByPsp);
        if (!string.IsNullOrWhiteSpace(ownerName)) {
            subtitle = ownerName + Environment.NewLine + subtitle;
        }
        if (info.IsCurrent) {
            subtitle = subtitle + Environment.NewLine + "current";
        }

        return new DosGraphNode {
            NodeId = nodeId,
            Title = title,
            Subtitle = subtitle,
            Kind = DosGraphNodeKind.Psp,
        };
    }

    private static string BuildMcbSummary(ushort pspSegment, IReadOnlyDictionary<ushort, List<ushort>> mcbHeaderSegmentsByPsp,
        IReadOnlyDictionary<ushort, int> mcbBytesByPsp) {
        if (!mcbHeaderSegmentsByPsp.TryGetValue(pspSegment, out List<ushort>? headerSegments) || headerSegments.Count == 0) {
            return "Owned MCBs: none";
        }

        IReadOnlyList<ushort> orderedHeaderSegments = headerSegments
            .OrderBy(static segment => segment)
            .ToArray();
        int previewCount = Math.Min(4, orderedHeaderSegments.Count);
        string headersText = string.Join(",", orderedHeaderSegments
            .Take(previewCount)
            .Select(static segment => ConvertUtils.ToHex16(segment)));
        if (orderedHeaderSegments.Count > previewCount) {
            headersText = headersText + ",...";
        }

        if (!mcbBytesByPsp.TryGetValue(pspSegment, out int totalBytes)) {
            totalBytes = 0;
        }

        return $"Owned MCBs: {headerSegments.Count} ({totalBytes} B), headers: {headersText}";
    }

    private static DosGraphNode CreatePlaceholderNode(int nodeId) {
        string hex = ConvertUtils.ToHex16((ushort)nodeId);
        return new DosGraphNode {
            NodeId = nodeId,
            Title = "PSP " + hex,
            Subtitle = "(referenced parent)",
            Kind = DosGraphNodeKind.Psp,
        };
    }

    private static int GetMcbNodeId(ushort headerSegment) {
        return 0x10000 + headerSegment;
    }

    private static DosGraphNode CreateMcbNode(int nodeId, ushort headerSegment, ushort ownerPspSegment, int sizeBytes,
        string ownerName) {
        string subtitle = "Owner PSP: " + ConvertUtils.ToHex16(ownerPspSegment) + Environment.NewLine +
            "Size: " + sizeBytes + " B";
        string trimmedOwnerName = ownerName.Trim();
        if (!string.IsNullOrEmpty(trimmedOwnerName)) {
            subtitle = subtitle + Environment.NewLine + "Owner: " + trimmedOwnerName;
        }

        return new DosGraphNode {
            NodeId = nodeId,
            Title = "MCB " + ConvertUtils.ToHex16(headerSegment),
            Subtitle = subtitle,
            Kind = DosGraphNodeKind.Mcb,
        };
    }
}
