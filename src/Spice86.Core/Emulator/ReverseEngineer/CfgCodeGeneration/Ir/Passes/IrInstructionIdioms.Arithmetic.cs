namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Relations;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// The arithmetic sequence recognizers of <see cref="IrInstructionIdioms"/>: the lowered statement
/// runs compilers emit for signed power-of-two division and modulo
/// (<c>cwd; sub|and+add; sar</c>, <c>cwd; xor; sub; and; xor; sub</c>), unsigned reciprocal
/// multiply (<c>mul; shr</c>) and scalar constant multiplication through a saved-copy chain
/// (<c>shl; shl; add s; shl</c>).
/// <para>Legality is the same as the other sequence rewrites: only markers may sit inside the
/// window, every consumed statement is <see cref="Joinable"/> and writes nothing outside the
/// accounted general registers and status flags, and every value a removed statement defines must
/// have its live uses inside the window - a scratch write still read afterwards, a flag version
/// observed by a later instruction or a phi/terminator use refuses the match. The replacement
/// sits at the last matched origin and only reads values that resolve to the versions visible
/// before the window, so removing the earlier statements cannot change what it reads.</para>
/// </summary>
internal static partial class IrInstructionIdioms {
    /// <summary>The register bytes a sequence may write: every general register except SP.</summary>
    private static readonly CpuValues SequenceRegisters =
        IrSsaValues.MaskOf(IrSsaValue.Eax) | IrSsaValues.MaskOf(IrSsaValue.Ecx)
        | IrSsaValues.MaskOf(IrSsaValue.Edx) | IrSsaValues.MaskOf(IrSsaValue.Ebx)
        | IrSsaValues.MaskOf(IrSsaValue.Ebp) | IrSsaValues.MaskOf(IrSsaValue.Esi)
        | IrSsaValues.MaskOf(IrSsaValue.Edi);

    /// <summary>The arithmetic recognizers, in order. <paramref name="index"/> is the first
    /// candidate index of a sequence.</summary>
    private static SequenceRewrite? ArithmeticSequence(IrBlock block, int index, IrSsa ssa) =>
        SignedPow2Division(block, index, ssa)
        ?? SignedPow2Modulo(block, index, ssa)
        ?? ReciprocalDivision(block, index, ssa)
        ?? ConstantMultiply(block, index, ssa);

    /// <summary>Closes a match: every effective statement of the window is consumed and joinable,
    /// writes stay inside general registers and status flags, every version a removed statement
    /// defines is live only inside the consumed set, and every read of the replacement resolves
    /// to the version visible before the window once the consumed writes are gone.</summary>
    private static bool SealWindow(IrBlock block, int first, int last, int[] consumed,
        CpuValues keptRegisters, ValueNode replacement, IrSsa ssa) =>
        SealWindow(block, first, last, consumed,
            new Dictionary<IrStatement, CpuValues> {
                [block.Statements[last]] = keptRegisters
            }, replacement, ssa);

    /// <summary>Same window rule with the kept definitions chosen per statement: a statement in
    /// <paramref name="keptByStatement"/> may leave its covered register versions live outside
    /// the window (the final definition of a register the caller rewrites itself); flag writes
    /// and every other statement's versions still die inside.</summary>
    internal static bool SealWindow(IrBlock block, int first, int last, int[] consumed,
        IReadOnlyDictionary<IrStatement, CpuValues> keptByStatement, ValueNode replacement,
        IrSsa ssa) {
        List<IrStatement> statements = block.Statements;
        HashSet<int> consumedIndices = [.. consumed];
        for (int i = first; i <= last; i++) {
            if (!consumedIndices.Contains(i) && !IsMarker(statements[i])) {
                return false;
            }
        }
        HashSet<IrStatement> consumedSet = [.. consumedIndices.Select(i => statements[i])];
        IrStatement lastStatement = statements[last];
        CpuValues allowed = SequenceRegisters | CpuValues.StatusFlags;
        foreach (int i in consumedIndices) {
            IrStatement statement = statements[i];
            if (!Joinable(statement)
                || (statement.Effects.MayWrite & ~allowed) != CpuValues.None) {
                return false;
            }
            bool kept = keptByStatement.TryGetValue(statement,
                out CpuValues keptRegisters);
            foreach ((IrSsaValue value, int version) in ssa.Definitions(statement)) {
                if (kept && !IrFlagLiveness.IsStatusFlagValue(value)
                    && (IrSsaValues.MaskOf(value) & keptRegisters) != CpuValues.None) {
                    continue;
                }
                CpuValues removedBytes = statement.Effects.MustWrite & IrSsaValues.MaskOf(value);
                foreach (IrSsaUse use in ssa.LiveUses(version)) {
                    // The use a later partial definition makes of this version is safe only when
                    // that statement fully overwrites every byte the removed write touched: the
                    // surviving bits never depended on it. A nonfaulting overwriter is required -
                    // a faulting write would leave the old bytes visible.
                    if (use.ConditionalNewVersion >= 0 && use.Statement is { } overwriter
                        && !overwriter.Effects.MayFault
                        && (removedBytes & ~overwriter.Effects.MustWrite) == CpuValues.None) {
                        continue;
                    }
                    object? site = use.Terminator ?? (object?)use.Statement;
                    if (site is null || !consumedSet.Contains(site)) {
                        return false;
                    }
                }
            }
        }
        // Every value the replacement reads must still resolve to its pre-window version once the
        // consumed writes are gone: a value written inside the window falls back to the version
        // before its last consumed definition, which must be the version before the window.
        IrEffects moved = IrEffectsVisitor.Of(replacement, false);
        // A memory read substituted from a consumed statement is version-checked like the
        // register reads; IO reads cannot be moved.
        if ((moved.TokenReads & ~IrTokens.Memory) != IrTokens.None) {
            return false;
        }
        uint bits = IrSsaValues.ValueBits(moved.Reads)
            | IrSsaValues.TokenBits(moved.TokenReads);
        for (int v = 0; v <= (int)IrSsaValue.Memory; v++) {
            if ((bits & (1u << v)) == 0) {
                continue;
            }
            IrSsaValue value = (IrSsaValue)v;
            int version = ssa.VersionBefore(block, last, value);
            while (version >= 0) {
                IrSsaVersionInfo info = ssa.Info(version);
                int defIndex = DefIndex(block, ssa, version);
                if (info.Kind is not (IrSsaDefinitionKind.Definition
                        or IrSsaDefinitionKind.PartialDefinition)
                    || !ReferenceEquals(info.Block, block)
                    || defIndex < first || defIndex >= last
                    || !consumedIndices.Contains(defIndex)) {
                    break;
                }
                version = ssa.VersionBefore(block, defIndex, value);
            }
            if (version != ssa.VersionBefore(block, first, value)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>The <c>target = value</c> register assignment of <paramref name="statement"/>,
    /// or null.</summary>
    private static bool AssignOf(IrStatement statement,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RegisterNode? target,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BinaryOperationNode? assign) {
        if (statement.Node is BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode register,
                Right: { } } node && register is not SegmentRegisterNode) {
            target = register;
            assign = node;
            return true;
        }
        target = null;
        assign = null;
        return false;
    }

    /// <summary><paramref name="node"/> without its outermost same-width conversion.</summary>
    private static ValueNode UnwrapConversion(ValueNode node) =>
        node is TypeConversionNode { Value: { } inner }
            && inner.DataType.BitWidth == node.DataType.BitWidth ? inner : node;

    private static ulong? BitsOf(ValueNode node) =>
        ConstantEvaluation.Resolved(node) is { } constant
            ? ConstantEvaluation.Bits(node.DataType, constant.Value)
            : null;

    /// <summary>The <c>r = (T)((S)r &gt;&gt; k)</c> or <c>d = (T)((S)r &gt;&gt; k)</c> shift
    /// assignment of <paramref name="statement"/>: target, source and constant count, signed when
    /// the source carries a same-width signed cast.</summary>
    private static (RegisterNode Target, RegisterNode Source, int Count, bool Signed)? ShiftOf(
        IrStatement statement) {
        if (!AssignOf(statement, out RegisterNode? target, out BinaryOperationNode? assign)
            || UnwrapConversion(assign.Right) is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.RIGHT_SHIFT, Left: { } shiftLeft,
                Right: { } shiftCount } shift) {
            return null;
        }
        RegisterNode? source;
        bool signed;
        switch (shiftLeft) {
            // Only a signed cast of the target's own width means an arithmetic shift: a wider
            // signed conversion like (int)AX >> 15 reads different bits than (short)AX >> 15.
            case TypeConversionNode { DataType: { Signed: true } castType,
                    Value: RegisterNode casted }
                when castType == DataType.SignedFromBitWidth(target.DataType.BitWidth):
                source = casted;
                signed = true;
                break;
            case RegisterNode plain:
                source = plain;
                signed = false;
                break;
            default:
                return null;
        }
        // A SAR keeps the signed result type of its operand (INT16 for a signed cast,
        // UINT16 for a plain register): match on width and agree on signedness instead of
        // requiring the exact destination type.
        if (shift.DataType.BitWidth != target.DataType.BitWidth
            || shift.DataType.Signed != signed || source.DataType != target.DataType
            || BitsOf(shiftCount) is not { } count || count > 15) {
            return null;
        }
        return (target, source, (int)count, signed);
    }

    /// <summary>The <c>r = r &lt;op&gt; operand</c> assignment of <paramref name="statement"/>:
    /// a binary operation of the node's own width whose left side is the target register.</summary>
    private static bool SelfBinaryOf(IrStatement statement, BinaryOperation operation,
        RegisterNode target,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ValueNode? operand) {
        operand = null;
        if (AssignOf(statement, out RegisterNode? left, out BinaryOperationNode? assign)
            && left.Equals(target)
            && UnwrapConversion(assign.Right) is BinaryOperationNode {
                BinaryOperation: { } op, Left: { } l, Right: { } r } binary
            && op == operation && binary.DataType == target.DataType
            && l.Equals(target)) {
            operand = r;
        }
        return operand is not null;
    }


    /// <summary>The index in <paramref name="block"/>'s statements of the definition site of
    /// <paramref name="version"/>, or -1. Statement indices in <see cref="IrSsaVersionInfo"/> go
    /// stale when a pass edits the list, so the site is found by the definitions table.</summary>
    private static int DefIndex(IrBlock block, IrSsa ssa, int version) =>
        ssa.DefiningStatement(version) is { } definer
            ? block.Statements.IndexOf(definer)
            : -1;


    // ------------------------------------------------------------------
    // Signed power-of-two division and modulo
    // ------------------------------------------------------------------

    /// <summary>The <c>cwd</c> sign extension <c>d = (ushort)((short)r &gt;&gt; 15)</c>.</summary>
    private static bool SignExtension(IrStatement statement,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RegisterNode? sign,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RegisterNode? source) {
        sign = null;
        source = null;
        if (ShiftOf(statement) is { } shift && shift.Signed && shift.Count == 15
            && !shift.Target.Equals(shift.Source)
            && shift.Target.DataType == DataType.UINT16) {
            sign = shift.Target;
            source = shift.Source;
        }
        return source is not null;
    }

    /// <summary>The emitted signed division: <c>r = (ushort)((int)(short)r / d)</c>.</summary>
    private static IVisitableAstNode SignedDivide(RegisterNode target, BinaryOperation operation,
        int divisor) {
        RegisterNode fresh = new(DataType.UINT16, target.RegisterIndex);
        return new BinaryOperationNode(DataType.UINT16, fresh, BinaryOperation.ASSIGN,
            new TypeConversionNode(DataType.UINT16,
                new BinaryOperationNode(DataType.INT32,
                    new TypeConversionNode(DataType.INT16, fresh),
                    operation, new ConstantNode(DataType.INT32, (ulong)divisor))));
    }

    /// <summary><c>cwd; r = r - d; sar r,1</c> is signed /2, and
    /// <c>cwd; d = d &amp; (2^k - 1); r = r + d; sar r,k</c> is signed /2^k.</summary>
    private static SequenceRewrite? SignedPow2Division(IrBlock block, int index, IrSsa ssa) {
        List<IrStatement> statements = block.Statements;
        if (!SignExtension(statements[index], out RegisterNode? sign, out RegisterNode? source)) {
            return null;
        }
        List<int> consumed = [index];
        int cursor = NextEffective(statements, index + 1);
        int shift;
        if (cursor >= 0
            && SelfBinaryOf(statements[cursor], BinaryOperation.MINUS, source,
                out ValueNode? subtrahend)
            && subtrahend.Equals(sign)) {
            consumed.Add(cursor);
            shift = 1;
        } else {
            if (cursor < 0
                || !SelfBinaryOf(statements[cursor], BinaryOperation.BITWISE_AND, sign,
                    out ValueNode? maskNode)
                || BitsOf(maskNode) is not { } mask
                || mask < 1 || (mask & (mask + 1)) != 0) {
                return null;
            }
            consumed.Add(cursor);
            cursor = NextEffective(statements, cursor + 1);
            if (cursor < 0
                || !SelfBinaryOf(statements[cursor], BinaryOperation.PLUS, source,
                    out ValueNode? addend)
                || !addend.Equals(sign)) {
                return null;
            }
            consumed.Add(cursor);
            int k = 0;
            while ((mask & (1uL << k)) != 0) {
                k++;
            }
            if (mask != (1uL << k) - 1 || k < 1 || k > 15) {
                return null;
            }
            shift = k;
        }
        int sarIndex = NextEffective(statements, cursor + 1);
        if (sarIndex < 0 || ShiftOf(statements[sarIndex]) is not { } sar
            || !sar.Signed || sar.Count != shift
            || !sar.Target.Equals(source) || !sar.Source.Equals(source)) {
            return null;
        }
        consumed.Add(sarIndex);
        IVisitableAstNode node = SignedDivide(source, BinaryOperation.DIVIDE, 1 << shift);
        ValueNode replacement = node is BinaryOperationNode { Right: { } divideRight }
            ? divideRight : source;
        if (!SealWindow(block, index, sarIndex, [.. consumed], IrValueSets.Of(source),
                replacement, ssa)) {
            return null;
        }
        return new SequenceRewrite(sarIndex, node, [.. consumed.Where(i => i != sarIndex)]);
    }

    /// <summary><c>cwd; r ^= d; r -= d; r &amp;= mask; r ^= d; r -= d</c> is the signed modulo by
    /// <c>mask + 1</c>, a power of two: the xor/sub pair computes the absolute value, the mask
    /// keeps the remainder and the second pair restores the sign. <c>short.MinValue</c> is exact
    /// because the absolute value wraps before the mask.</summary>
    private static SequenceRewrite? SignedPow2Modulo(IrBlock block, int index, IrSsa ssa) {
        List<IrStatement> statements = block.Statements;
        if (!SignExtension(statements[index], out RegisterNode? sign, out RegisterNode? source)) {
            return null;
        }
        List<int> consumed = [index];
        int cursor = index;
        ulong modulus = 0;
        foreach (BinaryOperation operation in new[] { BinaryOperation.BITWISE_XOR,
                BinaryOperation.MINUS, BinaryOperation.BITWISE_AND,
                BinaryOperation.BITWISE_XOR, BinaryOperation.MINUS }) {
            cursor = NextEffective(statements, cursor + 1);
            if (cursor < 0
                || !SelfBinaryOf(statements[cursor], operation, source, out ValueNode? operand)) {
                return null;
            }
            if (operation == BinaryOperation.BITWISE_AND) {
                if (BitsOf(operand) is not { } mask || (mask & (mask + 1)) != 0
                    || mask + 1 < 2 || mask + 1 > 0x8000) {
                    return null;
                }
                modulus = mask + 1;
            } else if (!operand.Equals(sign)) {
                return null;
            }
            consumed.Add(cursor);
        }
        int last = consumed[^1];
        IVisitableAstNode node = SignedDivide(source, BinaryOperation.MODULO, (int)modulus);
        ValueNode replacement = node is BinaryOperationNode { Right: { } moduloRight }
            ? moduloRight : source;
        if (!SealWindow(block, index, last, [.. consumed], IrValueSets.Of(source),
                replacement, ssa)) {
            return null;
        }
        return new SequenceRewrite(last, node, [.. consumed.Where(i => i != last)]);
    }

    // ------------------------------------------------------------------
    // Unsigned reciprocal multiply and scalar constant multiply
    // ------------------------------------------------------------------

    /// <summary><c>(low, high) = Mul16(src, m); high = high &gt;&gt;&gt; s</c> becomes
    /// <c>high = (ushort)(src / d)</c> when <see cref="ProvenDivisor"/> proves
    /// <c>floor(src * m / 2^(16+s)) == floor(src / d)</c> over the whole ushort domain: the low
    /// result and the MUL flag versions must die with the sequence.</summary>
    private static SequenceRewrite? ReciprocalDivision(IrBlock block, int index, IrSsa ssa) {
        List<IrStatement> statements = block.Statements;
        if (statements[index].Node is not MultiplicationNode {
                Width: BitWidth.WORD_16, Signed: false } multiplication) {
            return null;
        }
        ValueNode? source = null;
        ulong multiplier = 0;
        foreach ((ValueNode maybeSource, ValueNode maybeMultiplier) in
            new[] { (multiplication.Left, multiplication.Right),
                (multiplication.Right, multiplication.Left) }) {
            if (BitsOf(maybeMultiplier) is { } m && m > 0 && m <= 0xFFFF
                && IrVerifier.IsPureArm(maybeSource)) {
                source = maybeSource;
                multiplier = m;
                break;
            }
        }
        if (source is null || multiplier == 0) {
            return null;
        }
        int shift = NextEffective(statements, index + 1);
        if (shift < 0 || ShiftOf(statements[shift]) is not { } shr) {
            return null;
        }
        if (shr.Signed || !shr.Target.Equals(multiplication.High)
            || !shr.Source.Equals(multiplication.High)) {
            return null;
        }
        if (ProvenDivisor(multiplier, shr.Count) is not { } divisor) {
            return null;
        }
        // A Mul16 operand may be a compound expression: truncate it to the word the MUL consumed
        // before widening, so (uint)source alone cannot smuggle a wider value into the division.
        IVisitableAstNode node = new BinaryOperationNode(DataType.UINT16,
            new RegisterNode(DataType.UINT16, multiplication.High.RegisterIndex),
            BinaryOperation.ASSIGN,
            new BinaryOperationNode(DataType.UINT16,
                new TypeConversionNode(DataType.UINT16, source),
                BinaryOperation.DIVIDE,
                new ConstantNode(DataType.UINT16, divisor)));
        ValueNode replacement = ((BinaryOperationNode)node).Right;
        if (!SealWindow(block, index, shift, [index, shift],
                IrValueSets.Of(multiplication.High), replacement, ssa)) {
            return null;
        }
        return new SequenceRewrite(shift, node, [index]);
    }

    /// <summary>The divisor the pair (multiplier, shift) computes on the full ushort domain, or
    /// null. By construction divisor * multiplier &gt;= scale ensures no bucket starts too low;
    /// checking the largest complete bucket and the final partial bucket ensures no bucket ends
    /// too high. All intermediates fit ulong for the checked range.</summary>
    private static ushort? ProvenDivisor(ulong multiplier, int shift) {
        ulong scale = 1UL << (16 + shift);
        ulong divisor = (scale + multiplier - 1) / multiplier;
        if (divisor is < 2 or > ushort.MaxValue) {
            return null;
        }
        ulong lastInput = ushort.MaxValue;
        ulong fullBuckets = (lastInput + 1) / divisor;
        if (fullBuckets > 0
            && (fullBuckets * divisor - 1) * multiplier >= fullBuckets * scale) {
            return null;
        }
        ulong quotient = lastInput / divisor;
        if (lastInput * multiplier >= (quotient + 1) * scale) {
            return null;
        }
        return (ushort)divisor;
    }

    /// <summary>A run of <c>r &lt;&lt;= k</c>, <c>r += s</c> and <c>r -= s</c> assignments on one
    /// word register multiplies the entry value by a constant. Two coefficients track the entry
    /// register and the saved copy so a mid-chain observed value stays correct; the saved
    /// register must reach the chain through <c>s = r</c>.</summary>
    private static SequenceRewrite? ConstantMultiply(IrBlock block, int index, IrSsa ssa) {
        List<IrStatement> statements = block.Statements;
        if (!AssignOf(statements[index], out RegisterNode? target, out _)
            || target.DataType != DataType.UINT16 || target.RegisterIndex >= 4) {
            return null;
        }
        long coefficient = 1;
        long savedCoefficient = 0;
        RegisterNode? saved = null;
        bool arithmetic = false;
        List<int> consumed = [];
        int cursor = index;
        while (cursor >= 0) {
            IrStatement statement = statements[cursor];
            bool matched = false;
            if (AssignOf(statement, out RegisterNode? write, out BinaryOperationNode? assign)
                && write.Equals(target)
                && UnwrapConversion(assign.Right) is BinaryOperationNode {
                    Left: { } l, Right: { } r, BinaryOperation: { } op,
                    DataType: { } stepType } step
                && stepType == target.DataType) {
                if (op == BinaryOperation.LEFT_SHIFT && l.Equals(target)
                    && BitsOf(r) is { } k && k <= 15) {
                    coefficient = coefficient << (int)k & 0xFFFF;
                    savedCoefficient = savedCoefficient << (int)k & 0xFFFF;
                    matched = true;
                } else if (op == BinaryOperation.PLUS && l.Equals(target) && r.Equals(target)) {
                    coefficient = coefficient * 2 & 0xFFFF;
                    savedCoefficient = savedCoefficient * 2 & 0xFFFF;
                    matched = true;
                    arithmetic = true;
                } else if (op == BinaryOperation.PLUS || op == BinaryOperation.MINUS) {
                    RegisterNode? operand = null;
                    if (l.Equals(target) && SavedRegister(r) is { } addend) {
                        operand = addend;
                    } else if (op == BinaryOperation.PLUS && r.Equals(target)
                        && SavedRegister(l) is { } commuted) {
                        operand = commuted;
                    }
                    if (operand is not null && MergeSaved(ref saved, operand)) {
                        savedCoefficient = op == BinaryOperation.PLUS
                            ? savedCoefficient + 1 & 0xFFFF
                            : savedCoefficient - 1 & 0xFFFF;
                        matched = true;
                        arithmetic = true;
                    }
                }
            }
            if (!matched) {
                break;
            }
            consumed.Add(cursor);
            cursor = NextEffective(statements, cursor + 1);
        }
        if (consumed.Count < 2) {
            return null;
        }
        RegisterNode? source = null;
        long sourceCoefficient = 0;
        if (saved is not null) {
            // The saved register must provably hold the target's entry value: a reaching
            // s = r copy before the chain, itself not written inside the window.
            int savedVersion = ssa.VersionBefore(block, consumed[0],
                (IrSsaValue)saved.RegisterIndex);
            if (savedVersion < 0) {
                return null;
            }
            IrSsaVersionInfo info = ssa.Info(savedVersion);
            int copyIndex = DefIndex(block, ssa, savedVersion);
            if (info.Kind is not (IrSsaDefinitionKind.Definition
                    or IrSsaDefinitionKind.PartialDefinition)
                || !ReferenceEquals(info.Block, block)
                || copyIndex < 0 || copyIndex >= consumed[0]
                || !AssignOf(block.Statements[copyIndex], out RegisterNode? copyTarget,
                        out BinaryOperationNode? copy)
                || !copyTarget.Equals(saved) || !copy.Right.Equals(target)) {
                return null;
            }
            bool snapshot = ssa.VersionBefore(block, copyIndex,
                    (IrSsaValue)target.RegisterIndex)
                == ssa.VersionBefore(block, consumed[0], (IrSsaValue)target.RegisterIndex);
            if (snapshot) {
                source = new RegisterNode(saved.DataType, saved.RegisterIndex);
                sourceCoefficient = coefficient + savedCoefficient & 0xFFFF;
            }
        }
        int last = consumed[^1];
        if (!arithmetic && !ReadsArithmetic(ssa, statements[last], target)) {
            // A pure shift chain is the existing single-shift folder's job; only an
            // arithmetic reader or an add inside the chain makes it a multiplication.
            return null;
        }
        ValueNode multiplied;
        if (source is not null) {
            multiplied = sourceCoefficient == 0
                ? new ConstantNode(DataType.UINT16, 0)
                : Product(source, (ulong)sourceCoefficient);
        } else if (saved is null) {
            RegisterNode entry = new(DataType.UINT16, target.RegisterIndex);
            multiplied = coefficient == 0
                ? new ConstantNode(DataType.UINT16, 0)
                : Product(entry, (ulong)coefficient);
        } else {
            RegisterNode entry = new(DataType.UINT16, target.RegisterIndex);
            multiplied = new BinaryOperationNode(DataType.UINT16,
                Product(entry, (ulong)coefficient), BinaryOperation.PLUS,
                Product(saved, (ulong)(savedCoefficient & 0xFFFF)));
        }
        IVisitableAstNode node = IrConstantFolder.FoldConstantTerms(IrConstantFolder.Fold(
            new BinaryOperationNode(DataType.UINT16,
                new RegisterNode(DataType.UINT16, target.RegisterIndex),
                BinaryOperation.ASSIGN,
                new TypeConversionNode(DataType.UINT16, multiplied)),
            IrConstantFolder.NoVariableConstants));
        ValueNode replacement = node is BinaryOperationNode { Right: { } foldedRight }
            ? foldedRight : new ConstantNode(DataType.UINT16, 0);
        if (!SealWindow(block, index, last, [.. consumed], IrValueSets.Of(target),
                replacement, ssa)) {
            return null;
        }
        return new SequenceRewrite(last, node, [.. consumed.Where(i => i != last)]);
    }

    /// <summary>Whether the version <paramref name="statement"/> defines for
    /// <paramref name="target"/> is read by an arithmetic operation (+, -, *, /, %) - the
    /// marker that a shift chain computed a product rather than bit patterns.</summary>
    private static bool ReadsArithmetic(IrSsa ssa, IrStatement statement,
        RegisterNode target) {
        foreach ((IrSsaValue value, int version) in ssa.Definitions(statement)) {
            if (value != (IrSsaValue)target.RegisterIndex) {
                continue;
            }
            foreach (IrSsaUse use in ssa.LiveUses(version)) {
                if (use.ConditionalNewVersion >= 0 || use.Statement is not { } reader) {
                    continue;
                }
                bool arithmetic = IrAstChildren.Descendants(reader.Node)
                    .OfType<BinaryOperationNode>()
                    .Any(operation => operation.BinaryOperation is BinaryOperation.PLUS
                        or BinaryOperation.MINUS or BinaryOperation.MULTIPLY
                        or BinaryOperation.DIVIDE or BinaryOperation.MODULO
                        && IrAstChildren.Descendants(operation).OfType<RegisterNode>()
                            .Any(read => read.Equals(target)));
                if (arithmetic) {
                    return true;
                }
            }
        }
        return false;
    }

    private static RegisterNode? SavedRegister(ValueNode node) =>
        node is RegisterNode register && register is not SegmentRegisterNode
            && register.DataType == DataType.UINT16 && register.RegisterIndex < 4
                ? register : null;

    private static bool MergeSaved(ref RegisterNode? saved, RegisterNode candidate) {
        if (saved is null || saved.Equals(candidate)) {
            saved = candidate;
            return true;
        }
        return false;
    }

    private static ValueNode Product(ValueNode source, ulong coefficient) {
        // Only the low 16 bits of the product survive the assignment, so a word multiply
        // with a word coefficient is exact - no uint widening is needed.
        ValueNode word = new TypeConversionNode(DataType.UINT16, source);
        if (coefficient == 1) {
            return word;
        }
        return new BinaryOperationNode(DataType.UINT16, word, BinaryOperation.MULTIPLY,
            new ConstantNode(DataType.UINT16, coefficient));
    }
}
