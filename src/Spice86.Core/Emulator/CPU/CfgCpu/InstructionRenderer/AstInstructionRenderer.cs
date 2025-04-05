namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Shared.Emulator.Memory;

using System.Globalization;
using System.Linq;

public class AstInstructionRenderer : IAstVisitor<string> {
    private readonly RegisterRenderer _registerRenderer = new();

    public string VisitSegmentRegisterNode(SegmentRegisterNode node) {
        return _registerRenderer.ToStringSegmentRegister(node.RegisterIndex);
    }

    public string VisitSegmentedPointer(SegmentedPointer node) {
        string offset = node.Offset.Accept(this);
        string segment = node.Segment.Accept(this);

        return PointerDataTypeToString(node.DataType) + " " + segment + ":[" + offset + "]";
    }

    public string VisitRegisterNode(RegisterNode node) {
        return _registerRenderer.ToStringRegister(node.DataType.BitWidth, node.RegisterIndex);
    }

    public string VisitAbsolutePointerNode(AbsolutePointerNode node) {
        string absoluteAddress = node.AbsoluteAddress.Accept(this);
        return PointerDataTypeToString(node.DataType) + " [" + absoluteAddress + "]";
    }

    public string VisitConstantNode(ConstantNode node) {
        if (IsNegative(node)) {
            int valueSigned = SignExtend(node.Value, node.DataType.BitWidth);
            return valueSigned.ToString(CultureInfo.InvariantCulture);
        }
        uint value = node.Value;
        if (value < 10) {
            // render it as decimal as it is the same and it will save the 0x0
            return value.ToString(CultureInfo.InvariantCulture);
        }

        return node.DataType.BitWidth switch {
            BitWidth.BYTE_8 => $"0x{value:X2}",
            BitWidth.WORD_16 => $"0x{value:X4}",
            BitWidth.DWORD_32 => $"0x{value:X8}",
        };
    }

    private bool IsNegative(ConstantNode node) {
        if (node.DataType.Signed) {
            int value = SignExtend(node.Value, node.DataType.BitWidth);
            if (value < 0) {
                return true;
            }
        }
        return false;
    }

    private int SignExtend(uint value, BitWidth size) {
        return size switch {
            BitWidth.BYTE_8 => (sbyte)value,
            BitWidth.WORD_16 => (short)value,
            BitWidth.DWORD_32 => (int)value,
        };
    }

    public string VisitSegmentedAddressConstantNode(SegmentedAddressConstantNode node) {
        return node.Value.ToString();
    }

    public string VisitBinaryOperationNode(BinaryOperationNode node) {
        string left = node.Left.Accept(this);
        if (IsZero(node.Right) && node.Operation == Operation.PLUS) {
            return left;
        }
        string right = node.Right.Accept(this);
        if (IsNegative(node.Right) && node.Operation == Operation.PLUS) {
            return left + right;
        }
        return left + OperationToString(node.Operation) + right;
    }

    private bool IsZero(ValueNode valueNode) {
        return valueNode is ConstantNode constantNode && constantNode.Value == 0;
    }

    private bool IsNegative(ValueNode valueNode) {
        return valueNode is ConstantNode constantNode && IsNegative(constantNode);
    }

    public string VisitInstructionNode(InstructionNode node) {
        RepPrefix? repPrefix = node.RepPrefix;
        string prefix = "";
        if (repPrefix != null) {
            prefix = Enum.GetName(repPrefix.Value)?.ToLower() + " ";
        }
        string mnemonic = prefix + Enum.GetName(node.Operation)?.ToLower().Replace("_", " ");
        if (node.Parameters.Count == 0) {
            return mnemonic;
        }

        return mnemonic + " " + string.Join(",", node.Parameters.Select(param => param.Accept(this)));
    }

    private string OperationToString(Operation operation) {
        return operation switch {
            Operation.PLUS => "+",
            Operation.MULTIPLY => "*"
        };
    }

    private string PointerDataTypeToString(DataType dataType) {
        return dataType.BitWidth switch {
            BitWidth.BYTE_8 => "byte ptr",
            BitWidth.WORD_16 => "word ptr",
            BitWidth.DWORD_32 => "dword ptr"
        };
    }
}