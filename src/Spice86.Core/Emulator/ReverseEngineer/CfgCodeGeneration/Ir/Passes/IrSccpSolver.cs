namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;
using System.Numerics;

/// <summary>
/// The sparse conditional constant propagation itself: a worklist over the dependency edges
/// between SSA versions (a definition's lattice depends on the previous version its unwritten
/// bytes carry and on the version of every register its writer's right side reads; a phi's meet
/// depends on every operand version and on the executable edges into its block). The round
/// structure of the scan it replaces is kept: a block's edges are evaluated once, the round
/// after the block becomes executable, before that round's fills and meets run. Only edges
/// reachable from an entry are executable, an <see cref="IrBranch"/> whose condition evaluates
/// to a constant marks only the taken arm's edge.
/// </summary>
internal sealed partial class IrSccpSolver(IrMethod method, IrSsa ssa, IrConstantFacts facts) {
    private const int ByteCount = IrConstantFacts.ByteCount;
    private const uint AllBytes = IrConstantFacts.AllBytes;

    private sealed record DefinitionSite(IrBlock Block, int Index, IrStatement Statement,
        IrSsaValue Value, int Version);

    private readonly HashSet<IrBlock> _executable = [];
    private readonly HashSet<IrEdge> _executableEdges = [];
    private readonly Dictionary<IrBlock, List<DefinitionSite>> _sitesOfBlock = [];
    private readonly Dictionary<int, List<DefinitionSite>> _sitesByDependency = [];
    private readonly Dictionary<int, List<(IrSsaPhi Phi, IrBlock Block)>> _phisByOperand = [];
    private readonly Dictionary<int, List<IrBlock>> _branchesByDependency = [];
    private readonly Dictionary<int, DefinitionSite> _siteByVersion = [];
    private readonly Dictionary<int, (IrSsaPhi Phi, List<(int Version, List<IrEdge> Edges)> Operands)>
        _phiOps = [];
    private readonly HashSet<int> _dirtySites = [];
    private readonly HashSet<int> _dirtyPhis = [];
    private HashSet<IrBlock> _pendingEdges = [];
    private bool _changed;

    /// <summary>Fills the per-byte lattice of every tracked version of <paramref name="method"/>'s
    /// reachable statements.</summary>
    public void Compute() {
        for (int id = 0; id < ssa.VersionCount; id++) {
            facts.Undetermined[id] = AllBytes;
        }
        BuildIndex();
        Seed();
        RunRounds();
    }

    private static bool Tracked(IrSsaValue value) =>
        (value <= IrSsaValue.Edi && value != IrSsaValue.Esp) || value == IrSsaValue.Direction;

    private void BuildIndex() {
        foreach (IrBlock block in method.Blocks) {
            if (block.Terminator is IrBranch branch) {
                foreach (int dependency in Dependencies(block, block.Statements.Count,
                        branch.Condition)) {
                    AddDependent(_branchesByDependency, dependency, block);
                }
            }
            List<DefinitionSite> sites = [];
            for (int index = 0; index < block.Statements.Count; index++) {
                IrStatement statement = block.Statements[index];
                foreach ((IrSsaValue value, int version) in ssa.Definitions(statement)) {
                    if (!Tracked(value)) {
                        continue;
                    }
                    DefinitionSite site = new(block, index, statement, value, version);
                    sites.Add(site);
                    _siteByVersion[version] = site;
                    foreach (int dependency in Dependencies(block, index, statement, value, version)) {
                        AddDependent(_sitesByDependency, dependency, site);
                    }
                }
            }
            if (sites.Count > 0) {
                _sitesOfBlock[block] = sites;
            }
            foreach (IrSsaPhi phi in ssa.PhisOf(block)) {
                if (!Tracked(phi.Value)) {
                    continue;
                }
                List<(int Version, List<IrEdge> Edges)> operands = [.. phi.Operands.Select(
                    operand => (operand.Version,
                        Edges: operand.Predecessor.Terminator.Edges
                            .Where(edge => edge.Target == block).ToList()))];
                _phiOps[phi.Result] = (phi, operands);
                foreach ((IrBlock _, int version) in phi.Operands) {
                    AddDependent(_phisByOperand, version, (phi, block));
                }
            }
        }
    }

    private static void AddDependent<T>(Dictionary<int, List<T>> byDependency, int dependency,
        T dependent) {
        if (!byDependency.TryGetValue(dependency, out List<T>? dependents)) {
            byDependency[dependency] = dependents = [];
        }
        dependents.Add(dependent);
    }

    // A site, phi or branch is dirty when the lattice of a version it reads changed since its
    // last evaluation. Each round evaluates newly executable or dirtied branch edges first,
    // then the dirty definitions, then the dirty phi meets, in version order.
    private void TouchDependents(int version) {
        if (_sitesByDependency.TryGetValue(version, out List<DefinitionSite>? dependents)) {
            foreach (DefinitionSite dependent in dependents) {
                _dirtySites.Add(dependent.Version);
            }
        }
        if (_phisByOperand.TryGetValue(version,
                out List<(IrSsaPhi Phi, IrBlock Block)>? phis)) {
            foreach ((IrSsaPhi phi, IrBlock _) in phis) {
                _dirtyPhis.Add(phi.Result);
            }
        }
        if (_branchesByDependency.TryGetValue(version, out List<IrBlock>? branches)) {
            foreach (IrBlock dependent in branches) {
                _pendingEdges.Add(dependent);
            }
        }
    }

    private void FillSite(DefinitionSite site) {
        (uint known, uint undetermined, uint packed) = FillDefinition(
            site.Block, site.Index, site.Statement, site.Value, site.Version);
        if (known == facts.Known[site.Version] && undetermined == facts.Undetermined[site.Version]
            && packed == facts.Value[site.Version]) {
            return;
        }
        facts.Known[site.Version] = known;
        facts.Undetermined[site.Version] = undetermined;
        facts.Value[site.Version] = packed;
        _changed = true;
        TouchDependents(site.Version);
    }

    private void MeetPhi(IrSsaPhi phi,
        List<(int Version, List<IrEdge> Edges)> operands) {
        List<int> operandIds = [.. operands
            .Where(operand => operand.Edges.Any(_executableEdges.Contains))
            .Select(operand => operand.Version)];
        (uint known, uint undetermined, uint packed) = Meet(operandIds, phi.HasEntryOperand);
        if (known == facts.Known[phi.Result] && undetermined == facts.Undetermined[phi.Result]
            && packed == facts.Value[phi.Result]) {
            return;
        }
        facts.Known[phi.Result] = known;
        facts.Undetermined[phi.Result] = undetermined;
        facts.Value[phi.Result] = packed;
        _changed = true;
        TouchDependents(phi.Result);
    }

    private void MarkExecutable(IrBlock block) {
        if (!_executable.Add(block)) {
            return;
        }
        _pendingEdges.Add(block);
        if (_sitesOfBlock.TryGetValue(block, out List<DefinitionSite>? sites)) {
            foreach (DefinitionSite site in sites) {
                _dirtySites.Add(site.Version);
            }
        }
    }

    private void Seed() {
        foreach (IrBlock block in method.Blocks) {
            foreach (IrSsaPhi phi in ssa.PhisOf(block).Where(phi => Tracked(phi.Value))) {
                _dirtyPhis.Add(phi.Result);
            }
        }
        foreach (IrBlock entry in method.Entries.Select(entry => entry.Block)) {
            MarkExecutable(entry);
            foreach (int version in EntryVersions(entry)) {
                facts.Undetermined[version] = 0;
                TouchDependents(version);
            }
        }
        _changed = true;
    }

    private void RunRounds() {
        // A lattice cell can only move down: undetermined, then each of up to 64 bits loses its
        // known state once.
        int roundsLeft = 65 * ssa.VersionCount + 2;
        while (_changed) {
            if (roundsLeft-- <= 0) {
                throw new InvalidOperationException(
                    $"{nameof(IrSccpSolver)}.{nameof(RunRounds)} did not converge on {method.Name}.");
            }
            _changed = false;
            EvaluateEdges();
            if (_dirtySites.Count > 0) {
                List<int> siteRound = [.. _dirtySites];
                _dirtySites.Clear();
                siteRound.Sort();
                foreach (int version in siteRound) {
                    FillSite(_siteByVersion[version]);
                }
            }
            if (_dirtyPhis.Count > 0) {
                List<int> phiRound = [.. _dirtyPhis];
                _dirtyPhis.Clear();
                phiRound.Sort();
                foreach (int result in phiRound) {
                    (IrSsaPhi phi, List<(int Version, List<IrEdge> Edges)> operands) =
                        _phiOps[result];
                    MeetPhi(phi, operands);
                }
            }
        }
    }

    private void EvaluateEdges() {
        HashSet<IrBlock> edgeRound = _pendingEdges;
        _pendingEdges = [];
        foreach (IrBlock block in edgeRound) {
            if (!_executable.Contains(block)) {
                continue;
            }
            IEnumerable<IrEdge> edges = block.Terminator switch {
                IrBranch branch when facts.TryEvaluate(branch.Condition, block,
                        block.Statements.Count, out ulong condition) =>
                    [condition != 0 ? branch.WhenTrue : branch.WhenFalse],
                _ => block.Terminator.Edges
            };
            foreach (IrEdge edge in edges) {
                if (!_executableEdges.Add(edge)) {
                    continue;
                }
                _changed = true;
                MarkExecutable(edge.Target);
                foreach (IrSsaPhi phi in ssa.PhisOf(edge.Target).Where(phi => Tracked(phi.Value))) {
                    _dirtyPhis.Add(phi.Result);
                }
            }
        }
    }

    /// <summary>The versions a definition's lattice can depend on: the previous version its unwritten
    /// bytes carry, and the version of every register its writer's right side reads.</summary>
    private IEnumerable<int> Dependencies(IrBlock block, int index, IrStatement statement,
        IrSsaValue value, int version) {
        HashSet<int> dependencies = [];
        int previous = ssa.Info(version).Previous;
        if (previous >= 0) {
            dependencies.Add(previous);
        }
        uint writtenMask = IrConstantFacts.WrittenBytes(statement.Effects, value);
        if (value == IrSsaValue.Direction) {
            if (DirectionWriter(statement.Node) is { } directionWriter) {
                dependencies.UnionWith(Dependencies(block, index, directionWriter));
            }
            return dependencies;
        }
        BinaryOperationNode? writer = writtenMask == 0 ? null
            : WriterOf(statement.Node, (int)value, writtenMask);
        if (writer is null) {
            return dependencies;
        }
        dependencies.UnionWith(Dependencies(block, index, writer.Right));
        return dependencies;
    }

    /// <summary>The version of every register <paramref name="node"/> reads at
    /// <paramref name="index"/> in <paramref name="block"/>.</summary>
    private IEnumerable<int> Dependencies(IrBlock block, int index, IVisitableAstNode node) {
        HashSet<int> dependencies = [];
        foreach (IVisitableAstNode descendant in IrAstChildren.Descendants(node)) {
            if (descendant is CpuFlagNode { FlagMask: Flags.Direction }) {
                dependencies.Add(ssa.VersionBefore(block, index, IrSsaValue.Direction));
                continue;
            }
            if (descendant is not RegisterNode register || register is SegmentRegisterNode) {
                continue;
            }
            int registerIndex =
                BitOperations.TrailingZeroCount((ulong)IrValueSets.Of(register)) / ByteCount;
            if (registerIndex <= (int)IrSsaValue.Edi && registerIndex != (int)IrSsaValue.Esp) {
                dependencies.Add(ssa.VersionBefore(block, index, (IrSsaValue)registerIndex));
            }
        }
        return dependencies;
    }

    /// <summary>The MethodEntry versions of an entry block.</summary>
    private IEnumerable<int> EntryVersions(IrBlock block) {
        for (int v = 0; v <= (int)IrSsaValue.Edi; v++) {
            yield return ssa.VersionBefore(block, 0, (IrSsaValue)v);
        }
        yield return ssa.VersionBefore(block, 0, IrSsaValue.Direction);
    }

    /// <summary>The lattice entry of one definition: the written bytes are known when the statement's
    /// single register assignment evaluates to a constant, the unwritten bytes of a partial definition
    /// keep the previous version's lattice entry.</summary>
    private (uint Known, uint Undetermined, uint Value) FillDefinition(IrBlock block, int index,
        IrStatement statement, IrSsaValue value, int version) {
        uint writtenMask = IrConstantFacts.WrittenBytes(statement.Effects, value);
        uint known = 0;
        uint undetermined = 0;
        uint packed = 0;
        int previous = ssa.Info(version).Previous;
        if (previous >= 0) {
            uint unwritten = AllBytes & ~writtenMask;
            known = facts.Known[previous] & unwritten;
            undetermined = facts.Undetermined[previous] & unwritten;
            packed = facts.Value[previous] & IrConstantFacts.Expand(unwritten);
        }
        if (writtenMask == 0) {
            return (known, undetermined, packed);
        }
        if (value == IrSsaValue.Direction) {
            return TryEvaluateDirectionWriter(statement.Node, block, index,
                out ulong direction)
                ? (known | 1, undetermined, (packed & ~1u) | (uint)direction & 1u)
                : (known, undetermined, packed);
        }
        BinaryOperationNode? writer = WriterOf(statement.Node, (int)value, writtenMask);
        if (writer is null
            || !facts.TryEvaluate(writer.Right, block, index, out ulong constant)) {
            return (known, undetermined, packed);
        }
        int firstByte = BitOperations.TrailingZeroCount(writtenMask);
        known |= writtenMask;
        packed = (packed & ~IrConstantFacts.Expand(writtenMask))
            | ((uint)(constant << (8 * firstByte)) & IrConstantFacts.Expand(writtenMask));
        return (known, undetermined, packed);
    }

}
