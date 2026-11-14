namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Writes a <c>Stack</c> helper call as the plain nodes the helper performs, in its exact order:
/// offset temporaries computed from SP or BP (16-bit, never from ESP or EBP), the memory access through
/// a <see cref="SegmentedPointerNode"/> on SS and the register commits. The expansion is the semantic
/// model only: the effects visitor reads it and the opt-in JIT path compiles it; generated code keeps
/// printing the helpers. PushAll's "write everything, then throw the pending fault" stays on the helper
/// call at run time because a leading check node cannot defer its throw past the stores.
/// </summary>
internal static class StackExpansionBuilder {
    private static readonly SegmentRegisterNode Ss = new((int)SegmentRegisterIndex.SsIndex);
    private static readonly RegisterNode Sp = new(DataType.UINT16, (int)RegisterIndex.SpIndex);
    private static readonly RegisterNode Bp = new(DataType.UINT16, (int)RegisterIndex.BpIndex);
    private static readonly RegisterNode Ebp = new(DataType.UINT32, (int)RegisterIndex.BpIndex);
    private static readonly RegisterNode Esp = new(DataType.UINT32, (int)RegisterIndex.SpIndex);

    /// <summary>The expansion of a void stack call (push, push all, pop all, leave).</summary>
    public static StackExpansion Expand(StackCallNode call) {
        DataType type = DataType.UnsignedFromBitWidth(call.Width);
        int size = (int)call.Width / 8;
        return call.Operation switch {
            StackOperation.Push when call.Arguments is [ValueNode value] => ExpandPush(type, size, value),
            StackOperation.Pop => ExpandPop(type, size) with { Result = null },
            StackOperation.PushAll => ExpandPushAll(type, size, call.Arguments),
            StackOperation.PopAll => ExpandPopAll(type, size),
            StackOperation.Leave => ExpandLeave(type, size),
            _ => throw new InvalidOperationException(
                $"No expansion for {call.Operation} {call.Width} as a statement call.")
        };
    }

    /// <summary>The expansion of a value stack call (pop, peek).</summary>
    public static StackExpansion Expand(StackCallValueNode call) {
        DataType type = DataType.UnsignedFromBitWidth(call.Width);
        int size = (int)call.Width / 8;
        return call.Operation switch {
            StackOperation.Pop => ExpandPop(type, size),
            StackOperation.Peek when call.CallNode.Arguments is [ValueNode index] =>
                ExpandPeek(type, index),
            _ => throw new InvalidOperationException(
                $"No expansion for {call.Operation} {call.Width} as a value call.")
        };
    }

    /// <summary>Push: the pushed value is bound first, then newSp = SP - size, the store, then SP.</summary>
    private static StackExpansion ExpandPush(DataType type, int size, ValueNode value) {
        VariableDeclarationNode pushed = new(type, "pushed", value);
        VariableDeclarationNode newSp = new(DataType.UINT16, "newSp", Minus(Sp, size));
        List<IVisitableAstNode> statements = [
            newSp,
            Assign(Slot(type, newSp.Reference), pushed.Reference),
            Assign(Sp, newSp.Reference)
        ];
        return new StackExpansion([pushed], statements, null);
    }

    /// <summary>Pop: read SS:SP into the result, then SP = SP + size.</summary>
    private static StackExpansion ExpandPop(DataType type, int size) {
        VariableDeclarationNode popped = new(type, "popped", Slot(type, Sp));
        List<IVisitableAstNode> statements = [
            popped,
            Assign(Sp, Plus(Sp, size))
        ];
        return new StackExpansion([], statements, popped);
    }

    /// <summary>Peek: read SS:(ushort)(SP + index); nothing is written.</summary>
    private static StackExpansion ExpandPeek(DataType type, ValueNode index) {
        VariableDeclarationNode offset = new(index.DataType, "index", index);
        VariableDeclarationNode peeked = new(type, "peeked",
            Slot(type, Add(Sp, new TypeConversionNode(DataType.UINT16, offset.Reference))));
        return new StackExpansion([offset], [peeked], peeked);
    }

    /// <summary>Leave: read SS:BP (BP in every width), SP = BP + size, then the frame pointer write
    /// (BP for 16-bit, the full EBP for 32-bit).</summary>
    private static StackExpansion ExpandLeave(DataType type, int size) {
        VariableDeclarationNode framePointer = new(type, "framePointer", Slot(type, Bp));
        RegisterNode target = type == DataType.UINT32 ? Ebp : Bp;
        List<IVisitableAstNode> statements = [
            framePointer,
            Assign(Sp, Plus(Bp, size)),
            Assign(target, framePointer.Reference)
        ];
        return new StackExpansion([], statements, null);
    }

    /// <summary>PushAll: the range check, then the eight stores below SP in argument order, then SP =
    /// the last offset. The check records no pending fault here; the run-time path keeps the helper.</summary>
    private static StackExpansion ExpandPushAll(DataType type, int size, IReadOnlyList<IVisitableAstNode> arguments) {
        List<VariableDeclarationNode> operands = [];
        for (int i = 0; i < arguments.Count; i++) {
            if (arguments[i] is not ValueNode value) {
                throw new InvalidOperationException("A PushAll argument must be a value node.");
            }
            operands.Add(new VariableDeclarationNode(value.DataType, $"value{i}", value));
        }
        VariableDeclarationNode offset = new(DataType.UINT16, "offset", Sp);
        List<IVisitableAstNode> statements = [
            new StackRangeCheckNode(StackRangeDirection.Push, Sp, size, operands.Count),
            offset
        ];
        foreach (VariableDeclarationNode operand in operands) {
            statements.Add(Assign(offset.Reference, Minus(offset.Reference, size)));
            statements.Add(Assign(Slot(type, offset.Reference), operand.Reference));
        }
        statements.Add(Assign(Sp, offset.Reference));
        return new StackExpansion(operands, statements, null);
    }

    /// <summary>PopAll: the slots in helper order with a per-register commit, the SP slot of the
    /// 16-bit form as a bare access check, the 32-bit ESP slot kept in a temp for the final full ESP
    /// write (slot high half, new SP low half); SP = offset at the end of the 16-bit form.</summary>
    private static StackExpansion ExpandPopAll(DataType type, int size) {
        VariableDeclarationNode offset = new(DataType.UINT16, "offset", Sp);
        List<IVisitableAstNode> statements = [offset];
        bool wide = type == DataType.UINT32;
        int[] commitOrder = [7, 6, 5, -1, 3, 2, 1, 0];
        VariableDeclarationNode? espSlot = null;
        foreach (int register in commitOrder) {
            if (register < 0) {
                if (wide) {
                    espSlot = new VariableDeclarationNode(DataType.UINT32, "espSlot", Slot(type, offset.Reference));
                    statements.Add(espSlot);
                } else {
                    statements.Add(new StackRangeCheckNode(StackRangeDirection.Pop, offset.Reference, size, 1));
                }
            } else {
                statements.Add(Assign(new RegisterNode(type, register), Slot(type, offset.Reference)));
            }
            statements.Add(Assign(offset.Reference, Plus(offset.Reference, size)));
        }
        if (wide) {
            statements.Add(Assign(Esp, EspWrite(espSlot, offset.Reference)));
        } else {
            statements.Add(Assign(Sp, offset.Reference));
        }
        return new StackExpansion([], statements, null);
    }

    /// <summary>ESP = (slot &amp; 0xFFFF0000) | offset: the full 32-bit write of PopAll32.</summary>
    private static BinaryOperationNode EspWrite(VariableDeclarationNode? espSlot, ValueNode offset) {
        if (espSlot is null) {
            throw new InvalidOperationException("PopAll32 needs its ESP slot temporary.");
        }
        return new BinaryOperationNode(DataType.UINT32,
            new BinaryOperationNode(DataType.UINT32, espSlot.Reference, BinaryOperation.BITWISE_AND,
                new ConstantNode(DataType.UINT32, 0xFFFF0000)),
            BinaryOperation.BITWISE_OR,
            new TypeConversionNode(DataType.UINT32, offset));
    }

    private static SegmentedPointerNode Slot(DataType type, ValueNode offset) =>
        new(type, Ss, null, offset);

    private static BinaryOperationNode Assign(ValueNode to, ValueNode from) =>
        new(to.DataType, to, BinaryOperation.ASSIGN, from);

    private static BinaryOperationNode Minus(ValueNode value, int amount) =>
        new(DataType.UINT16, value, BinaryOperation.MINUS, new ConstantNode(DataType.UINT16, (ulong)amount));

    private static BinaryOperationNode Plus(ValueNode value, int amount) =>
        new(DataType.UINT16, value, BinaryOperation.PLUS, new ConstantNode(DataType.UINT16, (ulong)amount));

    private static BinaryOperationNode Add(ValueNode left, ValueNode right) =>
        new(DataType.UINT16, left, BinaryOperation.PLUS, right);
}
