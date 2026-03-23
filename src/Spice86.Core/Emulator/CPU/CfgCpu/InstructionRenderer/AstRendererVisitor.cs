namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Shared.Emulator.Memory;

using System.Globalization;

public class AstRendererVisitor<TOutput> : IAstVisitor<TOutput> {
    private readonly AsmRenderingConfig _config;
    private readonly RegisterRenderer _registerRenderer;
    private readonly MnemonicRenderer _mnemonicRenderer;
    private readonly IAstOutputRenderer<TOutput> _outputRenderer;

    public AstRendererVisitor(AsmRenderingConfig config, IAstOutputRenderer<TOutput> outputRenderer) {
        _config = config;
        _registerRenderer = new RegisterRenderer(config);
        _mnemonicRenderer = new MnemonicRenderer(config);
        _outputRenderer = outputRenderer;
    }

    public TOutput VisitSegmentRegisterNode(SegmentRegisterNode node) {
        return _outputRenderer.Register(_registerRenderer.ToStringSegmentRegister(node.RegisterIndex));
    }

    public TOutput VisitSegmentedPointer(SegmentedPointerNode node) {
        List<TOutput> result = [];
        string pointerType = PointerDataTypeToString(node.DataType);
        if (pointerType.Length > 0) {
            result.Add(_outputRenderer.Keyword(pointerType));
            result.Add(_outputRenderer.Text(" "));
        }

        TOutput segment = RenderSegment(node);
        if (!_outputRenderer.IsEmpty(segment)) {
            result.Add(segment);
            result.Add(_outputRenderer.Punctuation(":"));
        }

        result.Add(_outputRenderer.Punctuation("["));
        result.Add(node.Offset.Accept(this));
        result.Add(_outputRenderer.Punctuation("]"));
        return _outputRenderer.Concat([.. result]);
    }

    private TOutput RenderSegment(SegmentedPointerNode node) {
        if (!_config.ShowDefaultSegment && node.DefaultSegment == node.Segment) {
            return _outputRenderer.Empty();
        }
        return node.Segment.Accept(this);
    }

    public TOutput VisitRegisterNode(RegisterNode node) {
        return _outputRenderer.Register(_registerRenderer.ToStringRegister(node.DataType.BitWidth, node.RegisterIndex));
    }

    public TOutput VisitAbsolutePointerNode(AbsolutePointerNode node) {
        return _outputRenderer.Concat(
            _outputRenderer.Keyword(PointerDataTypeToString(node.DataType)),
            _outputRenderer.Punctuation(" ["),
            node.AbsoluteAddress.Accept(this),
            _outputRenderer.Punctuation("]"));
    }

    public TOutput VisitCpuFlagNode(CpuFlagNode node) {
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

        return _outputRenderer.Register(flagName);
    }

    public TOutput VisitConstantNode(ConstantNode node) {
        if (node.IsNegative) {
            long valueSigned = node.SignedValue;
            return _outputRenderer.Number(valueSigned.ToString(CultureInfo.InvariantCulture));
        }

        ulong value = node.Value;
        if (value < 10 && _config.PrefixHexWith0X) {
            return _outputRenderer.Number(value.ToString(CultureInfo.InvariantCulture));
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

        return _outputRenderer.Number(number);
    }

    public TOutput VisitNearAddressNode(NearAddressNode node) {
        if (!_config.DwordJumpOffset) {
            return VisitConstantNode(node);
        }

        ConstantNode nodePadded = new(DataType.UINT32, node.Value);
        TOutput address = nodePadded.Accept(this);

        long delta = (long)(node.Value - node.BaseAddress.Offset);
        string plus = delta > 0 ? "+" : "-";
        ulong deltaAbsolute = (ulong)Math.Abs(delta);
        string deltaString = $" (${plus}{deltaAbsolute:X})".ToLower();

        return _outputRenderer.Concat(
            _outputRenderer.FunctionAddress(_outputRenderer.ToPlainText(address)),
            _outputRenderer.Text(deltaString));
    }

    public TOutput VisitSegmentedAddressConstantNode(SegmentedAddressConstantNode node) {
        return _outputRenderer.FunctionAddress(node.Value.ToString());
    }

    public TOutput VisitBinaryOperationNode(BinaryOperationNode node) {
        TOutput left = RenderOperand(node.Left, node.BinaryOperation, isLeftOperand: true);
        if (IsZero(node.Right) && node.BinaryOperation == BinaryOperation.PLUS) {
            return left;
        }

        TOutput right = RenderOperand(node.Right, node.BinaryOperation, isLeftOperand: false);
        if (IsNegative(node.Right) && node.BinaryOperation == BinaryOperation.PLUS) {
            return _outputRenderer.Concat(left, right);
        }

        return _outputRenderer.Concat(left, _outputRenderer.Operator(OperationToString(node.BinaryOperation)), right);
    }

    private TOutput RenderOperand(ValueNode operand, BinaryOperation parentOperation, bool isLeftOperand) {
        TOutput rendered = operand.Accept(this);

        if (operand is BinaryOperationNode childBinaryOp) {
            int parentPrecedence = GetPrecedence(parentOperation);
            int childPrecedence = GetPrecedence(childBinaryOp.BinaryOperation);

            if (childPrecedence < parentPrecedence ||
                (!isLeftOperand && childPrecedence == parentPrecedence)) {
                rendered = _outputRenderer.Concat(
                    _outputRenderer.Punctuation("("),
                    rendered,
                    _outputRenderer.Punctuation(")"));
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

    public TOutput VisitUnaryOperationNode(UnaryOperationNode node) {
        TOutput value = node.Value.Accept(this);
        if (node.Value is BinaryOperationNode) {
            value = _outputRenderer.Concat(_outputRenderer.Punctuation("("), value, _outputRenderer.Punctuation(")"));
        }
        return _outputRenderer.Concat(_outputRenderer.Operator(UnaryOperationToString(node.UnaryOperation)), value);
    }

    public TOutput VisitTypeConversionNode(TypeConversionNode node) {
        string typeStr = node.DataType.BitWidth switch {
            BitWidth.NIBBLE_4 => node.DataType.Signed ? "(sbyte)" : "(byte)",
            BitWidth.BYTE_8 => node.DataType.Signed ? "(sbyte)" : "(byte)",
            BitWidth.WORD_16 => node.DataType.Signed ? "(short)" : "(ushort)",
            BitWidth.DWORD_32 => node.DataType.Signed ? "(int)" : "(uint)",
            BitWidth.QWORD_64 => node.DataType.Signed ? "(long)" : "(ulong)",
            _ => throw new InvalidOperationException($"Unsupported bit width {node.DataType.BitWidth}")
        };
        return _outputRenderer.Concat(_outputRenderer.Keyword(typeStr), node.Value.Accept(this));
    }

    private static bool IsZero(ValueNode valueNode) {
        return valueNode is ConstantNode constantNode && constantNode.Value == 0;
    }

    private static bool IsNegative(ValueNode valueNode) {
        return valueNode is ConstantNode { IsNegative: true };
    }

    public TOutput VisitInstructionNode(InstructionNode node) {
        List<TOutput> result = [];

        if (node.RepPrefix is { } repPrefix) {
            string prefixText = Enum.GetName(repPrefix)!.ToLowerInvariant() + " ";
            result.Add(_outputRenderer.Prefix(prefixText));
        }

        string mnemonic = _mnemonicRenderer.MnemonicToString(node.Operation);
        result.Add(_outputRenderer.Mnemonic(mnemonic));

        if (node.Parameters.Length == 0) {
            return _outputRenderer.Concat([.. result]);
        }

        result.Add(_outputRenderer.Text(" "));

        for (int i = 0; i < node.Parameters.Length; i++) {
            if (i > 0) {
                result.Add(_outputRenderer.Punctuation(","));
            }
            result.Add(node.Parameters[i].Accept(this));
        }

        return _outputRenderer.Concat([.. result]);
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

    public TOutput VisitMethodCallNode(MethodCallNode node) {
        throw CreateUnsupportedNodeException(nameof(MethodCallNode));
    }

    public TOutput VisitBlockNode(BlockNode node) {
        throw CreateUnsupportedNodeException(nameof(BlockNode));
    }

    public TOutput VisitIfElseNode(IfElseNode node) {
        throw CreateUnsupportedNodeException(nameof(IfElseNode));
    }

    public TOutput VisitMethodCallValueNode(MethodCallValueNode node) {
        throw CreateUnsupportedNodeException(nameof(MethodCallValueNode));
    }

    public TOutput VisitMoveIpNextNode(MoveIpNextNode node) {
        throw CreateUnsupportedNodeException(nameof(MoveIpNextNode));
    }

    public TOutput VisitVariableReferenceNode(VariableReferenceNode node) {
        throw CreateUnsupportedNodeException(nameof(VariableReferenceNode));
    }

    public TOutput VisitVariableDeclarationNode(VariableDeclarationNode node) {
        throw CreateUnsupportedNodeException(nameof(VariableDeclarationNode));
    }

    public TOutput VisitCallNearNode(CallNearNode node) {
        throw CreateUnsupportedNodeException(nameof(CallNearNode));
    }

    public TOutput VisitCallFarNode(CallFarNode node) {
        throw CreateUnsupportedNodeException(nameof(CallFarNode));
    }

    public TOutput VisitReturnNearNode(ReturnNearNode node) {
        throw CreateUnsupportedNodeException(nameof(ReturnNearNode));
    }

    public TOutput VisitReturnFarNode(ReturnFarNode node) {
        throw CreateUnsupportedNodeException(nameof(ReturnFarNode));
    }

    public TOutput VisitJumpNearNode(JumpNearNode node) {
        throw CreateUnsupportedNodeException(nameof(JumpNearNode));
    }

    public TOutput VisitJumpFarNode(JumpFarNode node) {
        throw CreateUnsupportedNodeException(nameof(JumpFarNode));
    }

    public TOutput VisitInterruptCallNode(InterruptCallNode node) {
        throw CreateUnsupportedNodeException(nameof(InterruptCallNode));
    }

    public TOutput VisitReturnInterruptNode(ReturnInterruptNode node) {
        throw CreateUnsupportedNodeException(nameof(ReturnInterruptNode));
    }

    private static InvalidOperationException CreateUnsupportedNodeException(string nodeName) {
        return new InvalidOperationException($"{nodeName} should not be rendered as assembly.");
    }
}
