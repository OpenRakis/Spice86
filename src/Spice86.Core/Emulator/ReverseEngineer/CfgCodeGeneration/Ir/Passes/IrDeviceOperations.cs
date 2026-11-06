namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Replaces a recognized sequence of port accesses inside one basic block by the device operation
/// the sequence performs: <c>Cmos.ReadRegister</c> / <c>Cmos.WriteRegister</c> for the 0x70/0x71
/// pair, <c>Pit.ProgramChannel</c> for a low/high control word plus its two channel data writes,
/// <c>Vga.SetPaletteEntry</c> for the DAC write index plus its three data writes,
/// <c>Vga.WriteSequencerRegister</c> / <c>Vga.WriteGraphicsControllerRegister</c> for the 0x3C4/5
/// and 0x3CE/F pairs (including the one-instruction word write at an index port), and
/// <c>Opl.WriteRegister</c> for the OPL address/data pairs. Runs late, after expression propagation
/// folded the register writes into the port arguments and before double precision.
/// <para>Legality, in the effects model:</para>
/// <list type="bullet">
/// <item>DO-1: the combined statement replaces the sequence's LAST access and keeps its origin, so
/// the I/O token advances by the consumed accesses only and the instruction the sequence ends on
/// keeps its boundary. The earlier accesses' statements are removed; their instruction-start
/// markers and comments stay.</item>
/// <item>DO-2: nothing between two accesses of a sequence may reach the device or change the
/// machine in a way the merge could hide: no other port access of any device, no memory effect, no
/// call, no interrupt, no event check (<see cref="IrEffects.DeliversInterrupts"/>), no fault
/// (<see cref="IrEffects.MayFault"/>), no speculative guard or transfer. Only instruction markers,
/// comments and effect-free register or temporary assignments may sit between the accesses.</item>
/// <item>DO-3: a value a consumed access moves to the last access must still read the same: every
/// register it reads keeps its SSA version from its own access to the last
/// (<see cref="IrSsa.SameVersions"/>), it reads no memory or I/O token, contains no call and no
/// division (side-effect-free), and no statement in between rewrites a temporary it reads (SSA
/// does not cover temporaries, so their names are checked directly).</item>
/// <item>DO-4: matching stays inside one basic block; a sequence that does not match completely
/// keeps its single-port calls, which remain correct.</item>
/// <item>DO-5: port and control-word constants are evaluated at their final width, so a
/// narrowing cast such as <c>(ushort)(byte)0x3C4</c> evaluates to 0xC4 and matches nothing.</item>
/// <item>DO-6: every access of the sequence - the last included - may neither fault nor deliver
/// an interrupt, and every value a write moves is pure: it writes nothing, reads no memory or I/O
/// token, and holds no call (an ALU call writes flags) or division.</item>
/// <item>DO-7: every consumed port must be registered in the dumped machine's port map to the
/// exact handler type the generated call drives (<see cref="RealTimeClock"/>,
/// <see cref="PitTimer"/>, <see cref="VgaIoPortHandler"/> or <see cref="Opl3Fm"/>): an unmapped or
/// differently handled port never reaches the device the operation writes.</item>
/// </list>
/// </summary>
internal static class IrDeviceOperations {
    private enum AccessKind {
        Write,
        WriteWord,
        Read,
        Other
    }

    /// <summary>One port access statement of a block.</summary>
    /// <param name="Index">The statement index in its block.</param>
    /// <param name="Kind">Byte write, byte read or an I/O access the pass cannot match.</param>
    /// <param name="Port">The constant port, 0 when it is not constant.</param>
    /// <param name="Value">The write's value argument; the call itself for other kinds (never
    /// read by the matchers).</param>
    /// <param name="Call">The port call node the statement holds.</param>
    private sealed record Access(int Index, AccessKind Kind, ushort Port, IVisitableAstNode Value,
        IVisitableAstNode Call);

    /// <summary>A matched sequence: the statements to remove and the replacement for the last.</summary>
    private sealed record Rewrite(int LastIndex, IVisitableAstNode Node, List<int> RemovedIndices);

    /// <summary>Combines the recognized port sequences of <paramref name="method"/>'s blocks;
    /// <paramref name="ioPorts"/> is the dumped machine's port-to-handler-type map and a sequence
    /// only combines when every port it touches is owned by the device the operation calls (DO-7).
    /// Returns the number of combined sequences. A nonzero count means the caller must rebuild the
    /// SSA and its webs, as every rewriting pass does.</summary>
    public static int Apply(IrMethod method, IrSsa ssa, IReadOnlyDictionary<ushort, Type> ioPorts) {
        int combined = 0;
        foreach (IrBlock block in method.Blocks) {
            combined += ApplyBlock(block, ssa, ioPorts);
        }
        return combined;
    }

    private static int ApplyBlock(IrBlock block, IrSsa ssa,
        IReadOnlyDictionary<ushort, Type> ioPorts) {
        List<Access> accesses = AccessesOf(block);
        if (accesses.Count == 0) {
            return 0;
        }
        List<Rewrite> rewrites = [];
        int m = 0;
        while (m < accesses.Count) {
            if (TryMatch(block, accesses, m, ssa, ioPorts, out Rewrite? rewrite)) {
                rewrites.Add(rewrite);
                m += rewrite.RemovedIndices.Count + 1;
            } else {
                m++;
            }
        }
        foreach (Rewrite rewrite in rewrites) {
            IrStatement old = block.Statements[rewrite.LastIndex];
            IrStatement replacement = IrStatement.Create(rewrite.Node, old.Origin);
            block.Statements[rewrite.LastIndex] = replacement;
            if (ssa.HasSite(old)) {
                ssa.Replace(old, replacement);
            }
        }
        foreach (int index in rewrites.SelectMany(rewrite => rewrite.RemovedIndices)
                .OrderByDescending(index => index)) {
            ssa.Remove(block.Statements[index]);
            block.Statements.RemoveAt(index);
        }
        return rewrites.Count;
    }

    /// <summary>The port access statements of <paramref name="block"/>, in order. Any statement
    /// touching the I/O token is an access; only constant-port byte accesses are matchable.</summary>
    private static List<Access> AccessesOf(IrBlock block) {
        List<Access> accesses = [];
        for (int i = 0; i < block.Statements.Count; i++) {
            IrStatement statement = block.Statements[i];
            if (statement.Node is PortWriteCallNode write) {
                if (ConstantPort(write.Arguments[0]) is { } port) {
                    AccessKind kind = write.Width switch {
                        BitWidth.BYTE_8 => AccessKind.Write,
                        BitWidth.WORD_16 => AccessKind.WriteWord,
                        _ => AccessKind.Other
                    };
                    accesses.Add(new Access(i, kind, port, write.Arguments[1], write));
                } else {
                    accesses.Add(new Access(i, AccessKind.Other, 0, write, write));
                }
            } else if (PortReadOf(statement.Node) is { } read
                && read.Width == BitWidth.BYTE_8
                && ConstantPort(read.CallNode.Arguments[0]) is { } readPort) {
                accesses.Add(new Access(i, AccessKind.Read, readPort, read, read));
            } else if (((statement.Effects.TokenReads | statement.Effects.TokenWrites)
                    & IrTokens.Io) != IrTokens.None) {
                accesses.Add(new Access(i, AccessKind.Other, 0, statement.Node, statement.Node));
            }
        }
        return accesses;
    }

    /// <summary>The constant <paramref name="node"/> evaluates to, honoring every conversion's
    /// target width: a narrowing cast changes the value, so only a full numeric evaluation is a
    /// constant port.</summary>
    private static bool TryConstant(IVisitableAstNode node, out ulong value) =>
        ConstantEvaluation.TryEvaluateReplacing(node, _ => null, out value);

    private static ushort? ConstantPort(IVisitableAstNode node) =>
        TryConstant(node, out ulong value) ? (ushort)value : null;

    /// <summary><paramref name="node"/> as a constant of <paramref name="type"/> when it evaluates
    /// to one, the node itself otherwise: generated arguments stay folded instead of printing a
    /// forest of casts and shifts.</summary>
    private static IVisitableAstNode Folded(IVisitableAstNode node, DataType type) =>
        TryConstant(node, out ulong value) ? new ConstantNode(type, value) : node;

    /// <summary>The <see cref="PortReadCallNode"/> <paramref name="node"/> holds when its whole
    /// value is the read, possibly inside conversions: an assignment or temporary declaration whose
    /// right side unwraps to the read and nothing else.</summary>
    private static PortReadCallNode? PortReadOf(IVisitableAstNode node) => node switch {
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Right: { } right }
            => UnwrapRead(right),
        VariableDeclarationNode { Initializer: { } initializer } => UnwrapRead(initializer),
        _ => null
    };

    private static PortReadCallNode? UnwrapRead(IVisitableAstNode node) => node switch {
        PortReadCallNode read => read,
        TypeConversionNode conversion => UnwrapRead(conversion.Value),
        ExplicitCastNode cast => UnwrapRead(cast.Value),
        _ => null
    };

    /// <summary>Whether every statement strictly between <paramref name="from"/> and
    /// <paramref name="to"/> can sit inside a merged sequence (DO-2): no I/O of any device, no
    /// memory effect, no call, no interrupt delivery, no fault, no speculative guard.</summary>
    private static bool BetweenIsCrossable(IrBlock block, int from, int to) {
        for (int i = from + 1; i < to; i++) {
            IrStatement statement = block.Statements[i];
            if ((statement.Effects.TokenReads | statement.Effects.TokenWrites) != IrTokens.None
                || statement.Effects.MayFault
                || statement.Effects.DeliversInterrupts
                || ContainsImpureCall(statement.Node)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>A helper call that is not a pure ALU computation: an <see cref="AluCallNode"/> or
    /// <see cref="AluCallValueNode"/> inside an ALU node is a flagged register operation and may be
    /// crossed; any other call (bound check, stack, port, typed service, opaque) may not.</summary>
    private static bool ContainsImpureCall(IVisitableAstNode node) =>
        IrAstChildren.Descendants(node).Any(descendant =>
            descendant is MethodCallNode and not AluCallNode
            || descendant is MethodCallValueNode and not AluCallValueNode);

    /// <summary>Whether <paramref name="expression"/> is safe to evaluate at another access than
    /// its own (DO-6): it writes nothing, reads no memory or I/O token, cannot fault or deliver an
    /// interrupt, and holds no call - an ALU call changes flags, so it cannot move either - and no
    /// possibly faulting division.</summary>
    private static bool IsMovableValue(IVisitableAstNode expression) {
        IrEffects effects = IrEffectsVisitor.Of(expression, false);
        return effects.MustWrite == CpuValues.None && effects.MayWrite == CpuValues.None
            && effects.TokenReads == IrTokens.None && effects.TokenWrites == IrTokens.None
            && !effects.MayFault && !effects.DeliversInterrupts
            && !IrAstChildren.Descendants(expression).Any(node =>
                node is MethodCallNode or MethodCallValueNode
                || node is BinaryOperationNode {
                    BinaryOperation: BinaryOperation.DIVIDE or BinaryOperation.MODULO });
    }

    /// <summary>Whether <paramref name="expression"/>'s register reads keep their version from
    /// before statement <paramref name="from"/> to before statement <paramref name="to"/>, and no
    /// statement in between writes a temporary it reads.</summary>
    private static bool ValueStays(IrBlock block, int from, int to, IVisitableAstNode expression,
        IrSsa ssa) {
        IrEffects reads = IrEffectsVisitor.Of(expression, false);
        if (!ssa.SameVersions(reads.Reads, IrTokens.None, (block, from), (block, to))) {
            return false;
        }
        HashSet<string> readLocals = [.. IrAstChildren.Descendants(expression)
            .OfType<VariableReferenceNode>().Select(reference => reference.VariableName)];
        if (readLocals.Count == 0) {
            return true;
        }
        for (int i = from + 1; i < to; i++) {
            foreach (string written in WrittenLocals(block.Statements[i].Node)) {
                if (readLocals.Contains(written)) {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>The temporary names <paramref name="node"/> assigns: a declaration's name and a
    /// variable assignment's target.</summary>
    private static IEnumerable<string> WrittenLocals(IVisitableAstNode node) =>
        IrAstChildren.Descendants(node).SelectMany(descendant => descendant switch {
            VariableDeclarationNode declaration => [declaration.VariableName],
            BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Left: VariableReferenceNode reference } => [reference.VariableName],
            _ => Enumerable.Empty<string>()
        });

    /// <summary>Every legality check of a candidate sequence over access positions
    /// <paramref name="m"/> to <paramref name="last"/> of <paramref name="accesses"/>: crossable
    /// statements between consecutive accesses, no access that can fault or deliver an interrupt,
    /// a pure value on every write, and moved values that still read the same at the last access.</summary>
    private static bool IsLegal(IrBlock block, List<Access> accesses, int m, int last,
        IrSsa ssa) {
        for (int i = m; i < last; i++) {
            if (!BetweenIsCrossable(block, accesses[i].Index, accesses[i + 1].Index)) {
                return false;
            }
        }
        int lastIndex = accesses[last].Index;
        for (int i = m; i <= last; i++) {
            Access access = accesses[i];
            IrEffects effects = block.Statements[access.Index].Effects;
            if (effects.MayFault || effects.DeliversInterrupts) {
                return false;
            }
            if (access.Kind is not (AccessKind.Write or AccessKind.WriteWord)) {
                continue;
            }
            if (!IsMovableValue(access.Value)
                || (i < last && !ValueStays(block, access.Index, lastIndex, access.Value, ssa))) {
                return false;
            }
        }
        return true;
    }

    /// <summary>Whether <paramref name="access"/>'s port is registered in the dumped machine's port
    /// map to exactly <paramref name="type"/> (DO-7): a subclass could override the port
    /// semantics, so only the concrete handler type qualifies.</summary>
    private static bool OwnedBy(Access access, Type type,
        IReadOnlyDictionary<ushort, Type> ioPorts) =>
        ioPorts.TryGetValue(access.Port, out Type? handler) && handler == type;

    /// <summary>Tries to match a sequence starting at access position <paramref name="m"/>.
    /// On success <paramref name="rewrite"/> holds the replacement for the last access's statement
    /// and the statement indices to remove.</summary>
    private static bool TryMatch(IrBlock block, List<Access> accesses, int m, IrSsa ssa,
        IReadOnlyDictionary<ushort, Type> ioPorts,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Rewrite? rewrite) {
        rewrite = null;
        Access first = accesses[m];
        if (first.Kind == AccessKind.WriteWord) {
            // A word write at an index port packs index and data in one instruction and becomes the
            // register write on its own (DO-1: the single access keeps its origin). WriteWord
            // routes the second byte inside the handler, so only the start port is matched.
            switch (first.Port) {
                case Ports.SequencerAddress:
                    rewrite = IndexedWordWrite(block, first, "WriteSequencerRegister", ioPorts);
                    return rewrite is not null;
                case Ports.GraphicsControllerAddress:
                    rewrite = IndexedWordWrite(block, first, "WriteGraphicsControllerRegister", ioPorts);
                    return rewrite is not null;
            }
            return false;
        }
        if (first.Kind == AccessKind.Write) {
            switch (first.Port) {
                case CmosPorts.Address:
                    return TryCmos(block, accesses, m, ssa, ioPorts, out rewrite);
                case PitTimer.PitControlPort:
                    return TryPit(block, accesses, m, ssa, ioPorts, out rewrite);
                case Ports.DacAddressWriteIndex:
                    return TryPalette(block, accesses, m, ssa, ioPorts, out rewrite);
                case Ports.SequencerAddress:
                    return TryIndexedWrite(block, accesses, m, ssa, (ushort)Ports.SequencerData,
                        "WriteSequencerRegister", ioPorts, out rewrite);
                case Ports.GraphicsControllerAddress:
                    return TryIndexedWrite(block, accesses, m, ssa, (ushort)Ports.GraphicsControllerData,
                        "WriteGraphicsControllerRegister", ioPorts, out rewrite);
                case { } when OplOwned(first, ioPorts):
                    return TryOpl(block, accesses, m, ssa, ioPorts, out rewrite);
            }
        }
        return false;
    }

    /// <summary>Whether <paramref name="access"/> writes to an OPL address port whose data port
    /// (the next port) is registered to <see cref="Opl3Fm"/> too: the chip treats even ports as the
    /// address and odd as data, so only an even start can be the pair the sequence writes through.
    /// </summary>
    private static bool OplOwned(Access access, IReadOnlyDictionary<ushort, Type> ioPorts) =>
        (access.Port & 1) == 0
        && ioPorts.TryGetValue(access.Port, out Type? addressHandler) && addressHandler == typeof(Opl3Fm)
        && ioPorts.TryGetValue((ushort)(access.Port + 1), out Type? dataHandler)
        && dataHandler == typeof(Opl3Fm);

    /// <summary>The <paramref name="n"/>-th access after <paramref name="m"/> when it is a byte
    /// write to <paramref name="port"/>; <see langword="null"/> otherwise.</summary>
    private static Access? WriteAt(List<Access> accesses, int m, int n, ushort port) =>
        m + n < accesses.Count && accesses[m + n] is { Kind: AccessKind.Write } access
        && access.Port == port && access.Value is ValueNode
            ? access
            : null;

    private static IVisitableAstNode Call(string property, string method,
        params IVisitableAstNode[] arguments) =>
        new DeviceOperationCallNode(property, method, arguments);

    /// <summary>The statement node <paramref name="accesses"/> at <paramref name="m"/> ends on:
    /// the last write is replaced by the device call; a last read keeps its assignment and only its
    /// port call becomes the device operation value.</summary>
    private static IVisitableAstNode ReplacementNode(IrBlock block, Access last,
        IVisitableAstNode operation) {
        if (last.Kind == AccessKind.Write) {
            return operation;
        }
        DeviceOperationValueNode value = (DeviceOperationValueNode)operation;
        IrStatement statement = block.Statements[last.Index];
        return IrAstChildren.MapChildren(statement.Node,
            child => ReplaceRead(child, (PortReadCallNode)last.Call, value));
    }

    private static IVisitableAstNode ReplaceRead(IVisitableAstNode node, PortReadCallNode read,
        DeviceOperationValueNode replacement) =>
        ReferenceEquals(node, read) ? replacement
            : IrAstChildren.MapChildren(node, child => ReplaceRead(child, read, replacement));

    private static List<int> Consumed(List<Access> accesses, int m, int last) =>
        [.. Enumerable.Range(m, last - m).Select(i => accesses[i].Index)];

    /// <summary><c>out 0x70, address; in al, 0x71</c> or <c>out 0x70, address; out 0x71, value</c>
    /// on ports the dumped machine mapped to <see cref="RealTimeClock"/>.</summary>
    private static bool TryCmos(IrBlock block, List<Access> accesses, int m, IrSsa ssa,
        IReadOnlyDictionary<ushort, Type> ioPorts,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Rewrite? rewrite) {
        rewrite = null;
        if (m + 1 >= accesses.Count || accesses[m + 1] is not { Port: CmosPorts.Data } second
            || !OwnedBy(accesses[m], typeof(RealTimeClock), ioPorts)
            || !OwnedBy(second, typeof(RealTimeClock), ioPorts)
            || !IsLegal(block, accesses, m, m + 1, ssa)) {
            return false;
        }
        IVisitableAstNode address = accesses[m].Value;
        if (second is { Kind: AccessKind.Read }) {
            DeviceOperationValueNode read = new(DataType.UINT8, "Cmos", "ReadRegister", address);
            rewrite = new Rewrite(second.Index,
                ReplacementNode(block, second, read), Consumed(accesses, m, m + 1));
            return true;
        }
        if (second is { Kind: AccessKind.Write, Value: { } value }) {
            rewrite = new Rewrite(second.Index,
                Call("Cmos", "WriteRegister", address, value), Consumed(accesses, m, m + 1));
            return true;
        }
        return false;
    }

    /// <summary><c>out 0x43, controlWord; out 0x40+channel, low; out 0x40+channel, high</c> for a
    /// channel 0 or 2 control word with low/high access, on ports the dumped machine mapped to
    /// <see cref="PitTimer"/>.</summary>
    private static bool TryPit(IrBlock block, List<Access> accesses, int m, IrSsa ssa,
        IReadOnlyDictionary<ushort, Type> ioPorts,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Rewrite? rewrite) {
        rewrite = null;
        if (!TryConstant(accesses[m].Value, out ulong controlRaw)) {
            return false;
        }
        byte controlWord = (byte)controlRaw;
        int channel = controlWord >> 6;
        if (channel is not (0 or 2) || ((controlWord >> 4) & 0x03) != 0x03) {
            return false;
        }
        ushort channelPort = (ushort)(PitTimer.PitChannel0Port + channel);
        if (WriteAt(accesses, m, 1, channelPort) is not { } low
            || WriteAt(accesses, m, 2, channelPort) is not { } high
            || !OwnedBy(accesses[m], typeof(PitTimer), ioPorts)
            || !OwnedBy(low, typeof(PitTimer), ioPorts) || !OwnedBy(high, typeof(PitTimer), ioPorts)
            || !IsLegal(block, accesses, m, m + 2, ssa)) {
            return false;
        }
        IVisitableAstNode divisor = Folded(new BinaryOperationNode(DataType.UINT16,
            new TypeConversionNode(DataType.UINT16, (ValueNode)low.Value),
            BinaryOperation.BITWISE_OR,
            new BinaryOperationNode(DataType.UINT16,
                new TypeConversionNode(DataType.UINT16, (ValueNode)high.Value),
                BinaryOperation.LEFT_SHIFT, new ConstantNode(DataType.UINT16, 8))), DataType.UINT16);
        rewrite = new Rewrite(high.Index,
            Call("Pit", "ProgramChannel", new ConstantNode(DataType.INT32, (ulong)channel),
                new ConstantNode(DataType.UINT8, controlWord), divisor),
            Consumed(accesses, m, m + 2));
        return true;
    }

    /// <summary><c>out 0x3C8, index; out 0x3C9, r; out 0x3C9, g; out 0x3C9, b</c> on ports the
    /// dumped machine mapped to <see cref="VgaIoPortHandler"/>.</summary>
    private static bool TryPalette(IrBlock block, List<Access> accesses, int m, IrSsa ssa,
        IReadOnlyDictionary<ushort, Type> ioPorts,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Rewrite? rewrite) {
        rewrite = null;
        if (WriteAt(accesses, m, 1, (ushort)Ports.DacData) is not { } red
            || WriteAt(accesses, m, 2, (ushort)Ports.DacData) is not { } green
            || WriteAt(accesses, m, 3, (ushort)Ports.DacData) is not { } blue
            || !OwnedBy(accesses[m], typeof(VgaIoPortHandler), ioPorts)
            || !OwnedBy(red, typeof(VgaIoPortHandler), ioPorts)
            || !OwnedBy(green, typeof(VgaIoPortHandler), ioPorts)
            || !OwnedBy(blue, typeof(VgaIoPortHandler), ioPorts)
            || !IsLegal(block, accesses, m, m + 3, ssa)) {
            return false;
        }
        rewrite = new Rewrite(blue.Index,
            Call("Vga", "SetPaletteEntry", accesses[m].Value, red.Value, green.Value, blue.Value),
            Consumed(accesses, m, m + 3));
        return true;
    }

    /// <summary>One word write at an index port: the low byte is the index, the high byte the data,
    /// so the single instruction becomes the register write. The instruction's value is evaluated
    /// twice by the operation, so it must be pure and unfaulting like any moved argument.</summary>
    private static Rewrite? IndexedWordWrite(IrBlock block, Access first, string method,
        IReadOnlyDictionary<ushort, Type> ioPorts) {
        if (!OwnedBy(first, typeof(VgaIoPortHandler), ioPorts)
            || block.Statements[first.Index].Effects.MayFault
            || block.Statements[first.Index].Effects.DeliversInterrupts
            || first.Value is not ValueNode wordValue
            || !IsMovableValue(wordValue)) {
            return null;
        }
        IVisitableAstNode low = Folded(
            new TypeConversionNode(DataType.UINT8, wordValue), DataType.UINT8);
        IVisitableAstNode high = Folded(new TypeConversionNode(DataType.UINT8,
            new BinaryOperationNode(DataType.UINT16, wordValue,
                BinaryOperation.RIGHT_SHIFT, new ConstantNode(DataType.UINT16, 8))), DataType.UINT8);
        return new Rewrite(first.Index, Call("Vga", method, low, high), []);
    }

    /// <summary>A sequencer or graphics controller byte pair index/data on ports the dumped
    /// machine mapped to <see cref="VgaIoPortHandler"/>.</summary>
    private static bool TryIndexedWrite(IrBlock block, List<Access> accesses, int m, IrSsa ssa,
        ushort dataPort, string method, IReadOnlyDictionary<ushort, Type> ioPorts,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Rewrite? rewrite) {
        rewrite = null;
        Access first = accesses[m];
        if (WriteAt(accesses, m, 1, dataPort) is { } data
            && OwnedBy(first, typeof(VgaIoPortHandler), ioPorts)
            && OwnedBy(data, typeof(VgaIoPortHandler), ioPorts)
            && IsLegal(block, accesses, m, m + 1, ssa)) {
            rewrite = new Rewrite(data.Index,
                Call("Vga", method, first.Value, data.Value), Consumed(accesses, m, m + 1));
            return true;
        }
        return false;
    }

    /// <summary><c>out addressPort, register; out addressPort + 1, value</c> on two adjacent ports
    /// the dumped machine mapped to <see cref="Opl3Fm"/>; the port stays an argument because the
    /// aliases and banks differ per mode and configuration.</summary>
    private static bool TryOpl(IrBlock block, List<Access> accesses, int m, IrSsa ssa,
        IReadOnlyDictionary<ushort, Type> ioPorts,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Rewrite? rewrite) {
        rewrite = null;
        ushort addressPort = accesses[m].Port;
        if (WriteAt(accesses, m, 1, (ushort)(addressPort + 1)) is not { } data
            || !IsLegal(block, accesses, m, m + 1, ssa)) {
            return false;
        }
        rewrite = new Rewrite(data.Index,
            Call("Opl", "WriteRegister", new ConstantNode(DataType.UINT16, addressPort),
                accesses[m].Value, data.Value),
            Consumed(accesses, m, m + 1));
        return true;
    }
}
