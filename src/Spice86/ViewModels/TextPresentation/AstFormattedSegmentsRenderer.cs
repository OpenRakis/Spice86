namespace Spice86.ViewModels.TextPresentation;

using Iced.Intel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Shared.Emulator.Memory;

using System.Globalization;

/// <summary>
/// AST visitor that produces <see cref="FormattedTextSegment"/> lists with
/// <see cref="FormatterTextKind"/> annotations matching the disassembly view,
/// enabling reuse of the same theme-aware syntax highlighting colors.
/// </summary>
public class AstFormattedSegmentsRenderer : IAstVisitor<List<FormattedTextSegment>> {
    private readonly AsmRenderingConfig _config;
    private readonly RegisterRenderer _registerRenderer;
    private readonly MnemonicRenderer _mnemonicRenderer;

    public AstFormattedSegmentsRenderer(AsmRenderingConfig config) {
        _config = config;
        _registerRenderer = new RegisterRenderer(config);
        _mnemonicRenderer = new MnemonicRenderer(config);
    }

    private static List<FormattedTextSegment> Seg(string text, FormatterTextKind kind) =>
        [new() { Text = text, Kind = kind }];

    private static List<FormattedTextSegment> Concat(params List<FormattedTextSegment>[] lists) {
        int totalCount = 0;
        foreach (List<FormattedTextSegment> list in lists) {
            totalCount += list.Count;
        }
        List<FormattedTextSegment> result = new(totalCount);
        foreach (List<FormattedTextSegment> list in lists) {
            result.AddRange(list);
        }
        return result;
    }

    public List<FormattedTextSegment> VisitSegmentRegisterNode(SegmentRegisterNode node) {
        return Seg(_registerRenderer.ToStringSegmentRegister(node.RegisterIndex), FormatterTextKind.Register);
    }

    public List<FormattedTextSegment> VisitSegmentedPointer(SegmentedPointerNode node) {
        List<FormattedTextSegment> result = [];
        string pointerType = PointerDataTypeToString(node.DataType);
        if (pointerType.Length > 0) {
            result.AddRange(Seg(pointerType, FormatterTextKind.Keyword));
            result.AddRange(Seg(" ", FormatterTextKind.Text));
        }

        List<FormattedTextSegment> segment = RenderSegment(node);
        if (segment.Count > 0) {
            result.AddRange(segment);
            result.AddRange(Seg(":", FormatterTextKind.Punctuation));
        }

        result.AddRange(Seg("[", FormatterTextKind.Punctuation));
        result.AddRange(node.Offset.Accept(this));
        result.AddRange(Seg("]", FormatterTextKind.Punctuation));
        return result;
    }

    private List<FormattedTextSegment> RenderSegment(SegmentedPointerNode node) {
        if (!_config.ShowDefaultSegment && node.DefaultSegment == node.Segment) {
            return [];
        }
        return node.Segment.Accept(this);
    }

    public List<FormattedTextSegment> VisitRegisterNode(RegisterNode node) {
        return Seg(_registerRenderer.ToStringRegister(node.DataType.BitWidth, node.RegisterIndex), FormatterTextKind.Register);
    }

    public List<FormattedTextSegment> VisitAbsolutePointerNode(AbsolutePointerNode node) {
        List<FormattedTextSegment> result = [];
        result.AddRange(Seg(PointerDataTypeToString(node.DataType), FormatterTextKind.Keyword));
        result.AddRange(Seg(" [", FormatterTextKind.Punctuation));
        result.AddRange(node.AbsoluteAddress.Accept(this));
        result.AddRange(Seg("]", FormatterTextKind.Punctuation));
        return result;
    }

    public List<FormattedTextSegment> VisitCpuFlagNode(CpuFlagNode node) {
        string flagName = node.FlagMask switch {
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
        return Seg(flagName, FormatterTextKind.Register);
    }

    public List<FormattedTextSegment> VisitConstantNode(ConstantNode node) {
        if (node.IsNegative) {
            long valueSigned = node.SignedValue;
            return Seg(valueSigned.ToString(CultureInfo.InvariantCulture), FormatterTextKind.Number);
        }
        ulong value = node.Value;
        if (value < 10 && _config.PrefixHexWith0X) {
            return Seg(value.ToString(CultureInfo.InvariantCulture), FormatterTextKind.Number);
        }

        string prefix = _config.PrefixHexWith0X ? "0x" : "";
        string number = prefix + node.DataType.BitWidth switch {
            BitWidth.NIBBLE_4 => $"{value:X1}",
            BitWidth.QUIBBLE_5 => $"{value:X1}",
            BitWidth.BYTE_8 => $"{value:X2}",
            BitWidth.WORD_16 => $"{value:X4}",
            BitWidth.DWORD_32 => $"{value:X8}",
            BitWidth.QWORD_64 => $"{value:X16}",
            _ => throw new InvalidOperationException($"Unsupported bit width {node.DataType.BitWidth}")
        };
        return Seg(number, FormatterTextKind.Number);
    }

    public List<FormattedTextSegment> VisitNearAddressNode(NearAddressNode node) {
        if (!_config.DwordJumpOffset) {
            return VisitConstantNode(node);
        }

        ConstantNode nodePadded = new(DataType.UINT32, node.Value);
        List<FormattedTextSegment> addressSegments = nodePadded.Accept(this);

        long delta = (long)(node.Value - node.BaseAddress.Offset);
        string plus = delta > 0 ? "+" : "-";
        ulong deltaAbsolute = (ulong)Math.Abs(delta);
        string deltaString = $" (${plus}{deltaAbsolute:X})".ToLower();

        return Concat(
            Seg(string.Join("", addressSegments.ConvertAll(s => s.Text)), FormatterTextKind.FunctionAddress),
            Seg(deltaString, FormatterTextKind.Text));
    }

    public List<FormattedTextSegment> VisitSegmentedAddressConstantNode(SegmentedAddressConstantNode node) {
        return Seg(node.Value.ToString(), FormatterTextKind.FunctionAddress);
    }

    public List<FormattedTextSegment> VisitBinaryOperationNode(BinaryOperationNode node) {
        List<FormattedTextSegment> left = RenderOperand(node.Left, node.BinaryOperation, isLeftOperand: true);
        if (IsZero(node.Right) && node.BinaryOperation == BinaryOperation.PLUS) {
            return left;
        }
        List<FormattedTextSegment> right = RenderOperand(node.Right, node.BinaryOperation, isLeftOperand: false);
        if (IsNegative(node.Right) && node.BinaryOperation == BinaryOperation.PLUS) {
            return Concat(left, right);
        }
        return Concat(left, Seg(OperationToString(node.BinaryOperation), FormatterTextKind.Operator), right);
    }

    private List<FormattedTextSegment> RenderOperand(ValueNode operand, BinaryOperation parentOperation, bool isLeftOperand) {
        List<FormattedTextSegment> rendered = operand.Accept(this);

        if (operand is BinaryOperationNode childBinaryOp) {
            int parentPrecedence = GetPrecedence(parentOperation);
            int childPrecedence = GetPrecedence(childBinaryOp.BinaryOperation);

            if (childPrecedence < parentPrecedence ||
                (!isLeftOperand && childPrecedence == parentPrecedence)) {
                rendered = Concat(Seg("(", FormatterTextKind.Punctuation), rendered, Seg(")", FormatterTextKind.Punctuation));
            }
        }

        return rendered;
    }

    private static int GetPrecedence(BinaryOperation operation) {
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

    public List<FormattedTextSegment> VisitUnaryOperationNode(UnaryOperationNode node) {
        List<FormattedTextSegment> value = node.Value.Accept(this);
        if (node.Value is BinaryOperationNode) {
            value = Concat(Seg("(", FormatterTextKind.Punctuation), value, Seg(")", FormatterTextKind.Punctuation));
        }
        return Concat(Seg(UnaryOperationToString(node.UnaryOperation), FormatterTextKind.Operator), value);
    }

    public List<FormattedTextSegment> VisitTypeConversionNode(TypeConversionNode node) {
        string typeStr = node.DataType.BitWidth switch {
            BitWidth.NIBBLE_4 => node.DataType.Signed ? "(sbyte)" : "(byte)",
            BitWidth.BYTE_8 => node.DataType.Signed ? "(sbyte)" : "(byte)",
            BitWidth.WORD_16 => node.DataType.Signed ? "(short)" : "(ushort)",
            BitWidth.DWORD_32 => node.DataType.Signed ? "(int)" : "(uint)",
            BitWidth.QWORD_64 => node.DataType.Signed ? "(long)" : "(ulong)",
            _ => throw new InvalidOperationException($"Unsupported bit width {node.DataType.BitWidth}")
        };
        return Concat(Seg(typeStr, FormatterTextKind.Keyword), node.Value.Accept(this));
    }

    public List<FormattedTextSegment> VisitInstructionNode(InstructionNode node) {
        List<FormattedTextSegment> result = [];

        RepPrefix? repPrefix = node.RepPrefix;
        if (repPrefix != null) {
            result.AddRange(Seg(Enum.GetName(repPrefix.Value)?.ToLower() + " ", FormatterTextKind.Prefix));
        }

        string mnemonic = _mnemonicRenderer.MnemonicToString(node.Operation);
        result.AddRange(Seg(mnemonic, FormatterTextKind.Mnemonic));

        if (node.Parameters.Length == 0) {
            return result;
        }

        result.AddRange(Seg(" ", FormatterTextKind.Text));

        for (int i = 0; i < node.Parameters.Length; i++) {
            if (i > 0) {
                result.AddRange(Seg(",", FormatterTextKind.Punctuation));
            }
            result.AddRange(node.Parameters[i].Accept(this));
        }

        return result;
    }

    private static string OperationToString(BinaryOperation binaryOperation) {
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

    private static string UnaryOperationToString(UnaryOperation unaryOperation) {
        return unaryOperation switch {
            UnaryOperation.NOT => "!",
            UnaryOperation.NEGATE => "-",
            UnaryOperation.BITWISE_NOT => "~",
            _ => throw new InvalidOperationException($"Unsupported AST operation {unaryOperation}")
        };
    }

    private string PointerDataTypeToString(DataType dataType) {
        if (!_config.ExplicitPointerType) {
            return "";
        }
        return dataType.BitWidth switch {
            BitWidth.BYTE_8 => "byte ptr",
            BitWidth.WORD_16 => "word ptr",
            BitWidth.DWORD_32 => "dword ptr",
            BitWidth.QWORD_64 => "qword ptr",
            _ => throw new InvalidOperationException($"Unsupported bit width {dataType.BitWidth}")
        };
    }

    private static bool IsZero(ValueNode valueNode) {
        return valueNode is ConstantNode constantNode && constantNode.Value == 0;
    }

    private static bool IsNegative(ValueNode valueNode) {
        return valueNode is ConstantNode { IsNegative: true };
    }

    public List<FormattedTextSegment> VisitMethodCallNode(MethodCallNode node) {
        throw new NotSupportedException("MethodCallNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitBlockNode(BlockNode node) {
        throw new NotSupportedException("BlockNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitIfElseNode(IfElseNode node) {
        throw new NotSupportedException("IfElseNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitMethodCallValueNode(MethodCallValueNode node) {
        throw new NotSupportedException("MethodCallValueNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitMoveIpNextNode(MoveIpNextNode node) {
        throw new NotSupportedException("MoveIpNextNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitVariableReferenceNode(VariableReferenceNode node) {
        throw new NotSupportedException("VariableReferenceNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitVariableDeclarationNode(VariableDeclarationNode node) {
        throw new NotSupportedException("VariableDeclarationNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitCallNearNode(CallNearNode node) {
        throw new NotSupportedException("CallNearNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitCallFarNode(CallFarNode node) {
        throw new NotSupportedException("CallFarNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitReturnNearNode(ReturnNearNode node) {
        throw new NotSupportedException("ReturnNearNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitReturnFarNode(ReturnFarNode node) {
        throw new NotSupportedException("ReturnFarNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitJumpNearNode(JumpNearNode node) {
        throw new NotSupportedException("JumpNearNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitJumpFarNode(JumpFarNode node) {
        throw new NotSupportedException("JumpFarNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitInterruptCallNode(InterruptCallNode node) {
        throw new NotSupportedException("InterruptCallNode should not be rendered as assembly.");
    }

    public List<FormattedTextSegment> VisitReturnInterruptNode(ReturnInterruptNode node) {
        throw new NotSupportedException("ReturnInterruptNode should not be rendered as assembly.");
    }
}
