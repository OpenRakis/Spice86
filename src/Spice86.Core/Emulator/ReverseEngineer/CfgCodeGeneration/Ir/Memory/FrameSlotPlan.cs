namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// How one method's proved frame accesses print: the per-method frame class name, the
/// <c>frame</c> variable, the property name of every pointer node whose every occurrence is a
/// frame access at the same slot, and the property list the class declares. A node with one
/// occurrence that is not a frame access, or occurrences at different slots, keeps the indexer.
/// </summary>
internal sealed class FrameSlotPlan {
    /// <summary>One property of the frame class.</summary>
    /// <param name="Name">The property name (<c>Arg0</c>, <c>Local4</c>, <c>SavedBp</c>, ...).</param>
    /// <param name="EntryOffset">The slot offset relative to the method-entry SP.</param>
    /// <param name="Width">The access width in bytes (1, 2 or 4).</param>
    internal sealed record Property(string Name, int EntryOffset, int Width);

    private FrameSlotPlan(string className, string variableName,
        Dictionary<SegmentedPointerNode, string> names, List<Property> properties) {
        ClassName = className;
        VariableName = variableName;
        Names = names;
        Pointers = new HashSet<SegmentedPointerNode>(names.Keys, ReferenceEqualityComparer.Instance);
        Properties = properties;
    }

    /// <summary>The name of the nested frame class (<c>&lt;MethodName&gt;Frame</c>).</summary>
    internal string ClassName { get; }

    /// <summary>The name of the frame variable declared in the method (<c>frame</c>, or a variant
    /// when a method variable already holds the name).</summary>
    internal string VariableName { get; }

    /// <summary>Pointer node (reference-keyed: records are shared by reference through tail
    /// duplication) to the property name it prints as.</summary>
    internal IReadOnlyDictionary<SegmentedPointerNode, string> Names { get; }

    /// <summary>The pointer nodes <see cref="Names"/> covers, for membership checks that keep
    /// reference equality.</summary>
    internal IReadOnlySet<SegmentedPointerNode> Pointers { get; }

    /// <summary>The declared properties, sorted by entry offset then width.</summary>
    internal IReadOnlyList<Property> Properties { get; }

    /// <summary>The plan of <paramref name="method"/>, or null when no pointer node qualifies:
    /// a node prints as a property only when every one of its occurrences in the method's
    /// statements is a frame access of <paramref name="frames"/> at the same
    /// (entry offset, width), and its data type is an unsigned 8 / 16 / 32 bits.</summary>
    internal static FrameSlotPlan? Build(IrMethod method, IrFrames frames, string className) {
        Dictionary<SegmentedPointerNode, List<IrFrameAccess?>> byNode =
            new(ReferenceEqualityComparer.Instance);
        foreach ((SegmentedPointerNode pointer, IrFrameAccess? access) in frames.PointerResults) {
            if (!byNode.TryGetValue(pointer, out List<IrFrameAccess?>? list)) {
                list = [];
                byNode[pointer] = list;
            }
            list.Add(access);
        }
        int? returnBytes = frames.ReturnAddressBytes;
        bool standardFrame = method.Entries.Any(entry => frames.FrameAt(entry) is not null);
        Dictionary<SegmentedPointerNode, string> names = new(ReferenceEqualityComparer.Instance);
        SortedDictionary<(int EntryOffset, int Width), Property> properties = new();
        foreach ((SegmentedPointerNode pointer, List<IrFrameAccess?> results) in byNode) {
            List<IrFrameAccess> accesses = [.. results.OfType<IrFrameAccess>()];
            if (pointer.DataType != DataType.UINT8 && pointer.DataType != DataType.UINT16
                && pointer.DataType != DataType.UINT32
                || accesses.Count != results.Count
                || accesses.Select(access => (access.EntryOffset, access.Width))
                      .Distinct().Count() != 1) {
                continue;
            }
            IrFrameAccess first = accesses[0];
            string name = NameOf(first.EntryOffset, first.Width, returnBytes, standardFrame);
            names[pointer] = name;
            properties[(first.EntryOffset, first.Width)] =
                new Property(name, first.EntryOffset, first.Width);
        }
        if (names.Count == 0) {
            return null;
        }
        return new FrameSlotPlan(className, FrameVariableName(method), names,
            [.. properties.Values]);
    }

    /// <summary>The property name of the slot at <paramref name="entryOffset"/>: an argument past
    /// the return address (<c>Arg&lt;entry offset - r&gt;</c>), inside the return frame
    /// <c>ReturnIp</c> / <c>ReturnCs</c> / <c>SavedFlags</c> by the word they hold
    /// (<c>ReturnFrame&lt;entry offset&gt;</c> for any other offset), the saved BP of a standard
    /// frame (<c>SavedBp</c>), a local below it (<c>Local&lt;-entry offset&gt;</c>), or
    /// <c>Stack&lt;entry offset&gt;</c> when the return address size is unknown. A byte / dword
    /// access suffixes <c>Byte</c> / <c>Dword</c>.</summary>
    private static string NameOf(int entryOffset, int width, int? returnBytes,
        bool standardFrame) {
        string slot;
        if (entryOffset < 0) {
            slot = $"Local{-entryOffset}";
            if (entryOffset == -2 && width == 2 && standardFrame) {
                slot = "SavedBp";
            }
        } else if (returnBytes is not { } bytes) {
            slot = $"Stack{entryOffset}";
        } else if (entryOffset < bytes) {
            slot = entryOffset switch {
                0 => "ReturnIp",
                2 => "ReturnCs",
                4 when bytes == 6 => "SavedFlags",
                _ => $"ReturnFrame{entryOffset}"
            };
        } else {
            slot = $"Arg{entryOffset - bytes}";
        }
        return slot + (width switch {
            1 => "Byte",
            4 => "Dword",
            _ => ""
        });
    }

    private static string FrameVariableName(IrMethod method) =>
        method.MethodVariables.Any(variable => variable.VariableName == "frame") ? "frame_" : "frame";
}
