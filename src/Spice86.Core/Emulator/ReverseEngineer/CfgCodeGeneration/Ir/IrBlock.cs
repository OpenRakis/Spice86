namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using System.Linq;

/// <summary>
/// A flat block of IR statements ending with one terminator. One instruction with inner control flow
/// becomes several blocks. A block is its own goto target: it implements <see cref="IGotoTarget"/> with
/// reference equality.
/// </summary>
internal sealed class IrBlock : IGotoTarget {
    private readonly List<IrStatement> _statements = [];
    private IrTerminator? _terminator;

    /// <summary>Creates a block with no statements and no terminator (set by the editor, replaced by passes).</summary>
    public IrBlock(int id) {
        Id = id;
    }

    /// <summary>The block id, unique inside its method.</summary>
    public int Id { get; }

    /// <summary>The block statements, in order. Mutated by <see cref="IrEditor"/> only.</summary>
    public IReadOnlyList<IrStatement> Statements => _statements;

    /// <summary>Whether a terminator was set.</summary>
    public bool HasTerminator => _terminator is not null;

    /// <summary>The block terminator, set by <see cref="IrEditor"/> only.</summary>
    /// <exception cref="InvalidOperationException">Read before a terminator was set.</exception>
    public IrTerminator Terminator => _terminator
        ?? throw new InvalidOperationException($"IR block {Id} has no terminator.");

    /// <summary>The first statement's origin, else the terminator's.</summary>
    public ICfgNode EntryOrigin => Statements.Count > 0 ? Statements[0].Origin : Terminator.Origin;

    /// <summary>The CFG nodes the block holds: the origins of its statements and its terminator.</summary>
    public IEnumerable<ICfgNode> Origins =>
        Statements.Select(statement => statement.Origin).Append(Terminator.Origin).Distinct();

    /// <summary>
    /// Whether the block starts an instruction: it holds an <see cref="InstructionStartNode"/> statement,
    /// and every statement before the first one is a boundary node (<see cref="CheckExternalEventsNode"/>
    /// or <see cref="SpeculativeGuardNode"/>) with the same origin.
    /// </summary>
    public bool StartsInstruction {
        get {
            int startIndex = _statements.FindIndex(statement => statement.Node is InstructionStartNode);
            if (startIndex < 0) {
                return false;
            }
            ICfgNode origin = _statements[startIndex].Origin;
            for (int index = 0; index < startIndex; index++) {
                IrStatement statement = _statements[index];
                if (statement.Node is not CheckExternalEventsNode and not SpeculativeGuardNode
                    || !ReferenceEquals(statement.Origin, origin)) {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>The index of <paramref name="statement"/> in the block, or -1.</summary>
    internal int IndexOfStatement(IrStatement statement) => _statements.IndexOf(statement);

    internal int IndexOfStatement(Predicate<IrStatement> match) => _statements.FindIndex(match);

    /// <summary>Splices <paramref name="removeCount"/> statements out at <paramref name="index"/> and
    /// inserts <paramref name="inserts"/> in their place. Called by <see cref="IrEditor"/> only.</summary>
    internal void Splice(int index, int removeCount, IReadOnlyList<IrStatement> inserts) {
        _statements.RemoveRange(index, removeCount);
        _statements.InsertRange(index, inserts);
    }

    /// <summary>Replaces the whole statement list. Called by <see cref="IrEditor"/> only.</summary>
    internal void ReplaceAll(IReadOnlyList<IrStatement> statements) {
        _statements.Clear();
        _statements.AddRange(statements);
    }

    /// <summary>Sets the terminator. Called by <see cref="IrEditor"/> only.</summary>
    internal void SetTerminator(IrTerminator terminator) => _terminator = terminator;
}
