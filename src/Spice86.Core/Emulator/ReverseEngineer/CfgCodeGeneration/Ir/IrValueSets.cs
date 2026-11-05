namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>Conversions between <see cref="CpuValues"/> and the register / status-flag sets it packs.</summary>
internal static class IrValueSets {
    /// <summary>The <see cref="CpuValues"/> bytes each register shape covers, indexed by
    /// <see cref="RegisterNode.RegisterIndex"/>: the x86 8-bit index picks the low (0-3) or high
    /// (4-7) byte of AX, CX, DX and BX.</summary>
    private static readonly CpuValues[] ByteMasks = [
        CpuValues.Al, CpuValues.Cl, CpuValues.Dl, CpuValues.Bl,
        CpuValues.Ah, CpuValues.Ch, CpuValues.Dh, CpuValues.Bh];
    private static readonly CpuValues[] WordMasks = [
        CpuValues.Ax, CpuValues.Cx, CpuValues.Dx, CpuValues.Bx,
        CpuValues.Sp, CpuValues.Bp, CpuValues.Si, CpuValues.Di];

    /// <summary>The value, mask and status flag of every tracked value, in <see cref="IrSsaValue"/>
    /// order: the eight 32-bit registers, the six segment registers and the seven flags.</summary>
    internal static readonly (IrSsaValue Value, CpuValues Mask, StatusFlags Flag)[] Values = [
        (IrSsaValue.Eax, CpuValues.Eax, StatusFlags.None),
        (IrSsaValue.Ecx, CpuValues.Ecx, StatusFlags.None),
        (IrSsaValue.Edx, CpuValues.Edx, StatusFlags.None),
        (IrSsaValue.Ebx, CpuValues.Ebx, StatusFlags.None),
        (IrSsaValue.Esp, CpuValues.Esp, StatusFlags.None),
        (IrSsaValue.Ebp, CpuValues.Ebp, StatusFlags.None),
        (IrSsaValue.Esi, CpuValues.Esi, StatusFlags.None),
        (IrSsaValue.Edi, CpuValues.Edi, StatusFlags.None),
        (IrSsaValue.Es, CpuValues.Es, StatusFlags.None),
        (IrSsaValue.Cs, CpuValues.Cs, StatusFlags.None),
        (IrSsaValue.Ss, CpuValues.Ss, StatusFlags.None),
        (IrSsaValue.Ds, CpuValues.Ds, StatusFlags.None),
        (IrSsaValue.Fs, CpuValues.Fs, StatusFlags.None),
        (IrSsaValue.Gs, CpuValues.Gs, StatusFlags.None),
        (IrSsaValue.Carry, CpuValues.Carry, StatusFlags.Carry),
        (IrSsaValue.Parity, CpuValues.Parity, StatusFlags.Parity),
        (IrSsaValue.Auxiliary, CpuValues.Auxiliary, StatusFlags.Auxiliary),
        (IrSsaValue.Zero, CpuValues.Zero, StatusFlags.Zero),
        (IrSsaValue.Sign, CpuValues.Sign, StatusFlags.Sign),
        (IrSsaValue.Overflow, CpuValues.Overflow, StatusFlags.Overflow),
        (IrSsaValue.Direction, CpuValues.Direction, StatusFlags.None)];

    static IrValueSets() {
        for (int i = 0; i < Values.Length; i++) {
            if (Values[i].Value != (IrSsaValue)i) {
                throw new InvalidOperationException(
                    $"Values[{i}] is {Values[i].Value}, not {(IrSsaValue)i}.");
            }
        }
    }

    /// <summary>The <see cref="Values"/> rows that carry a status flag.</summary>
    internal static readonly (IrSsaValue Value, CpuValues Mask, StatusFlags Flag)[] StatusFlagRows =
        [.. Values.Where(row => row.Flag != StatusFlags.None)];

    /// <summary>The <see cref="IrSsaValue"/>s whose mask lies inside <paramref name="set"/>, in
    /// <see cref="IrSsaValue"/> order.</summary>
    internal static IrSsaValue[] ValuesIn(CpuValues set) =>
        [.. Values.Where(row => (row.Mask & ~set) == CpuValues.None).Select(row => row.Value)];

    /// <summary>
    /// The <see cref="CpuValues"/> bytes <paramref name="register"/> covers: one byte for an 8-bit
    /// register (the x86 index picks the low or high byte of its 16-bit register), two for a 16-bit
    /// register, four for a 32-bit one, its own bit for a segment register.
    /// </summary>
    public static CpuValues Of(RegisterNode register) {
        int index = register.RegisterIndex;
        if (register is SegmentRegisterNode) {
            return Values[(int)IrSsaValue.Es + index].Mask;
        }
        return register.DataType.BitWidth switch {
            BitWidth.BYTE_8 => ByteMasks[index],
            BitWidth.WORD_16 => WordMasks[index],
            _ => Values[index].Mask
        };
    }

    /// <summary>The <see cref="CpuValues"/> of a status-flag set.</summary>
    public static CpuValues FromStatusFlags(StatusFlags flags) {
        CpuValues result = CpuValues.None;
        foreach ((_, CpuValues mask, StatusFlags flag) in StatusFlagRows) {
            if ((flags & flag) != 0) {
                result |= mask;
            }
        }
        return result;
    }

    /// <summary>The low-word mask of each general register, in register-index order.</summary>
    internal static readonly CpuValues[] RegisterLowWordMasks = WordMasks;

    /// <summary>
    /// The read node a single-field <see cref="CpuValues"/> mask stands for: an 8, 16 or 32-bit
    /// <see cref="RegisterNode"/> for a register field, a <see cref="SegmentRegisterNode"/> for a
    /// segment register and a single-flag <see cref="CpuFlagNode"/> for a status or direction flag.
    /// </summary>
    /// <exception cref="KeyNotFoundException"><paramref name="field"/> is not one of the masks the
    /// tables above list.</exception>
    public static ValueNode NodeOf(CpuValues field) => NodeFactories[field]();

    private static readonly IReadOnlyDictionary<CpuValues, Func<ValueNode>> NodeFactories =
        BuildNodeFactories();

    private static IReadOnlyDictionary<CpuValues, Func<ValueNode>> BuildNodeFactories() {
        Dictionary<CpuValues, Func<ValueNode>> factories = [];
        for (int index = 0; index < ByteMasks.Length; index++) {
            int registerIndex = index;
            factories[ByteMasks[index]] = () => new RegisterNode(DataType.UINT8, registerIndex);
            factories[WordMasks[index]] = () => new RegisterNode(DataType.UINT16, registerIndex);
            factories[Values[index].Mask] = () => new RegisterNode(DataType.UINT32, registerIndex);
        }
        for (int index = 0; index <= (int)IrSsaValue.Gs - (int)IrSsaValue.Es; index++) {
            int registerIndex = index;
            factories[Values[(int)IrSsaValue.Es + index].Mask] =
                () => new SegmentRegisterNode(registerIndex);
        }
        foreach ((_, CpuValues mask, StatusFlags flag) in StatusFlagRows) {
            factories[mask] = () => new CpuFlagNode((ushort)flag);
        }
        factories[CpuValues.Direction] = () => new CpuFlagNode(CPU.Flags.Direction);
        return factories;
    }
}
