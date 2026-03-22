namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Shared.Emulator.Memory;

using System.Globalization;

/// <summary>
/// Unified AST visitor that renders assembly instructions by delegating each output fragment to an
/// <see cref="IRenderer"/>. Use <see cref="StringRenderer"/> for plain-string output and
/// a token-collecting renderer (in the UI layer) for syntax-highlighted token lists.
/// </summary>
public class AstFormattedTokenRenderer : IAstVisitor<object?> {
    private readonly AsmRenderingConfig _config;
    private readonly IRenderer _renderer;
    private readonly RegisterRenderer _registerRenderer;
    private readonly MnemonicRenderer _mnemonicRenderer;

    /// <summary>
    /// Initializes a new instance of <see cref="AstFormattedTokenRenderer"/>.
    /// </summary>
    /// <param name="config">Rendering configuration.</param>
    /// <param name="renderer">The renderer that receives each output fragment.</param>
    public AstFormattedTokenRenderer(AsmRenderingConfig config, IRenderer renderer) {
        _config = config;
        _renderer = renderer;
        _registerRenderer = new RegisterRenderer(config);
        _mnemonicRenderer = new MnemonicRenderer(config);
    }

    /// <inheritdoc/>
    public object? VisitSegmentRegisterNode(SegmentRegisterNode node) {
        _renderer.WriteRegister(_registerRenderer.ToStringSegmentRegister(node.RegisterIndex));
        return null;
    }

    /// <inheritdoc/>
    public object? VisitSegmentedPointer(SegmentedPointerNode node) {
        string pointerType = PointerDataTypeToString(node.DataType);
        if (pointerType.Length > 0) {
            _renderer.WriteKeyword(pointerType);
            _renderer.WriteText(" ");
        }

        bool showSegment = _config.ShowDefaultSegment || node.DefaultSegment != node.Segment;
        if (showSegment) {
            node.Segment.Accept(this);
            _renderer.WritePunctuation(":");
        }

        _renderer.WritePunctuation("[");
        node.Offset.Accept(this);
        _renderer.WritePunctuation("]");
        return null;
    }

    /// <inheritdoc/>
    public object? VisitRegisterNode(RegisterNode node) {
        _renderer.WriteRegister(_registerRenderer.ToStringRegister(node.DataType.BitWidth, node.RegisterIndex));
        return null;
    }

    /// <inheritdoc/>
    public object? VisitAbsolutePointerNode(AbsolutePointerNode node) {
        _renderer.WriteKeyword(PointerDataTypeToString(node.DataType));
        _renderer.WritePunctuation(" [");
        node.AbsoluteAddress.Accept(this);
        _renderer.WritePunctuation("]");
        return null;
    }

    /// <inheritdoc/>
    public object? VisitCpuFlagNode(CpuFlagNode node) {
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
        _renderer.WriteRegister(flagName);
        return null;
    }

    /// <inheritdoc/>
    public object? VisitConstantNode(ConstantNode node) {
        _renderer.WriteNumber(GetConstantText(node));
        return null;
    }

    /// <inheritdoc/>
    public object? VisitNearAddressNode(NearAddressNode node) {
        if (!_config.DwordJumpOffset) {
            return VisitConstantNode(node);
        }

        ConstantNode nodePadded = new(DataType.UINT32, node.Value);
        string addressText = GetConstantText(nodePadded);

        long delta = (long)(node.Value - node.BaseAddress.Offset);
        string plus = delta > 0 ? "+" : "-";
        ulong deltaAbsolute = (ulong)Math.Abs(delta);
        string deltaString = $" (${plus}{deltaAbsolute:X})".ToLower();

        _renderer.WriteFunctionAddress(addressText);
        _renderer.WriteText(deltaString);
        return null;
    }

    /// <inheritdoc/>
    public object? VisitSegmentedAddressConstantNode(SegmentedAddressConstantNode node) {
        _renderer.WriteFunctionAddress(node.Value.ToString());
        return null;
    }

    /// <inheritdoc/>
    public object? VisitBinaryOperationNode(BinaryOperationNode node) {
        RenderOperand(node.Left, node.BinaryOperation, isLeftOperand: true);
        if (IsZero(node.Right) && node.BinaryOperation == BinaryOperation.PLUS) {
            return null;
        }
        if (!IsNegative(node.Right) || node.BinaryOperation != BinaryOperation.PLUS) {
            _renderer.WriteOperator(OperationToString(node.BinaryOperation));
        }
        RenderOperand(node.Right, node.BinaryOperation, isLeftOperand: false);
        return null;
    }

    private void RenderOperand(ValueNode operand, BinaryOperation parentOperation, bool isLeftOperand) {
        bool needsParens = false;
        if (operand is BinaryOperationNode childBinaryOp) {
            int parentPrecedence = GetPrecedence(parentOperation);
            int childPrecedence = GetPrecedence(childBinaryOp.BinaryOperation);
            needsParens = childPrecedence < parentPrecedence ||
                          (!isLeftOperand && childPrecedence == parentPrecedence);
        }

        if (needsParens) {
            _renderer.WritePunctuation("(");
        }
        operand.Accept(this);
        if (needsParens) {
            _renderer.WritePunctuation(")");
        }
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

    /// <inheritdoc/>
    public object? VisitUnaryOperationNode(UnaryOperationNode node) {
        _renderer.WriteOperator(UnaryOperationToString(node.UnaryOperation));
        if (node.Value is BinaryOperationNode) {
            _renderer.WritePunctuation("(");
            node.Value.Accept(this);
            _renderer.WritePunctuation(")");
        } else {
            node.Value.Accept(this);
        }
        return null;
    }

    /// <inheritdoc/>
    public object? VisitTypeConversionNode(TypeConversionNode node) {
        string typeStr = node.DataType.BitWidth switch {
            BitWidth.NIBBLE_4 => node.DataType.Signed ? "(sbyte)" : "(byte)",
            BitWidth.BYTE_8 => node.DataType.Signed ? "(sbyte)" : "(byte)",
            BitWidth.WORD_16 => node.DataType.Signed ? "(short)" : "(ushort)",
            BitWidth.DWORD_32 => node.DataType.Signed ? "(int)" : "(uint)",
            BitWidth.QWORD_64 => node.DataType.Signed ? "(long)" : "(ulong)",
            _ => throw new InvalidOperationException($"Unsupported bit width {node.DataType.BitWidth}")
        };
        _renderer.WriteKeyword(typeStr);
        node.Value.Accept(this);
        return null;
    }

    /// <inheritdoc/>
    public object? VisitInstructionNode(InstructionNode node) {
        RepPrefix? repPrefix = node.RepPrefix;
        if (repPrefix != null) {
            string prefixName = Enum.GetName(repPrefix.Value)?.ToLower() ?? string.Empty;
            if (prefixName.Length > 0) {
                _renderer.WritePrefix(prefixName + " ");
            }
        }

        _renderer.WriteMnemonic(_mnemonicRenderer.MnemonicToString(node.Operation));

        if (node.Parameters.Length == 0) {
            return null;
        }

        _renderer.WriteText(" ");
        for (int i = 0; i < node.Parameters.Length; i++) {
            if (i > 0) {
                _renderer.WritePunctuation(",");
            }
            node.Parameters[i].Accept(this);
        }
        return null;
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

    private string GetConstantText(ConstantNode node) {
        if (node.IsNegative) {
            return node.SignedValue.ToString(CultureInfo.InvariantCulture);
        }
        ulong value = node.Value;
        if (value < 10 && _config.PrefixHexWith0X) {
            return value.ToString(CultureInfo.InvariantCulture);
        }
        string prefix = _config.PrefixHexWith0X ? "0x" : "";
        return prefix + node.DataType.BitWidth switch {
            BitWidth.NIBBLE_4 => $"{value:X1}",
            BitWidth.QUIBBLE_5 => $"{value:X1}",
            BitWidth.BYTE_8 => $"{value:X2}",
            BitWidth.WORD_16 => $"{value:X4}",
            BitWidth.DWORD_32 => $"{value:X8}",
            BitWidth.QWORD_64 => $"{value:X16}",
            _ => throw new InvalidOperationException($"Unsupported bit width {node.DataType.BitWidth}")
        };
    }

    private static bool IsZero(ValueNode valueNode) {
        return valueNode is ConstantNode constantNode && constantNode.Value == 0;
    }

    private static bool IsNegative(ValueNode valueNode) {
        return valueNode is ConstantNode { IsNegative: true };
    }

    /// <inheritdoc/>
    public object? VisitMethodCallNode(MethodCallNode node) {
        throw new NotSupportedException("MethodCallNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitBlockNode(BlockNode node) {
        throw new NotSupportedException("BlockNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitIfElseNode(IfElseNode node) {
        throw new NotSupportedException("IfElseNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitMethodCallValueNode(MethodCallValueNode node) {
        throw new NotSupportedException("MethodCallValueNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitMoveIpNextNode(MoveIpNextNode node) {
        throw new NotSupportedException("MoveIpNextNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitVariableReferenceNode(VariableReferenceNode node) {
        throw new NotSupportedException("VariableReferenceNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitVariableDeclarationNode(VariableDeclarationNode node) {
        throw new NotSupportedException("VariableDeclarationNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitCallNearNode(CallNearNode node) {
        throw new NotSupportedException("CallNearNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitCallFarNode(CallFarNode node) {
        throw new NotSupportedException("CallFarNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitReturnNearNode(ReturnNearNode node) {
        throw new NotSupportedException("ReturnNearNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitReturnFarNode(ReturnFarNode node) {
        throw new NotSupportedException("ReturnFarNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitJumpNearNode(JumpNearNode node) {
        throw new NotSupportedException("JumpNearNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitJumpFarNode(JumpFarNode node) {
        throw new NotSupportedException("JumpFarNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitInterruptCallNode(InterruptCallNode node) {
        throw new NotSupportedException("InterruptCallNode should not be rendered as assembly.");
    }

    /// <inheritdoc/>
    public object? VisitReturnInterruptNode(ReturnInterruptNode node) {
        throw new NotSupportedException("ReturnInterruptNode should not be rendered as assembly.");
    }
}
