namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Recognizes the six-statement AST the parser builds for DIV/IDIV and carries the operands and output
/// registers the emitter needs to replace those statements with one helper call:
/// 1. divisor variable declaration
/// 2. dividend variable declaration
/// 3. quotient variable declaration initialized with the ALU Div/Idiv call
/// 4. quotient assignment to the low register
/// 5. remainder assignment to the high register
/// 6. MoveIpNextNode
/// </summary>
internal sealed record DivisionPattern(
    BitWidth Width, bool Signed, ValueNode Dividend, ValueNode Divisor,
    RegisterNode Low, RegisterNode High) {

    private static readonly AstBuilder Builder = new();

    /// <summary>
    /// Matches the division shape in <paramref name="node"/>.
    /// A node that is not a root <see cref="BlockNode"/> whose first three statements are the
    /// <c>divisor</c>, <c>dividend</c> and <c>quotient</c> declarations is not a division candidate and
    /// yields <c>null</c>, so the caller keeps its normal lowering. A candidate must satisfy every
    /// invariant of the parser's division shape; a violated invariant throws
    /// <see cref="NotSupportedException"/> naming <paramref name="instructionAddress"/> instead of falling
    /// back to normal lowering. Matching reads the AST only: it mutates nothing.
    /// </summary>
    /// <param name="node">The root execution AST node of one instruction.</param>
    /// <param name="instructionAddress">Address of the instruction, reported in malformed-candidate diagnostics.</param>
    /// <returns>The recognized division, or <c>null</c> when <paramref name="node"/> is not a candidate.</returns>
    /// <exception cref="NotSupportedException">The node is a candidate but violates a division invariant.</exception>
    public static DivisionPattern? TryMatch(IVisitableAstNode node, SegmentedAddress instructionAddress) {
        if (node is not BlockNode block || block.Statements.Count < 3) {
            return null;
        }
        IReadOnlyList<IVisitableAstNode> statements = block.Statements;
        if (statements[0] is not VariableDeclarationNode divisorDecl || divisorDecl.VariableName != "divisor" ||
            statements[1] is not VariableDeclarationNode dividendDecl || dividendDecl.VariableName != "dividend" ||
            statements[2] is not VariableDeclarationNode quotientDecl || quotientDecl.VariableName != "quotient") {
            return null;
        }
        if (statements.Count != 6) {
            throw Malformed(instructionAddress, $"the block holds {statements.Count} statements instead of six");
        }
        if (statements[5] is not MoveIpNextNode) {
            throw Malformed(instructionAddress, "the final statement is not a MoveIpNextNode");
        }
        if (quotientDecl.Initializer is not MethodCallValueNode aluCall) {
            throw Malformed(instructionAddress, "the quotient initializer is not an ALU method call");
        }

        MethodCallNode callNode = aluCall.CallNode;
        BitWidth width = WidthFromAluProperty(callNode.PropertyPath, instructionAddress);
        bool signed = SignedFromAluMethod(callNode.MethodName, instructionAddress);
        DataType divisorType = DataType.FromBitWidth(width, signed);
        DataType dividendType = DataType.FromBitWidth(width.Double(), signed);
        DataType unsignedType = DataType.UnsignedFromBitWidth(width);

        RequireType(instructionAddress, "the divisor declaration type", divisorDecl.DataType, divisorType);
        RequireType(instructionAddress, "the divisor initializer type", divisorDecl.Initializer.DataType, divisorType);
        RequireType(instructionAddress, "the dividend declaration type", dividendDecl.DataType, dividendType);
        RequireType(instructionAddress, "the dividend initializer type", dividendDecl.Initializer.DataType, dividendType);
        RequireType(instructionAddress, "the quotient declaration type", quotientDecl.DataType, divisorType);
        RequireType(instructionAddress, "the ALU method result type", aluCall.DataType, divisorType);
        RequireAluArguments(instructionAddress, callNode, dividendDecl, divisorDecl);

        (RegisterNode low, RegisterNode high) = ExpectedRegisters(width);
        BinaryOperationNode quotientAssignment = RequireAssignment(instructionAddress, "statement 4", statements[3], unsignedType);
        if (quotientAssignment.Left != low) {
            throw Malformed(instructionAddress, "statement 4 does not target the low output register");
        }
        if (quotientAssignment.Right != Builder.TypeConversion.Convert(unsignedType, quotientDecl.Reference)) {
            throw Malformed(instructionAddress, "statement 4 does not assign the quotient reference");
        }

        BinaryOperationNode remainderAssignment = RequireAssignment(instructionAddress, "statement 5", statements[4], unsignedType);
        if (remainderAssignment.Left != high) {
            throw Malformed(instructionAddress, "statement 5 does not target the high output register");
        }
        if (remainderAssignment.Right != ExpectedRemainder(unsignedType, dividendType, dividendDecl.Reference, divisorDecl.Reference)) {
            throw Malformed(instructionAddress, "statement 5 does not assign the canonical remainder expression");
        }
        if (dividendDecl.Initializer != ExpectedDividendInitializer(width, dividendType, low, high)) {
            throw Malformed(instructionAddress, "the dividend initializer is not the canonical register combination");
        }
        RequireDivisorOperand(instructionAddress, divisorDecl.Initializer, signed, unsignedType, divisorType);

        RegisterNode lowOutput = (RegisterNode)quotientAssignment.Left;
        RegisterNode highOutput = (RegisterNode)remainderAssignment.Left;
        return new DivisionPattern(width, signed, dividendDecl.Initializer, divisorDecl.Initializer, lowOutput, highOutput);
    }

    private static BitWidth WidthFromAluProperty(string? propertyPath, SegmentedAddress instructionAddress) {
        return propertyPath switch {
            "Alu8" => BitWidth.BYTE_8,
            "Alu16" => BitWidth.WORD_16,
            "Alu32" => BitWidth.DWORD_32,
            _ => throw Malformed(instructionAddress, $"unknown ALU property path '{propertyPath ?? "null"}'")
        };
    }

    private static bool SignedFromAluMethod(string methodName, SegmentedAddress instructionAddress) {
        return methodName switch {
            "Div" => false,
            "Idiv" => true,
            _ => throw Malformed(instructionAddress, $"unknown ALU method '{methodName}'")
        };
    }

    private static void RequireType(SegmentedAddress instructionAddress, string what, DataType actual, DataType expected) {
        if (actual != expected) {
            throw Malformed(instructionAddress, $"{what} is {actual} instead of {expected}");
        }
    }

    private static void RequireAluArguments(SegmentedAddress instructionAddress, MethodCallNode callNode,
        VariableDeclarationNode dividendDecl, VariableDeclarationNode divisorDecl) {
        if (callNode.Arguments.Count != 2) {
            throw Malformed(instructionAddress, $"the ALU call argument count is {callNode.Arguments.Count} instead of two");
        }
        if (callNode.Arguments[0] is not VariableReferenceNode dividendArgument || dividendArgument != dividendDecl.Reference) {
            throw Malformed(instructionAddress, "the first ALU argument is not the dividend declaration reference");
        }
        if (callNode.Arguments[1] is not VariableReferenceNode divisorArgument || divisorArgument != divisorDecl.Reference) {
            throw Malformed(instructionAddress, "the second ALU argument is not the divisor declaration reference");
        }
    }

    private static BinaryOperationNode RequireAssignment(SegmentedAddress instructionAddress, string label,
        IVisitableAstNode statement, DataType expectedType) {
        if (statement is not BinaryOperationNode assignment || assignment.BinaryOperation != BinaryOperation.ASSIGN) {
            throw Malformed(instructionAddress, $"{label} is not an assignment");
        }
        RequireType(instructionAddress, $"{label} type", assignment.DataType, expectedType);
        return assignment;
    }

    private static (RegisterNode Low, RegisterNode High) ExpectedRegisters(BitWidth width) {
        DataType registerType = DataType.UnsignedFromBitWidth(width);
        return width switch {
            BitWidth.BYTE_8 => (new RegisterNode(registerType, (int)RegisterIndex.AxIndex),
                new RegisterNode(registerType, (int)RegisterIndex.AxIndex + 4)),
            BitWidth.WORD_16 => (new RegisterNode(registerType, (int)RegisterIndex.AxIndex),
                new RegisterNode(registerType, (int)RegisterIndex.DxIndex)),
            BitWidth.DWORD_32 => (new RegisterNode(registerType, (int)RegisterIndex.AxIndex),
                new RegisterNode(registerType, (int)RegisterIndex.DxIndex)),
            _ => throw new NotSupportedException($"Unsupported division width {width}.")
        };
    }

    private static ValueNode ExpectedRemainder(DataType unsignedType, DataType dividendType,
        VariableReferenceNode dividendReference, VariableReferenceNode divisorReference) {
        BinaryOperationNode modulo = new(dividendType, dividendReference, BinaryOperation.MODULO,
            Builder.TypeConversion.Convert(dividendType, divisorReference));
        return Builder.TypeConversion.Convert(unsignedType, modulo);
    }

    private static ValueNode ExpectedDividendInitializer(BitWidth width, DataType dividendType,
        RegisterNode low, RegisterNode high) {
        if (width == BitWidth.BYTE_8) {
            return Builder.TypeConversion.Convert(dividendType, Builder.Register.Reg16(RegisterIndex.AxIndex));
        }
        return Builder.CombineHighLowRegisters(high, low, width, dividendType);
    }

    private static void RequireDivisorOperand(SegmentedAddress instructionAddress, ValueNode initializer,
        bool signed, DataType unsignedType, DataType divisorType) {
        if (signed) {
            if (initializer is TypeConversionNode conversion && conversion.DataType == divisorType &&
                IsUnsignedOperand(conversion.Value, unsignedType)) {
                return;
            }
            throw Malformed(instructionAddress, "the divisor initializer is not a signed conversion around a register or memory operand");
        }
        if (!IsUnsignedOperand(initializer, unsignedType)) {
            throw Malformed(instructionAddress, "the divisor initializer is not a register or memory operand");
        }
    }

    private static bool IsUnsignedOperand(ValueNode node, DataType unsignedType) {
        return node is RegisterNode register && register.DataType == unsignedType
            || node is SegmentedPointerNode pointer && pointer.DataType == unsignedType;
    }

    private static NotSupportedException Malformed(SegmentedAddress instructionAddress, string diagnostic) =>
        new($"Malformed division pattern at {instructionAddress}: {diagnostic}.");
}
