namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;

using System.Numerics;

/// <summary>
/// What <see cref="IrConstants"/> computed for one method: the per-byte constant lattice of every
/// general-register SSA version, the per-position set of registers whose current version was defined
/// cleanly (no interrupt delivery, no unknown-like effect and no method entry on any path between the
/// definition and the position), and the queries the branch pass runs on them.
/// </summary>
internal sealed class IrConstantFacts {
    /// <summary>Bytes per register the lattice tracks.</summary>
    public const int ByteCount = 4;
    /// <summary>Every byte of one register.</summary>
    public const uint AllBytes = 0xF;
    /// <summary>Bit set over <see cref="IrSsaValue"/> covering the eight general registers.</summary>
    public const uint GeneralRegisters = (1u << ((int)IrSsaValue.Edi + 1)) - 1;

    private readonly IrSsa _ssa;
    private readonly Dictionary<IrBlock, uint[]> _cleanBefore = [];

    internal IrConstantFacts(IrSsa ssa) {
        _ssa = ssa;
        Known = new uint[ssa.VersionCount];
        Undetermined = new uint[ssa.VersionCount];
        Value = new uint[ssa.VersionCount];
    }

    /// <summary>The bytes of a version the lattice proves constant, as a byte mask.</summary>
    internal uint[] Known { get; }

    /// <summary>The bytes of a version not yet determined (no executable information).</summary>
    internal uint[] Undetermined { get; }

    /// <summary>The known bytes of a version, packed one byte per lane.</summary>
    internal uint[] Value { get; }

    /// <summary>Records the clean registers before position <paramref name="position"/> of <paramref name="block"/>.</summary>
    internal void SetClean(IrBlock block, int position, uint mask) {
        if (!_cleanBefore.TryGetValue(block, out uint[]? positions)) {
            positions = new uint[block.Statements.Count + 1];
            _cleanBefore[block] = positions;
        }
        positions[position] = mask;
    }

    /// <summary>The bytes <paramref name="effects"/> writes of <paramref name="value"/>'s register, as a byte
    /// mask. The direction flag is one bit: lane 0.</summary>
    internal static uint WrittenBytes(IrEffects effects, IrSsaValue value) {
        if (value == IrSsaValue.Direction) {
            return ((effects.MustWrite | effects.MayWrite) & IrValues.Direction) != IrValues.None ? 1u : 0u;
        }
        return (uint)(((ulong)((effects.MustWrite | effects.MayWrite) & IrSsaValues.MaskOf(value))
            >> (ByteCount * (int)value)) & AllBytes);
    }

    /// <summary>The 32-bit mask with a lane per byte of <paramref name="bytes"/>.</summary>
    internal static uint Expand(uint bytes) {
        uint mask = 0;
        for (int b = 0; b < ByteCount; b++) {
            if ((bytes & (1U << b)) != 0) {
                mask |= 0xFFU << (8 * b);
            }
        }
        return mask;
    }

    /// <summary>The known value of <paramref name="register"/>'s bytes before position
    /// <paramref name="position"/> of <paramref name="block"/> (an index of
    /// <c>block.Statements.Count</c> means before the terminator), or false when a covered byte can
    /// vary or an external event may have written the register since.</summary>
    public bool TryGetBefore(IrBlock block, int position, RegisterNode register, out ulong value) {
        value = 0;
        if (register is SegmentRegisterNode) {
            return false;
        }
        ulong bits = (ulong)IrValueSets.Of(register);
        int registerIndex = BitOperations.TrailingZeroCount(bits) / ByteCount;
        uint cleanMask = _cleanBefore.TryGetValue(block, out uint[]? clean) ? clean[position] : 0;
        if (registerIndex == (int)IrSsaValue.Esp || (cleanMask & (1u << registerIndex)) == 0) {
            return false;
        }
        int id = _ssa.VersionBefore(block, position, (IrSsaValue)registerIndex);
        int shift = 0;
        for (int b = 0; b < ByteCount; b++) {
            if ((bits & (1UL << (ByteCount * registerIndex + b))) == 0) {
                continue;
            }
            if ((Known[id] & (1U << b)) == 0) {
                return false;
            }
            value |= ((Value[id] >> (8 * b)) & 0xFFUL) << shift;
            shift += 8;
        }
        return true;
    }

    /// <summary>The known direction flag before position <paramref name="position"/> of
    /// <paramref name="block"/>, or false when the version can vary. A write the lattice cannot
    /// evaluate (a flag-register restore, an interrupt delivery, unknown code) creates a version
    /// whose lane is not known, so no separate cleanness check is needed.</summary>
    public bool TryGetDirection(IrBlock block, int position, out bool direction) {
        direction = false;
        int id = _ssa.VersionBefore(block, position, IrSsaValue.Direction);
        if ((Known[id] & 1) == 0) {
            return false;
        }
        direction = (Value[id] & 1) != 0;
        return true;
    }

    /// <summary>Evaluates <paramref name="node"/> the way <see cref="ConstantEvaluation"/> does,
    /// with the known register bytes and direction flag before position <paramref name="position"/>
    /// of <paramref name="block"/> as leaves.</summary>
    public bool TryEvaluate(IVisitableAstNode node, IrBlock block, int position, out ulong value) =>
        ConstantEvaluation.TryEvaluate(node,
            leaf => leaf switch {
                RegisterNode register
                    when TryGetBefore(block, position, register, out ulong leafValue) => leafValue,
                CpuFlagNode { FlagMask: Flags.Direction }
                    when TryGetDirection(block, position, out bool direction) => direction ? 1UL : 0UL,
                _ => null
            },
            out value);
}
