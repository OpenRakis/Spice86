namespace Spice86.ViewModels.Services;

using Spice86.Shared.Utils;

using System.Collections.Generic;

/// <summary>
/// Computes jump arc segments for disassembly lines, assigning lanes to avoid overlapping lines.
/// </summary>
internal static class JumpLineCalculator {
    /// <summary>
    /// Computes jump arc segments for all lines and assigns them to each <see cref="DebuggerLineViewModel"/>.
    /// </summary>
    /// <param name="sortedLines">The lines sorted by address.</param>
    public static void ComputeJumpArcs(IReadOnlyList<DebuggerLineViewModel> sortedLines) {
        // Build an index from physical address to line index for fast lookup
        Dictionary<uint, int> addressToIndex = new(sortedLines.Count);
        for (int i = 0; i < sortedLines.Count; i++) {
            addressToIndex[sortedLines[i].Address] = i;
        }

        // Collect all arcs where both source and target are in the visible lines
        List<(int SourceIndex, int TargetIndex, int TopIndex, int BottomIndex)> arcs = [];
        for (int i = 0; i < sortedLines.Count; i++) {
            DebuggerLineViewModel line = sortedLines[i];
            if (line.BranchTarget is not { } target) {
                continue;
            }

            // Only include jumps (conditional/unconditional branches), not calls
            if (!line.IsConditionalBranch && !line.IsUnconditionalBranch) {
                continue;
            }

            uint targetPhysical = MemoryUtils.ToPhysicalAddress(target.Segment, target.Offset);
            if (!addressToIndex.TryGetValue(targetPhysical, out int targetIndex)) {
                continue;
            }

            // Skip self-loops
            if (targetIndex == i) {
                continue;
            }

            int topIndex = Math.Min(i, targetIndex);
            int bottomIndex = Math.Max(i, targetIndex);
            arcs.Add((i, targetIndex, topIndex, bottomIndex));
        }

        // Sort arcs by span (smallest first) so inner arcs get inner lanes
        arcs.Sort((a, b) => (a.BottomIndex - a.TopIndex).CompareTo(b.BottomIndex - b.TopIndex));

        // Assign lanes greedily
        List<List<(int Top, int Bottom)>> lanes = [];
        int[] arcLanes = new int[arcs.Count];

        for (int i = 0; i < arcs.Count; i++) {
            (int _, int _, int topIndex, int bottomIndex) = arcs[i];
            int assignedLane = -1;

            for (int lane = 0; lane < lanes.Count; lane++) {
                bool conflict = false;
                foreach ((int existingTop, int existingBottom) in lanes[lane]) {
                    if (topIndex <= existingBottom && bottomIndex >= existingTop) {
                        conflict = true;
                        break;
                    }
                }
                if (!conflict) {
                    assignedLane = lane;
                    break;
                }
            }

            if (assignedLane == -1) {
                assignedLane = lanes.Count;
                lanes.Add([]);
            }

            lanes[assignedLane].Add((topIndex, bottomIndex));
            arcLanes[i] = assignedLane;
        }

        int maxLanes = lanes.Count;

        // Build per-line segment lists
        Dictionary<int, List<JumpArcSegment>> lineSegments = new();

        for (int arcIndex = 0; arcIndex < arcs.Count; arcIndex++) {
            (int sourceIndex, int targetIndex, int topIndex, int bottomIndex) = arcs[arcIndex];
            int lane = arcLanes[arcIndex];

            for (int lineIndex = topIndex; lineIndex <= bottomIndex; lineIndex++) {
                JumpSegmentType type;
                bool isTarget;

                if (lineIndex == topIndex) {
                    type = JumpSegmentType.TopEnd;
                    isTarget = targetIndex == topIndex;
                } else if (lineIndex == bottomIndex) {
                    type = JumpSegmentType.BottomEnd;
                    isTarget = targetIndex == bottomIndex;
                } else {
                    type = JumpSegmentType.Middle;
                    isTarget = false;
                }

                if (!lineSegments.TryGetValue(lineIndex, out List<JumpArcSegment>? segments)) {
                    segments = [];
                    lineSegments[lineIndex] = segments;
                }
                segments.Add(new JumpArcSegment(lane, type, isTarget));
            }
        }

        // Assign to lines
        for (int i = 0; i < sortedLines.Count; i++) {
            if (lineSegments.TryGetValue(i, out List<JumpArcSegment>? segments)) {
                sortedLines[i].JumpArcSegments = segments;
            } else {
                sortedLines[i].JumpArcSegments = [];
            }
            sortedLines[i].MaxJumpLanes = maxLanes;
        }
    }
}
