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
    /// <summary>Creates a block with no statements and no terminator (set by the builder, replaced by passes).</summary>
    public IrBlock(int id) {
        Id = id;
    }

    /// <summary>The block id, unique inside its method.</summary>
    public int Id { get; }

    /// <summary>The block statements, in order.</summary>
    public List<IrStatement> Statements { get; } = [];

    private IrTerminator? _terminator;

    /// <summary>Whether a terminator was set.</summary>
    public bool HasTerminator => _terminator is not null;

    /// <summary>The block terminator, set once by the builder and replaced by passes.</summary>
    /// <exception cref="InvalidOperationException">Read before a terminator was set.</exception>
    public IrTerminator Terminator {
        get => _terminator ?? throw new InvalidOperationException($"IR block {Id} has no terminator.");
        set => _terminator = value;
    }

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
            int startIndex = Statements.FindIndex(statement => statement.Node is InstructionStartNode);
            if (startIndex < 0) {
                return false;
            }
            ICfgNode origin = Statements[startIndex].Origin;
            for (int index = 0; index < startIndex; index++) {
                IrStatement statement = Statements[index];
                if (statement.Node is not CheckExternalEventsNode and not SpeculativeGuardNode
                    || !ReferenceEquals(statement.Origin, origin)) {
                    return false;
                }
            }
            return true;
        }
    }
}
