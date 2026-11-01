namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.InterruptServices;
using Spice86.Shared.Emulator.Memory;

using System.Linq;
using System.Text;

/// <summary>
/// The <c>// DOS ...</c> comments and the string literals of the generated code.
/// <see cref="Record"/> runs right after
/// <see cref="IrConstants"/> in the pipeline and stores, per <c>int 21h</c> statement whose AH is a
/// known constant, the service and the known pointer registers; it edits nothing. After the pass
/// pipeline and the segment summaries, <see cref="Resolve"/> completes each record with the DS value
/// of <see cref="IrSegmentValues"/> at the surviving interrupt statement and reads the shown bytes
/// from the dump-time memory snapshot, keeping only bytes <see cref="ChangedMemorySnapshot.IsUnchanged"/>
/// proves never changed during discovery. A typed call to a string-reading service whose bytes are
/// all printable, unchanged and inside one segment gets the string as a literal argument instead of
/// a comment. <see cref="Apply"/> prints each resolved text as a <see cref="CommentNode"/> line
/// after the instruction's <c>// SEG:OFF asm</c> line, or sets
/// <see cref="InterruptServiceCallNode.StringLiteral"/>.
/// </summary>
internal static class DosCallComments {
    private const int MaxShownBytes = 64;

    private static readonly RegisterNode Ah = (RegisterNode)IrValueSets.NodeOf(CpuValues.Ah);
    private static readonly RegisterNode Dx = (RegisterNode)IrValueSets.NodeOf(CpuValues.Dx);
    private static readonly RegisterNode Cx = (RegisterNode)IrValueSets.NodeOf(CpuValues.Cx);
    private static readonly RegisterNode Bx = (RegisterNode)IrValueSets.NodeOf(CpuValues.Bx);

    /// <summary>Returns the <c>int 21h</c> sites of <paramref name="method"/>, keyed by instruction
    /// origin. Runs on the group-1 facts, before the passes that invalidate them.</summary>
    public static Dictionary<ICfgNode, DosCallRecord> Record(IrMethod method, IrConstantFacts facts) {
        Dictionary<ICfgNode, DosCallRecord> dosCalls = new();
        foreach (IrBlock block in method.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                IrStatement statement = block.Statements[index];
                if (FindInterruptCall(statement.Node) is not { } call
                    || !facts.TryEvaluate(UnwrapCast(call.Arguments[^1]), block, index, out ulong vector)
                    || vector != 0x21
                    || !facts.TryGetBefore(block, index, Ah, out ulong service)) {
                    continue;
                }
                ServiceContract? contract =
                    call.Service is { VectorNumber: 0x21, Services: HandlerServices.SelectedServices selected }
                        ? selected.Contracts.GetValueOrDefault((byte)service)
                        : null;
                if ((byte)service is not 0x09 and not 0x40
                    && (contract?.StringArgument ?? ServiceStringArgument.None).Terminator() is null) {
                    continue;
                }
                dosCalls[statement.Origin] = new DosCallRecord((byte)service, contract,
                    Known(Dx), Known(Cx), Known(Bx));

                ushort? Known(RegisterNode register) =>
                    facts.TryGetBefore(block, index, register, out ulong value) ? (ushort)value : null;
            }
        }
        return dosCalls;
    }

    /// <summary>Resolves the records of <paramref name="prepared"/> into comment texts or string
    /// literals, keyed by instruction address. A record drops out when its interrupt statement did
    /// not survive the passes, when DS has no proven value at it, when a needed register is unknown,
    /// or when a shown byte is outside the snapshot or not unchanged. A typed service call that
    /// reads a string at <c>DS:DX</c> gets the string as a literal argument (and no comment) when
    /// its terminator is found, every byte up to and including it is unchanged, the string does not
    /// wrap the offset and every byte before the terminator is printable.</summary>
    internal static Dictionary<SegmentedAddress, DosCallResolution> Resolve(PreparedMethod prepared,
        IrSegmentValues values, ChangedMemorySnapshot changed, byte[] memory) {
        Dictionary<SegmentedAddress, DosCallResolution> resolutions = new();
        foreach (IrBlock block in prepared.IrMethod.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                IrStatement statement = block.Statements[index];
                if (!prepared.DosCalls.TryGetValue(statement.Origin, out DosCallRecord? record)) {
                    continue;
                }
                if (!HasInterruptCall(statement.Node)
                    || record.Dx is not { } dx
                    || values.ValueBefore(block, index, IrSsaValue.Ds) is not SegmentValue.Known ds) {
                    continue;
                }
                uint physical = (uint)(ds.Value * 16 + dx);
                InterruptServiceCallNode? serviceCall = FindServiceCall(statement.Node);
                if (serviceCall is not null
                    && serviceCall.Contract.StringArgument.Terminator() is { } terminator
                    && StringLiteral(record, memory, changed, physical, dx, terminator)
                        is { } literal) {
                    resolutions[statement.Origin.Address] = new DosCallResolution(null, literal);
                    continue;
                }
                string? text = record.Service switch {
                    0x09 => PrintComment(memory, changed, physical),
                    0x40 => WriteComment(record, memory, changed, physical),
                    _ => AsciiZComment(record, memory, changed, physical)
                };
                if (text is not null) {
                    resolutions[statement.Origin.Address] = new DosCallResolution(text, null);
                }
            }
        }
        return resolutions;
    }

    /// <summary>Replaces <paramref name="body"/> with a copy that prints each resolved comment as a
    /// <see cref="CommentNode"/> right after the <c>// SEG:OFF asm</c> line of its instruction, and
    /// each resolved literal as the last argument of the typed service call of its
    /// instruction.</summary>
    internal static BlockNode Apply(BlockNode body,
        IReadOnlyDictionary<SegmentedAddress, DosCallResolution> resolutions) {
        IVisitableAstNode Rewrite(IVisitableAstNode node) {
            if (node is InstructionBoundaryNode boundary
                && resolutions.TryGetValue(boundary.Node.Address, out DosCallResolution? resolution)) {
                if (resolution.StringLiteral is { } literal) {
                    IVisitableAstNode Replace(IVisitableAstNode child) {
                        if (child is InterruptServiceCallNode call) {
                            return call with { StringLiteral = literal };
                        }
                        return IrAstChildren.MapChildren(child, Replace);
                    }
                    return boundary with { Body = (BlockNode)Replace(boundary.Body) };
                }
                if (resolution.Comment is { } text) {
                    List<IVisitableAstNode> statements = [.. boundary.Body.Statements];
                    int position = statements.FindIndex(s => s is InstructionStartNode) + 1;
                    statements.Insert(position, new CommentNode(text));
                    return boundary with { Body = new BlockNode([.. statements]) };
                }
            }
            return IrAstChildren.MapChildren(node, Rewrite);
        }
        return (BlockNode)Rewrite(body);
    }

    private static InterruptCallHelperNode? FindInterruptCall(IVisitableAstNode node) =>
        IrAstChildren.Descendants(node).OfType<InterruptCallHelperNode>().FirstOrDefault();

    private static InterruptServiceCallNode? FindServiceCall(IVisitableAstNode node) =>
        IrAstChildren.Descendants(node).OfType<InterruptServiceCallNode>().FirstOrDefault();

    /// <summary>The constant string at <paramref name="physical"/>, when its terminator sits at
    /// most <see cref="MaxShownBytes"/> bytes away, every byte up to and including it is unchanged,
    /// the string does not wrap the <paramref name="dx"/> offset and every byte before the
    /// terminator is printable ASCII, a tab, a line feed or a carriage return.</summary>
    private static string? StringLiteral(DosCallRecord record, byte[] memory,
        ChangedMemorySnapshot changed, uint physical, ushort dx, byte terminator) {
        int end = ScanFor(memory, physical, terminator);
        int length = end + 1;
        if (end < 0
            || physical + length > memory.LongLength
            || dx + length > 0x10000
            || !changed.IsUnchanged(physical, length)) {
            return null;
        }
        StringBuilder text = new(end);
        for (int i = 0; i < end; i++) {
            byte b = memory[physical + i];
            if (b is not ((>= 0x20 and <= 0x7E) or 0x09 or 0x0A or 0x0D)) {
                return null;
            }
            text.Append((char)b);
        }
        return text.ToString();
    }

    /// <summary>Whether the statement still holds the <c>int</c> the record was taken on: the raw
    /// <see cref="InterruptCallHelperNode"/>, or the typed call
    /// <see cref="IrInterruptServiceCalls"/> put in its place.</summary>
    private static bool HasInterruptCall(IVisitableAstNode node) => IrAstChildren.Descendants(node)
        .Any(descendant => descendant is InterruptCallHelperNode or InterruptServiceCallNode);

    private static IVisitableAstNode UnwrapCast(IVisitableAstNode node) =>
        node is ExplicitCastNode cast ? cast.Value : node;

    /// <summary><c>DOS 09h print "Hello$"</c>: a <c>$</c>-terminated string, terminator included.</summary>
    private static string? PrintComment(byte[] memory, ChangedMemorySnapshot changed, uint physical) {
        int terminator = ScanFor(memory, physical, (byte)'$');
        if (terminator < 0) {
            return null;
        }
        int length = terminator + 1;
        if (physical + length > memory.LongLength) {
            return null;
        }
        if (!changed.IsUnchanged(physical, length)) {
            return null;
        }
        return $"DOS 09h print \"{Escape(memory, physical, length)}\"";
    }

    /// <summary><c>DOS 3Dh Open "DATA.BIN"</c>: the contract name of a service reading a string
    /// at <c>DS:DX</c>; the terminator byte counts for the unchanged check but is not shown.</summary>
    private static string? AsciiZComment(DosCallRecord record, byte[] memory,
        ChangedMemorySnapshot changed, uint physical) {
        if (record.Contract is null
            || record.Contract.StringArgument.Terminator() is not { } terminator) {
            return null;
        }
        int end = ScanFor(memory, physical, terminator);
        if (end < 0 || physical + end + 1 > memory.LongLength) {
            return null;
        }
        if (!changed.IsUnchanged(physical, end + 1)) {
            return null;
        }
        return $"DOS {record.Service:X2}h {record.Contract.Name} \"{Escape(memory, physical, end)}\"";
    }

    /// <summary><c>DOS 40h write handle 1 "DATA"</c>: the CX bytes at <c>DS:DX</c>, the handle number
    /// when BX is known.</summary>
    private static string? WriteComment(DosCallRecord record, byte[] memory,
        ChangedMemorySnapshot changed, uint physical) {
        if (record.Cx is not { } count) {
            return null;
        }
        int shown = Math.Min((int)count, MaxShownBytes);
        if (physical + shown > memory.LongLength) {
            return null;
        }
        string handle = record.Bx is { } bx ? $" handle {bx}" : string.Empty;
        if (!changed.IsUnchanged(physical, shown)) {
            return null;
        }
        string ellipsis = count > MaxShownBytes ? "..." : string.Empty;
        return $"DOS 40h write{handle} \"{Escape(memory, physical, shown)}{ellipsis}\"";
    }

    /// <summary>The index of <paramref name="terminator"/> at most <see cref="MaxShownBytes"/> bytes
    /// from <paramref name="physical"/>, or -1 when it is absent there or memory ends first.</summary>
    private static int ScanFor(byte[] memory, uint physical, byte terminator) {
        int length = (int)Math.Min(MaxShownBytes, memory.LongLength - physical);
        if (length <= 0) {
            return -1;
        }
        for (int i = 0; i < length; i++) {
            if (memory[physical + i] == terminator) {
                return i;
            }
        }
        return -1;
    }

    private static string Escape(byte[] memory, uint physical, int length) {
        StringBuilder text = new(length);
        for (int i = 0; i < length; i++) {
            byte b = memory[physical + i];
            text.Append(b switch {
                (byte)'"' => "\\\"",
                (byte)'\\' => "\\\\",
                >= 0x20 and <= 0x7E => (char)b,
                _ => $"\\x{b:X2}"
            });
        }
        return text.ToString();
    }
}
