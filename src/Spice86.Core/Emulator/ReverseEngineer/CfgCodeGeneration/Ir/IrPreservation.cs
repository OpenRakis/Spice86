namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// The bytes of a value's SSA version that still hold the value it had at method entry. One rule
/// set shared by the hardware interrupt effect builder and the calling conventions: the entry
/// version preserves every byte; a phi preserves the operand intersection (the entry operand
/// included); a version without a defining statement preserves the previous version's bytes; a pop
/// matched to pushes of preserved bytes restores them; a full definition preserves nothing; a
/// partial definition by a statement whose transfer effect the caller knows (a call, an event
/// check) preserves the previous bytes the transfer does not change; anything else preserves the
/// previous bytes the statement does not write. The first query for a value computes the
/// preservation of all its versions at once, as a greatest fixed point: every version starts at
/// the full mask and the rules re-apply reading the current table until nothing changes, which
/// converges in a few passes because every rule only narrows when its inputs narrow. A loop back
/// edge is then just a dependency on a later table entry, so loops no longer take exponential
/// time.
/// </summary>
internal sealed class IrPreservation {
    private readonly IrSsa _ssa;
    private readonly IrStackSlots _slots;
    private readonly Func<IrStatement, CpuValues?> _transferChanges;
    private readonly Dictionary<IrSsaPhi, IrBlock> _phiBlocks = new();
    private readonly Dictionary<int, IrSsaPhi> _phiByResult = new();
    private readonly Dictionary<IrSsaValue, CpuValues?[]> _tables = new();

    /// <summary>Creates the shared preservation rule set for <paramref name="method"/>.</summary>
    /// <param name="transferChanges">For a statement whose effect is a transfer the caller knows
    /// (a call, an event check): the bytes it may change; null for any other statement.</param>
    public IrPreservation(IrMethod method, IrSsa ssa, IrStackSlots slots,
        Func<IrStatement, CpuValues?> transferChanges) {
        _ssa = ssa;
        _slots = slots;
        _transferChanges = transferChanges;
        foreach (IrBlock block in method.Blocks) {
            foreach (IrSsaPhi phi in ssa.PhisOf(block)) {
                _phiBlocks[phi] = block;
                _phiByResult[phi.Result] = phi;
            }
        }
    }

    /// <summary>The bytes of <paramref name="value"/> that <paramref name="version"/> still holds
    /// from method entry.</summary>
    public CpuValues Preserved(IrSsaValue value, int version) {
        if (version < 0) {
            return CpuValues.None;
        }
        if (!_tables.TryGetValue(value, out CpuValues?[]? table)) {
            table = ComputeTable(value);
            _tables[value] = table;
        }
        return version < table.Length ? table[version] ?? CpuValues.None : CpuValues.None;
    }

    /// <summary>All versions of <paramref name="value"/>, solved together as a greatest fixed
    /// point: the table is indexed by version and holds null where the version is not
    /// <paramref name="value"/>'s.</summary>
    private CpuValues?[] ComputeTable(IrSsaValue value) {
        CpuValues mask = IrSsaValues.MaskOf(value);
        CpuValues?[] table = new CpuValues?[_ssa.VersionCount];
        for (int version = 0; version < table.Length; version++) {
            if (_ssa.Info(version).Value == value) {
                table[version] = mask;
            }
        }
        bool changed;
        do {
            changed = false;
            for (int version = 0; version < table.Length; version++) {
                if (table[version] is not { } current) {
                    continue;
                }
                IrSsaVersionInfo info = _ssa.Info(version);
                if (info.Kind == IrSsaDefinitionKind.MethodEntry) {
                    continue;
                }
                CpuValues next = ApplyRules(value, version, info, table);
                if (next != current) {
                    table[version] = next;
                    changed = true;
                }
            }
        } while (changed);
        return table;
    }

    private CpuValues Lookup(CpuValues?[] table, int version) =>
        version >= 0 && version < table.Length ? table[version] ?? CpuValues.None : CpuValues.None;

    private CpuValues ApplyRules(IrSsaValue value, int version, IrSsaVersionInfo info,
        CpuValues?[] table) {
        CpuValues mask = IrSsaValues.MaskOf(value);
        if (info.Kind == IrSsaDefinitionKind.Phi) {
            IrSsaPhi phi = _phiByResult[version];
            CpuValues result = mask;
            foreach ((IrBlock _, int operand) in phi.Operands) {
                result &= Lookup(table, operand);
            }
            if (phi.HasEntryOperand && _phiBlocks.TryGetValue(phi, out IrBlock? phiBlock)) {
                result &= Lookup(table, _ssa.VersionBefore(phiBlock, 0, value));
            }
            return result;
        }
        if (_ssa.DefinitionSite(version) is IrTerminator terminator) {
            if (info.Kind == IrSsaDefinitionKind.Definition || info.Previous < 0) {
                return CpuValues.None;
            }
            CpuValues terminatorWritten =
                (terminator.Effects.MustWrite | terminator.Effects.MayWrite) & mask;
            return Lookup(table, info.Previous) & ~terminatorWritten;
        }
        if (_ssa.DefiningStatement(version) is not { } definition) {
            if (info.Kind == IrSsaDefinitionKind.Definition || info.Previous < 0) {
                return CpuValues.None;
            }
            return Lookup(table, info.Previous);
        }
        CpuValues previous = Lookup(table, info.Previous);
        CpuValues written = (definition.Effects.MustWrite | definition.Effects.MayWrite) & mask;
        if (IsPopRestore(definition) && PopRestoresPushedBytes(definition, value, written,
                table, out CpuValues restored)) {
            return previous | (written & restored);
        }
        if (info.Kind == IrSsaDefinitionKind.Definition) {
            return CpuValues.None;
        }
        if (_transferChanges(definition) is { } changes) {
            return previous & ~changes;
        }
        return previous & ~written;
    }

    /// <summary>Every stack-slot read of a pop-assign must resolve to pushes of the same register
    /// bytes, pushed at a version where those bytes were preserved. <paramref name="restored"/> is
    /// the union of the pushed bytes: only those bytes of the popped register are proven restored
    /// (a wider pop keeps the pushed bytes, not the unread upper ones).</summary>
    private bool PopRestoresPushedBytes(IrStatement pop, IrSsaValue value, CpuValues written,
        CpuValues?[] table, out CpuValues restored) {
        restored = CpuValues.None;
        IReadOnlyList<IrStackSlotRead> reads = _slots.ReadsOf(pop);
        if (reads.Count == 0) {
            return false;
        }
        foreach (IrStackSlotRead read in reads) {
            if (!read.Complete || !read.ProvedRam) {
                return false;
            }
            foreach (IrStackSlotDefinition definition in read.Definitions) {
                if (!IsPushOf(definition.Statement, value, out CpuValues pushed)) {
                    return false;
                }
                int pushedVersion = _ssa.VersionUsed(definition.Statement, value);
                if (pushedVersion < 0
                    || (Lookup(table, pushedVersion) & pushed & written) != (pushed & written)) {
                    return false;
                }
                restored |= pushed;
            }
        }
        return true;
    }

    /// <summary>Whether <paramref name="statement"/> is a push of bytes of
    /// <paramref name="value"/>'s register, and which of its bytes it pushes.</summary>
    internal static bool IsPushOf(IrStatement statement, IrSsaValue value, out CpuValues pushed) {
        pushed = CpuValues.None;
        if (statement.Node is not StackCallNode {
                Operation: StackOperation.Push, Arguments: [RegisterNode register] }) {
            return false;
        }
        CpuValues bits = IrValueSets.Of(register);
        if ((bits & ~IrSsaValues.MaskOf(value)) != CpuValues.None) {
            return false;
        }
        pushed = bits;
        return true;
    }

    internal static bool IsPopRestore(IrStatement statement) =>
        statement.Node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN }
        && IrAstChildren.Descendants(statement.Node).OfType<StackCallValueNode>()
            .Any(stack => stack.Operation == StackOperation.Pop);
}
