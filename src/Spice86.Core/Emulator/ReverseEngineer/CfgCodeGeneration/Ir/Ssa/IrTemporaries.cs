namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>
/// The declaring statement and the reading statements / terminators of every variable name of an
/// <see cref="IrMethod"/>, built in one walk. Positions are (block, index) pairs; an index equal to the
/// block's statement count means the terminator. Temporaries are single-assignment by construction, so
/// they are not versioned.
/// </summary>
internal sealed class IrTemporaries {
    private readonly Dictionary<string, (IrBlock Block, int Index)> _declarations = [];
    private readonly Dictionary<string, List<(IrBlock Block, int Index)>> _reads = [];

    private IrTemporaries() {
    }

    /// <summary>Builds the temporaries table of <paramref name="method"/>.</summary>
    public static IrTemporaries Build(IrMethod method) {
        IrTemporaries temporaries = new();
        foreach (IrBlock block in method.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                temporaries.Visit(block.Statements[index].Node, block, index);
            }
            temporaries.VisitTerminator(block.Terminator, block);
        }
        return temporaries;
    }

    /// <summary>The position declaring <paramref name="name"/>, or null when no statement declares it.</summary>
    public (IrBlock Block, int Index)? DeclarationOf(string name) =>
        _declarations.TryGetValue(name, out (IrBlock Block, int Index) declaration) ? declaration : null;

    /// <summary>The positions reading <paramref name="name"/>, in walk order.</summary>
    public IReadOnlyList<(IrBlock Block, int Index)> ReadsOf(string name) =>
        _reads.TryGetValue(name, out List<(IrBlock Block, int Index)>? reads) ? reads : [];

    private void Visit(IVisitableAstNode node, IrBlock block, int index) {
        if (node is VariableDeclarationNode declaration) {
            _declarations[declaration.VariableName] = (block, index);
        } else if (node is VariableReferenceNode reference
                   && node is not MethodScopedVariableReferenceNode) {
            AddRead(reference.VariableName, block, index);
        }
        foreach (IVisitableAstNode child in IrAstChildren.ChildrenOf(node)) {
            Visit(child, block, index);
        }
    }

    private void VisitTerminator(IrTerminator terminator, IrBlock block) {
        IVisitableAstNode? root = terminator switch {
            IrBranch branch => branch.Condition,
            IrSwitch switchNode => switchNode.Value,
            IrExit exit => exit.Transfer,
            _ => null
        };
        if (root is not null) {
            Visit(root, block, block.Statements.Count);
        }
    }

    private void AddRead(string name, IrBlock block, int index) {
        if (!_reads.TryGetValue(name, out List<(IrBlock Block, int Index)>? reads)) {
            reads = [];
            _reads[name] = reads;
        }
        reads.Add((block, index));
    }
}
