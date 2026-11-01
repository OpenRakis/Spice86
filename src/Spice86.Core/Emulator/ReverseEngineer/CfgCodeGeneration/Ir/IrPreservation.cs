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
/// previous bytes the statement does not write. A cycle preserves nothing.
/// </summary>
internal sealed class IrPreservation {
    private readonly IrSsa _ssa;
    private readonly IrStackSlots _slots;
    private readonly Func<IrStatement, CpuValues?> _transferChanges;
    private readonly Dictionary<IrSsaPhi, IrBlock> _phiBlocks = new();
    private readonly Dictionary<int, IrSsaPhi> _phiByResult = new();
    private readonly Dictionary<int, CpuValues> _memo = new();
    private readonly HashSet<int> _visiting = new();

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
        if (_memo.TryGetValue(version, out CpuValues memoized)) {
            return memoized;
        }
        IrSsaVersionInfo info = _ssa.Info(version);
        if (info.Value != value) {
            return CpuValues.None;
        }
        if (info.Kind == IrSsaDefinitionKind.MethodEntry) {
            return IrSsaValues.MaskOf(value);
        }
        if (!_visiting.Add(version)) {
            return CpuValues.None;
        }
        CpuValues result = PreservedUncached(value, version, info);
        _visiting.Remove(version);
        _memo[version] = result;
        return result;
    }

    private CpuValues PreservedUncached(IrSsaValue value, int version, IrSsaVersionInfo info) {
        CpuValues mask = IrSsaValues.MaskOf(value);
        if (info.Kind == IrSsaDefinitionKind.Phi) {
            IrSsaPhi phi = _phiByResult[version];
            CpuValues result = mask;
            foreach ((IrBlock _, int operand) in phi.Operands) {
                result &= Preserved(value, operand);
            }
            if (phi.HasEntryOperand && _phiBlocks.TryGetValue(phi, out IrBlock? phiBlock)) {
                result &= Preserved(value, _ssa.VersionBefore(phiBlock, 0, value));
            }
            return result;
        }
        if (_ssa.DefiningStatement(version) is not { } definition) {
            if (info.Kind == IrSsaDefinitionKind.Definition || info.Previous < 0) {
                return CpuValues.None;
            }
            return Preserved(value, info.Previous);
        }
        CpuValues previous = info.Previous >= 0 ? Preserved(value, info.Previous) : CpuValues.None;
        CpuValues written = (definition.Effects.MustWrite | definition.Effects.MayWrite) & mask;
        if (IsPopRestore(definition) && PopRestoresPushedBytes(definition, value, written,
                out CpuValues restored)) {
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
        out CpuValues restored) {
        restored = CpuValues.None;
        IReadOnlyList<IrStackSlotRead> reads = _slots.ReadsOf(pop);
        if (reads.Count == 0) {
            return false;
        }
        foreach (IrStackSlotRead read in reads) {
            if (!read.Complete) {
                return false;
            }
            foreach (IrStackSlotDefinition definition in read.Definitions) {
                if (!IsPushOf(definition.Statement, value, out CpuValues pushed)) {
                    return false;
                }
                int pushedVersion = _ssa.VersionUsed(definition.Statement, value);
                if (pushedVersion < 0
                    || (Preserved(value, pushedVersion) & pushed & written) != (pushed & written)) {
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
