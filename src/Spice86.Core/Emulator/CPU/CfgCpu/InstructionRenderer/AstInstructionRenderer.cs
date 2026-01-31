namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
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

    public string VisitSegmentedPointer(SegmentedPointerNode node) {
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

    public string VisitCpuFlagNode(CpuFlagNode node) {
        return node.FlagMask switch {
            Flags.Carry => "CF",
            Flags.Parity => "PF",
            Flags.Auxiliary => "AF",
            Flags.Zero => "ZF",
            Flags.Sign => "SF",
            Flags.Trap => "TF",
            Flags.Interrupt => "IF",
            Flags.Direction => "DF",
            Flags.Overflow => "OF",
            _ => throw new InvalidOperationException($"Unknown flag mask: 0x{node.FlagMask:X4}")
        };
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
            _ => throw new InvalidOperationException($"Unsupported bit width {node.DataType.BitWidth}")
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
            _ => throw new InvalidOperationException($"Unsupported bit width {size}")
        };
    }

    public string VisitSegmentedAddressConstantNode(SegmentedAddressConstantNode node) {
        return node.Value.ToString();
    }

    public string VisitBinaryOperationNode(BinaryOperationNode node) {
        string left = RenderOperand(node.Left, node.BinaryOperation, isLeftOperand: true);
        if (IsZero(node.Right) && node.BinaryOperation == BinaryOperation.PLUS) {
            return left;
        }
        string right = RenderOperand(node.Right, node.BinaryOperation, isLeftOperand: false);
        if (IsNegative(node.Right) && node.BinaryOperation == BinaryOperation.PLUS) {
            return left + right;
        }
        return left + OperationToString(node.BinaryOperation) + right;
    }
    
    private string RenderOperand(ValueNode operand, BinaryOperation parentOperation, bool isLeftOperand) {
        string rendered = operand.Accept(this);
        
        // Check if we need to wrap in parentheses for precedence
        if (operand is BinaryOperationNode childBinaryOp) {
            int parentPrecedence = GetPrecedence(parentOperation);
            int childPrecedence = GetPrecedence(childBinaryOp.BinaryOperation);
            
            // Wrap if child has lower precedence, or same precedence on right side (for left-associativity)
            if (childPrecedence < parentPrecedence || 
                (!isLeftOperand && childPrecedence == parentPrecedence)) {
                rendered = "(" + rendered + ")";
            }
        }
        
        return rendered;
    }
    
    private int GetPrecedence(BinaryOperation operation) {
        return operation switch {
            BinaryOperation.ASSIGN => 1,
            BinaryOperation.LOGICAL_OR => 2,
            BinaryOperation.LOGICAL_AND => 3,
            BinaryOperation.BITWISE_OR => 4,
            BinaryOperation.BITWISE_XOR => 5,
            BinaryOperation.BITWISE_AND => 6,
            BinaryOperation.EQUAL => 7,
            BinaryOperation.NOT_EQUAL => 7,
            BinaryOperation.LESS_THAN => 8,
            BinaryOperation.GREATER_THAN => 8,
            BinaryOperation.LESS_THAN_OR_EQUAL => 8,
            BinaryOperation.GREATER_THAN_OR_EQUAL => 8,
            BinaryOperation.LEFT_SHIFT => 9,
            BinaryOperation.RIGHT_SHIFT => 9,
            BinaryOperation.PLUS => 10,
            BinaryOperation.MINUS => 10,
            BinaryOperation.MULTIPLY => 11,
            BinaryOperation.DIVIDE => 11,
            BinaryOperation.MODULO => 11,
            _ => 0
        };
    }
    
    public string VisitUnaryOperationNode(UnaryOperationNode node) {
        string value = node.Value.Accept(this);
        // Wrap binary operations in parentheses to preserve precedence
        if (node.Value is BinaryOperationNode) {
            value = "(" + value + ")";
        }
        return OperationToString(node.UnaryOperation) + value;
    }
    
    public string VisitTypeConversionNode(TypeConversionNode node) {
        string typeStr = node.DataType.BitWidth switch {
            BitWidth.BYTE_8 => node.DataType.Signed ? "(sbyte)" : "(byte)",
            BitWidth.WORD_16 => node.DataType.Signed ? "(short)" : "(ushort)",
            BitWidth.DWORD_32 => node.DataType.Signed ? "(int)" : "(uint)",
            _ => throw new InvalidOperationException($"Unsupported bit width {node.DataType.BitWidth}")
        };
        string value = node.Value.Accept(this);
        return typeStr + value;
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

    private string OperationToString(BinaryOperation binaryOperation) {
        return binaryOperation switch {
            BinaryOperation.PLUS => "+",
            BinaryOperation.MINUS => "-",
            BinaryOperation.MULTIPLY => "*",
            BinaryOperation.DIVIDE => "/",
            BinaryOperation.MODULO => "%",
            BinaryOperation.EQUAL => "==",
            BinaryOperation.NOT_EQUAL => "!=",
            BinaryOperation.LESS_THAN => "<",
            BinaryOperation.GREATER_THAN => ">",
            BinaryOperation.LESS_THAN_OR_EQUAL => "<=",
            BinaryOperation.GREATER_THAN_OR_EQUAL => ">=",
            BinaryOperation.LOGICAL_AND => "&&",
            BinaryOperation.LOGICAL_OR => "||",
            BinaryOperation.BITWISE_AND => "&",
            BinaryOperation.BITWISE_OR => "|",
            BinaryOperation.BITWISE_XOR => "^",
            BinaryOperation.LEFT_SHIFT => "<<",
            BinaryOperation.RIGHT_SHIFT => ">>",
            BinaryOperation.ASSIGN => "=",
            _ => throw new InvalidOperationException($"Unsupported AST operation {binaryOperation}")
        };
    }
    
    private string OperationToString(UnaryOperation unaryOperation) {
        return unaryOperation switch {
            UnaryOperation.NOT => "!",
            UnaryOperation.NEGATE => "-",
            UnaryOperation.BITWISE_NOT => "~",
            _ => throw new InvalidOperationException($"Unsupported AST operation {unaryOperation}")
        };
    }

    private string PointerDataTypeToString(DataType dataType) {
        return dataType.BitWidth switch {
            BitWidth.BYTE_8 => "byte ptr",
            BitWidth.WORD_16 => "word ptr",
            BitWidth.DWORD_32 => "dword ptr",
            _ => throw new InvalidOperationException($"Unsupported bit width {dataType.BitWidth}")
        };
    }

    public string VisitMethodCallNode(MethodCallNode node) {
        throw new NotSupportedException("MethodCallNode should not be rendered as assembly.");
    }

    public string VisitBlockNode(BlockNode node) {
        throw new NotSupportedException("BlockNode should not be rendered as assembly.");
    }

    public string VisitIfElseNode(IfElseNode node) {
        throw new NotSupportedException("IfElseNode should not be rendered as assembly.");
    }

    public string VisitMethodCallValueNode(MethodCallValueNode node) {
        throw new NotSupportedException("MethodCallValueNode should not be rendered as assembly.");
    }

    public string VisitMoveIpNextNode(MoveIpNextNode node) {
        throw new NotSupportedException("MoveIpNextNode should not be rendered as assembly.");
    }

    public string VisitVariableReferenceNode(VariableReferenceNode node) {
        throw new NotSupportedException("VariableReferenceNode should not be rendered as assembly.");
    }

    public string VisitVariableDeclarationNode(VariableDeclarationNode node) {
        throw new NotSupportedException("VariableDeclarationNode should not be rendered as assembly.");
    }

    public string VisitCallNearNode(CallNearNode node) {
        throw new NotSupportedException("CallNearNode should not be rendered as assembly.");
    }

    public string VisitCallFarNode(CallFarNode node) {
        throw new NotSupportedException("CallFarNode should not be rendered as assembly.");
    }

    public string VisitReturnNearNode(ReturnNearNode node) {
        throw new NotSupportedException("ReturnNearNode should not be rendered as assembly.");
    }

    public string VisitReturnFarNode(ReturnFarNode node) {
        throw new NotSupportedException("ReturnFarNode should not be rendered as assembly.");
    }

    public string VisitJumpNearNode(JumpNearNode node) {
        throw new NotSupportedException("JumpNearNode should not be rendered as assembly.");
    }

    public string VisitJumpFarNode(JumpFarNode node) {
        throw new NotSupportedException("JumpFarNode should not be rendered as assembly.");
    }

    public string VisitInterruptCallNode(InterruptCallNode node) {
        throw new NotSupportedException("InterruptCallNode should not be rendered as assembly.");
    }

    public string VisitReturnInterruptNode(ReturnInterruptNode node) {
        throw new NotSupportedException("ReturnInterruptNode should not be rendered as assembly.");
    }
}