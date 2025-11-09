namespace Spice86.Core.Emulator.VM.Breakpoint;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.Memory;

/// <summary>
/// Evaluates CfgCpu AST expressions in the context of breakpoint conditions.
/// Implements the visitor pattern to traverse and evaluate expression trees at runtime.
/// </summary>
public class ExpressionEvaluator : IAstVisitor<long> {
    private readonly State _state;
    private readonly IMemory _memory;
    private readonly long _triggerAddress;

    /// <summary>
    /// Initializes a new instance of the ExpressionEvaluator class.
    /// </summary>
    /// <param name="state">CPU state for accessing registers.</param>
    /// <param name="memory">Memory interface for reading memory values.</param>
    /// <param name="triggerAddress">The address that triggered the breakpoint.</param>
    public ExpressionEvaluator(State state, IMemory memory, long triggerAddress) {
        _state = state;
        _memory = memory;
        _triggerAddress = triggerAddress;
    }

    public long VisitBinaryOperationNode(BinaryOperationNode node) {
        long left = node.Left.Accept(this);
        long right = node.Right.Accept(this);

        return node.BinaryOperation switch {
            BinaryOperation.PLUS => left + right,
            BinaryOperation.MINUS => left - right,
            BinaryOperation.MULTIPLY => left * right,
            BinaryOperation.DIVIDE => right != 0 ? left / right : 0,
            BinaryOperation.MODULO => right != 0 ? left % right : 0,
            BinaryOperation.EQUAL => left == right ? 1 : 0,
            BinaryOperation.NOT_EQUAL => left != right ? 1 : 0,
            BinaryOperation.LESS_THAN => left < right ? 1 : 0,
            BinaryOperation.GREATER_THAN => left > right ? 1 : 0,
            BinaryOperation.LESS_THAN_OR_EQUAL => left <= right ? 1 : 0,
            BinaryOperation.GREATER_THAN_OR_EQUAL => left >= right ? 1 : 0,
            BinaryOperation.LOGICAL_AND => (left != 0 && right != 0) ? 1 : 0,
            BinaryOperation.LOGICAL_OR => (left != 0 || right != 0) ? 1 : 0,
            BinaryOperation.BITWISE_AND => left & right,
            BinaryOperation.BITWISE_OR => left | right,
            BinaryOperation.BITWISE_XOR => left ^ right,
            BinaryOperation.LEFT_SHIFT => left << (int)right,
            BinaryOperation.RIGHT_SHIFT => left >> (int)right,
            BinaryOperation.ASSIGN => throw new NotSupportedException("Assignment not supported in breakpoint conditions"),
            _ => throw new ArgumentException($"Unsupported binary operation: {node.BinaryOperation}")
        };
    }

    public long VisitUnaryOperationNode(UnaryOperationNode node) {
        long value = node.Value.Accept(this);

        return node.UnaryOperation switch {
            UnaryOperation.NOT => value == 0 ? 1 : 0,
            UnaryOperation.NEGATE => -value,
            UnaryOperation.BITWISE_NOT => ~value,
            _ => throw new ArgumentException($"Unsupported unary operation: {node.UnaryOperation}")
        };
    }

    public long VisitConstantNode(ConstantNode node) {
        return node.Value;
    }

    public long VisitRegisterNode(RegisterNode node) {
        return node.RegisterIndex switch {
            0 => _state.AX,
            1 => _state.CX,
            2 => _state.DX,
            3 => _state.BX,
            4 => _state.SP,
            5 => _state.BP,
            6 => _state.SI,
            7 => _state.DI,
            _ => 0
        };
    }

    public long VisitSegmentRegisterNode(SegmentRegisterNode node) {
        return node.RegisterIndex switch {
            0 => _state.ES,
            1 => _state.CS,
            2 => _state.SS,
            3 => _state.DS,
            4 => _state.FS,
            5 => _state.GS,
            _ => 0
        };
    }

    public long VisitAbsolutePointerNode(AbsolutePointerNode node) {
        long address = node.AbsoluteAddress.Accept(this);
        // Use SneakilyRead to avoid triggering breakpoints during evaluation
        return node.DataType.BitWidth switch {
            Shared.Emulator.Memory.BitWidth.BYTE_8 => _memory.SneakilyRead((uint)address),
            Shared.Emulator.Memory.BitWidth.WORD_16 => ReadWord(address),
            Shared.Emulator.Memory.BitWidth.DWORD_32 => ReadDWord(address),
            _ => 0
        };
    }

    private long ReadWord(long address) {
        byte low = _memory.SneakilyRead((uint)address);
        byte high = _memory.SneakilyRead((uint)address + 1);
        return (ushort)((high << 8) | low);
    }

    private long ReadDWord(long address) {
        byte b0 = _memory.SneakilyRead((uint)address);
        byte b1 = _memory.SneakilyRead((uint)address + 1);
        byte b2 = _memory.SneakilyRead((uint)address + 2);
        byte b3 = _memory.SneakilyRead((uint)address + 3);
        return (uint)((b3 << 24) | (b2 << 16) | (b1 << 8) | b0);
    }

    public long VisitSegmentedPointer(SegmentedPointerNode node) {
        long segment = node.Segment.Accept(this);
        long offset = node.Offset.Accept(this);
        long address = (segment << 4) + offset;
        return node.DataType.BitWidth switch {
            Shared.Emulator.Memory.BitWidth.BYTE_8 => _memory.SneakilyRead((uint)address),
            Shared.Emulator.Memory.BitWidth.WORD_16 => ReadWord(address),
            Shared.Emulator.Memory.BitWidth.DWORD_32 => ReadDWord(address),
            _ => 0
        };
    }

    public long VisitSegmentedAddressConstantNode(SegmentedAddressConstantNode node) {
        return (long)node.Value.Linear;
    }

    public long VisitInstructionNode(InstructionNode node) {
        throw new NotSupportedException("Instruction nodes not supported in breakpoint conditions");
    }
}
