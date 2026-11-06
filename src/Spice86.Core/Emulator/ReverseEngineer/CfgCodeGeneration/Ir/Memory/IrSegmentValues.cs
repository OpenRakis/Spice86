namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// A data-flow analysis over the SSA of one method giving, for every register version a statement
/// reads, what the register holds: a <see cref="SegmentValue.Known"/> constant (a constant
/// assignment through a full 16-bit write, a phi whose operands all resolve to it, a pop whose
/// stack read is complete and whose definitions all push it, the entry version of CS when every
/// method entry has the same code segment, or a register the <see cref="IrSegmentFacts"/> mark as a
/// known entry or call result), a <see cref="SegmentValue.Symbolic"/> root (each entry version is
/// its own root, the members of an entry equality class share the root the facts name, and a copy
/// source no statement defines) or <see cref="SegmentValue.Unknown"/> (a phi merge of different
/// values, a non-complete stack read, and everything else - loads, arithmetic, byte writes, calls,
/// hardware effects). The stack reads come from the same <see cref="IrStackSlots"/> state the alias
/// analysis fills.
/// </summary>
internal sealed class IrSegmentValues {
    private readonly IrSsa _ssa;
    private readonly IrStackSlots _slots;
    private IrSegmentFacts _facts;
    private readonly IReadOnlySet<int> _callDefinedVersions;
    private readonly ushort? _codeSegment;
    private readonly HashSet<IrBlock> _entryBlocks;
    private readonly Dictionary<int, (SegmentValue Value, bool FactDependent)> _values = new();
    private bool _resolvingDependent;

    /// <summary>Builds the analysis over the SSA of <paramref name="method"/>; <paramref name="slots"/>
    /// must be computed on the same SSA and <paramref name="facts"/> is
    /// <see cref="IrSegmentFacts.None"/> when no cross-method summary exists.
    /// <paramref name="callDefinedVersions"/> lists the register versions a call defines: their
    /// resolution reads the facts and is invalidated by <see cref="SetFacts"/>.</summary>
    public IrSegmentValues(IrMethod method, IrSsa ssa, IrStackSlots slots, IrSegmentFacts facts,
        IReadOnlySet<int> callDefinedVersions) {
        _ssa = ssa;
        _slots = slots;
        _facts = facts;
        _callDefinedVersions = callDefinedVersions;
        _codeSegment = UniformEntrySegment(method);
        _entryBlocks = method.Entries.Select(entry => entry.Block).ToHashSet();
    }

    /// <summary>Replaces the cross-method facts and drops only the resolutions that could have read
    /// them: the entry versions of the entry blocks and the call-defined versions.</summary>
    internal void SetFacts(IrSegmentFacts facts) {
        _facts = facts;
        foreach (int version in _values.Where(pair => pair.Value.FactDependent)
                     .Select(pair => pair.Key).ToList()) {
            _values.Remove(version);
        }
    }

    /// <summary>What the register <paramref name="value"/> holds at <paramref name="statement"/>, for
    /// the version the statement reads.</summary>
    public SegmentValue ValueAt(IrStatement statement, IrSsaValue value) {
        int version = _ssa.VersionUsed(statement, value);
        return version < 0 ? SegmentValue.Unknown.Instance : Resolve(version);
    }

    /// <summary>Whether <paramref name="first"/> and <paramref name="second"/> provably hold the same
    /// value at <paramref name="statement"/>: both the same constant or both symbolic with the same
    /// root version.</summary>
    public bool SameValue(IrStatement statement, IrSsaValue first, IrSsaValue second) {
        return (ValueAt(statement, first), ValueAt(statement, second)) switch {
            (SegmentValue.Known a, SegmentValue.Known b) => a.Value == b.Value,
            (SegmentValue.Symbolic a, SegmentValue.Symbolic b) => a.RootVersion == b.RootVersion,
            _ => false
        };
    }

    /// <summary>What the register <paramref name="value"/> holds when the statement at
    /// <paramref name="statementIndex"/> of <paramref name="block"/> runs, whether or not the
    /// statement reads the register.</summary>
    internal SegmentValue ValueBefore(IrBlock block, int statementIndex, IrSsaValue value) {
        int version = _ssa.VersionBefore(block, statementIndex, value);
        return version < 0 ? SegmentValue.Unknown.Instance : Resolve(version);
    }

    /// <summary>Whether <paramref name="first"/> and <paramref name="second"/> provably hold the
    /// same value when the statement at <paramref name="statementIndex"/> of
    /// <paramref name="block"/> runs, for the versions live then.</summary>
    internal bool SameValueBefore(IrBlock block, int statementIndex, IrSsaValue first,
        IrSsaValue second) {
        return (ValueBefore(block, statementIndex, first),
                ValueBefore(block, statementIndex, second)) switch {
            (SegmentValue.Known a, SegmentValue.Known b) => a.Value == b.Value,
            (SegmentValue.Symbolic a, SegmentValue.Symbolic b) => a.RootVersion == b.RootVersion,
            _ => false
        };
    }

    /// <summary>Whether <paramref name="first"/> and <paramref name="second"/> provably hold the
    /// same value at <paramref name="terminator"/>, for the versions it reads.</summary>
    internal bool SameValue(IrTerminator terminator, IrSsaValue first, IrSsaValue second) {
        return (ValueAt(terminator, first), ValueAt(terminator, second)) switch {
            (SegmentValue.Known a, SegmentValue.Known b) => a.Value == b.Value,
            (SegmentValue.Symbolic a, SegmentValue.Symbolic b) => a.RootVersion == b.RootVersion,
            _ => false
        };
    }

    /// <summary>What the register <paramref name="value"/> holds at <paramref name="terminator"/>,
    /// for the version the terminator reads.</summary>
    internal SegmentValue ValueAt(IrTerminator terminator, IrSsaValue value) {
        int version = _ssa.VersionUsed(terminator, value);
        return version < 0 ? SegmentValue.Unknown.Instance : Resolve(version);
    }

    /// <summary>The resolved value of <paramref name="version"/>.</summary>
    internal SegmentValue ValueOf(int version) =>
        version < 0 ? SegmentValue.Unknown.Instance : Resolve(version);

    private static ushort? UniformEntrySegment(IrMethod method) {
        if (method.Entries.Count == 0) {
            return null;
        }
        ushort segment = method.Entries[0].Node.Address.Segment;
        return method.Entries.All(entry => entry.Node.Address.Segment == segment) ? segment : null;
    }

    private SegmentValue Resolve(int version) => Resolve(version, []);

    private SegmentValue Resolve(int version, HashSet<int> visiting) {
        if (_values.TryGetValue(version, out (SegmentValue Value, bool FactDependent) cached)) {
            _resolvingDependent |= cached.FactDependent;
            return cached.Value;
        }
        if (!visiting.Add(version)) {
            return SegmentValue.Unknown.Instance;
        }
        bool outer = _resolvingDependent;
        _resolvingDependent = false;
        IrSsaVersionInfo info = _ssa.Info(version);
        // The code segment a method-entry version holds is the same wherever the dominator
        // tree materializes it; other registers at a non-entry root may carry a call's or a
        // transfer's unknown effect, so they keep the unresolved path.
        SegmentValue value = info.Kind switch {
            IrSsaDefinitionKind.MethodEntry
                when info.Value == IrSsaValue.Cs || _entryBlocks.Contains(info.Block)
                    => EntryValue(info.Value, version, info.Block),
            IrSsaDefinitionKind.Phi => PhiValue(version, visiting),
            _ => CallOrDefinitionValue(version, info, visiting)
        };
        bool dependent = _resolvingDependent;
        _resolvingDependent = outer || dependent;
        visiting.Remove(version);
        _values[version] = (value, dependent);
        return value;
    }

    private SegmentValue EntryValue(IrSsaValue value, int version, IrBlock block) {
        _resolvingDependent = true;
        if (value == IrSsaValue.Cs && _codeSegment is { } segment) {
            return new SegmentValue.Known(segment);
        }
        IrSegmentEntryFact? fact = _facts.EntryFactOf(block);
        if (fact is null) {
            return new SegmentValue.Symbolic(version);
        }
        if (fact.KnownValues.TryGetValue(value, out ushort known)) {
            return new SegmentValue.Known(known);
        }
        if (fact.EqualTo.TryGetValue(value, out IrSsaValue canonical)) {
            int root = _ssa.VersionBefore(block, 0, canonical);
            return root >= 0 ? new SegmentValue.Symbolic(root) : SegmentValue.Unknown.Instance;
        }
        return new SegmentValue.Symbolic(version);
    }

    /// <summary>A version a call defines resolves from what the callees leave behind; anything else
    /// resolves from its defining statement.</summary>
    private SegmentValue CallOrDefinitionValue(int version, IrSsaVersionInfo info,
        HashSet<int> visiting) {
        if (_callDefinedVersions.Contains(version)) {
            _resolvingDependent = true;
        }
        if (_facts.TryCallValue(version, out IrSegmentCallValue? callValue) && callValue is not null) {
            return callValue switch {
                IrSegmentCallValue.Known known => new SegmentValue.Known(known.Value),
                IrSegmentCallValue.SameAsRead read => Resolve(read.Version, visiting),
                _ => SegmentValue.Unknown.Instance
            };
        }
        return DefinitionValue(version, info, visiting);
    }

    private SegmentValue PhiValue(int version, HashSet<int> visiting) {
        IrSsaPhi? phi = _ssa.Phis.FirstOrDefault(candidate => candidate.Result == version);
        if (phi is null || phi.HasEntryOperand) {
            return SegmentValue.Unknown.Instance;
        }
        SegmentValue? value = null;
        foreach ((IrBlock _, int operand) in phi.Operands) {
            SegmentValue operandValue = Resolve(operand, visiting);
            if (operandValue is SegmentValue.Unknown
                || (value is not null && !value.Equals(operandValue))) {
                return SegmentValue.Unknown.Instance;
            }
            value = operandValue;
        }
        return value ?? SegmentValue.Unknown.Instance;
    }

    private SegmentValue DefinitionValue(int version, IrSsaVersionInfo info, HashSet<int> visiting) {
        IrStatement? statement = _ssa.DefiningStatement(version);
        if (statement is null) {
            return SegmentValue.Unknown.Instance;
        }
        if (statement.Node is InterruptServiceCallNode) {
            // A direct service call defines only its result targets, whose values are whatever
            // the service returned.
            return SegmentValue.Unknown.Instance;
        }
        if (info.Value == IrSsaValue.Direction
            && statement.Node is BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: CpuFlagNode { FlagMask: Flags.Direction },
                Right: ConstantNode direction }) {
            return new SegmentValue.Known(direction.Value != 0 ? (ushort)1 : (ushort)0);
        }
        if (statement.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode left,
                Right: ValueNode right }
            || !IrSegmentValueResolver.CoversWord(info.Value, left)) {
            return SegmentValue.Unknown.Instance;
        }
        if (IrSegmentValueResolver.ConstantWordOf(right) is { } constant) {
            return new SegmentValue.Known(constant);
        }
        if (right is RegisterNode source) {
            int sourceVersion = _ssa.VersionUsed(statement, SourceValueOf(source));
            if (sourceVersion < 0) {
                return SegmentValue.Unknown.Instance;
            }
            SegmentValue resolved = Resolve(sourceVersion, visiting);
            if (resolved is not SegmentValue.Unknown) {
                return resolved;
            }
            return RootValue(sourceVersion);
        }
        if (right is SegmentedPointerNode or AbsolutePointerNode
            or StackCallValueNode { Operation: StackOperation.Pop or StackOperation.Peek }) {
            return StackValue(statement, visiting);
        }
        return SegmentValue.Unknown.Instance;
    }

    /// <summary>The value <paramref name="statement"/> reads off the stack: every stack read of the
    /// statement must be complete, and every slot definition of every read must push the same value.
    /// </summary>
    private SegmentValue StackValue(IrStatement statement, HashSet<int> visiting) {
        SegmentValue? value = null;
        foreach (IrStackSlotRead read in _slots.ReadsOf(statement)) {
            if (!read.Complete) {
                return SegmentValue.Unknown.Instance;
            }
            foreach (SegmentValue pushed in read.Definitions
                    .Select(definition => PushedValue(definition, visiting))) {
                if (pushed is SegmentValue.Unknown
                    || (value is not null && !value.Equals(pushed))) {
                    return SegmentValue.Unknown.Instance;
                }
                value = pushed;
            }
        }
        return value ?? SegmentValue.Unknown.Instance;
    }

    private SegmentValue PushedValue(IrStackSlotDefinition definition, HashSet<int> visiting) {
        if (definition.Statement.Node is not StackCallNode {
                Operation: StackOperation.Push } push
            || push.Arguments is not [ValueNode argument]) {
            return SegmentValue.Unknown.Instance;
        }
        if (IrSegmentValueResolver.ConstantWordOf(argument) is { } constant) {
            return new SegmentValue.Known(constant);
        }
        if (argument is not RegisterNode source) {
            return SegmentValue.Unknown.Instance;
        }
        int sourceVersion = _ssa.VersionUsed(definition.Statement, SourceValueOf(source));
        return sourceVersion < 0 ? SegmentValue.Unknown.Instance : Resolve(sourceVersion, visiting);
    }

    /// <summary>A copy source whose version no statement defines is its own root; one a statement
    /// defines but does not resolve is unknown.</summary>
    private SegmentValue RootValue(int version) {
        IrSsaVersionInfo info = _ssa.Info(version);
        if (info.Kind is IrSsaDefinitionKind.Definition or IrSsaDefinitionKind.PartialDefinition
            && _ssa.DefiningStatement(version) is null) {
            return new SegmentValue.Symbolic(version);
        }
        return SegmentValue.Unknown.Instance;
    }

    private static IrSsaValue SourceValueOf(RegisterNode source) {
        if (source is SegmentRegisterNode) {
            return (IrSsaValue)((int)IrSsaValue.Es + source.RegisterIndex);
        }
        return (IrSsaValue)source.RegisterIndex;
    }
}
