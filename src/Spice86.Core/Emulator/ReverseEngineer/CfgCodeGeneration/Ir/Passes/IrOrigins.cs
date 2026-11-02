namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

/// <summary>Per-origin views of a lifted method for the liveness-driven passes.</summary>
internal static class IrOrigins {
    /// <summary>One statement and where it sits.</summary>
    public readonly record struct Located(IrBlock Block, int Index, IrStatement Statement);

    /// <summary>The statements of each origin in layout order, keyed by origin.</summary>
    public static Dictionary<ICfgNode, List<Located>> ByOrigin(IrMethod method) {
        Dictionary<ICfgNode, List<Located>> byOrigin = [];
        foreach (IrBlock block in method.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                IrStatement statement = block.Statements[index];
                if (!byOrigin.TryGetValue(statement.Origin, out List<Located>? list)) {
                    list = byOrigin[statement.Origin] = [];
                }
                list.Add(new Located(block, index, statement));
            }
        }
        return byOrigin;
    }

    /// <summary>
    /// Whether the origin's statements can be rewritten: none can fault.
    /// Boundary statements (event checks, speculative guards) carry no register reads the rewrites touch.
    /// </summary>
    public static bool Rewritable(IReadOnlyList<Located> statements, ICfgNode origin) {
        foreach (Located located in statements) {
            if (located.Statement.Effects.MayFault) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The statement right after the origin's <see cref="InstructionStartNode"/> in the same block - the
    /// first statement of the instruction body - or <c>null</c>.
    /// </summary>
    public static Located? FirstBodyStatement(IReadOnlyList<Located> statements, ICfgNode origin) {
        for (int i = 0; i < statements.Count; i++) {
            Located located = statements[i];
            if (located.Statement.Node is not InstructionStartNode) {
                continue;
            }
            int index = located.Index + 1;
            if (index >= located.Block.Statements.Count) {
                return null;
            }
            IrStatement next = located.Block.Statements[index];
            return ReferenceEquals(next.Origin, origin) ? new Located(located.Block, index, next) : null;
        }
        return null;
    }
}
