namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class CSharpStatementPrinterTest {
    private const string TargetLabel = "L_1000_0010";
    private static readonly SegmentedAddress At = new(0x1000, 0x0010);
    private static readonly RegisterNode Ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
    private readonly ICfgNode _target = Substitute.For<ICfgNode>();

    [Fact]
    public void LabelAndGotoUseTheLabelOfTheTarget() {
        // Arrange
        BlockNode block = new(new LabelNode(_target), new GotoNode(_target));

        // Act
        string printed = Print(block);

        // Assert
        printed.Should().Be(Lines($"{TargetLabel}:", $"goto {TargetLabel};"));
    }

    [Fact]
    public void CyclicJumpReentersTheEntryDispatcher() {
        // Arrange
        BlockNode block = new(new EntryDispatcherLabelNode(), new CyclicJumpNode("loop_1000_0020_10020", 0x0020));

        // Act
        string printed = Print(block);

        // Assert
        printed.Should().Be(Lines(
            "entrydispatcher:",
            "if (JumpDispatcher.Jump(loop_1000_0020_10020, 0x0020)) {",
            "    loadOffset = JumpDispatcher.NextEntryAddress;",
            "    goto entrydispatcher;",
            "}",
            "return JumpDispatcher.RequiredJumpAsmReturn;"));
    }

    [Fact]
    public void BoundaryStatementsPrintTheirHelperCalls() {
        // Arrange
        BlockNode block = new(
            new CommentNode("1000:0010 inc AX"),
            new CheckExternalEventsNode(At),
            new SpeculativeGuardNode(At, "Signature_1000_0010"));

        // Act
        string printed = Print(block);

        // Assert
        printed.Should().Be(Lines(
            "// 1000:0010 inc AX",
            "CheckExternalEvents(cs1, 0x0010);",
            "VerifySpeculativeEntryOrFail(cs1, 0x0010, Signature_1000_0010);"));
    }

    [Fact]
    public void HelperCallsTakeSegmentVariablesOffsetsAndMethodGroups() {
        // Arrange
        BlockNode block = new(
            new MethodCallNode(null, "NearCall", new SegmentVariableNode(0x1000), new OffsetLiteralNode(0x0013),
                new MethodGroupNode("f_1000_0100_10100", 0)),
            new MethodCallNode(null, "FarCall", new SegmentVariableNode(0x1000), new OffsetLiteralNode(0x0018),
                new SegmentVariableNode(0xF000), new MethodGroupNode("g_F000_0000_F0000", 0x0004)),
            new ReturnNode(new MethodCallNode(null, "h_1000_0200_10200", new OffsetLiteralNode(0))));

        // Act
        string printed = Print(block);

        // Assert
        printed.Should().Be(Lines(
            "NearCall(cs1, 0x0013, f_1000_0100_10100);",
            "FarCall(cs1, 0x0018, cs2, _ => g_F000_0000_F0000(0x0004));",
            "return h_1000_0200_10200(0x0000);"));
    }

    [Fact]
    public void SwitchPrintsSectionsWithoutAddingBreak() {
        // Arrange
        FailAsUntestedNode failure = new([UntestedMessagePart.Literal("Untested near jump")]);
        SwitchNode switchNode = new(
            new TypeConversionNode(DataType.UINT16, Ax),
            [
                new SwitchSection(0x0010, new BlockNode(new GotoNode(_target))),
                new SwitchSection(0x0020, new BlockNode(new MethodCallNode(null, "Foo"), new BreakNode()))
            ],
            new BlockNode(failure));

        // Act
        string printed = Print(switchNode);

        // Assert
        printed.Should().Be(Lines(
            "switch (AX) {",
            "    case 0x0010:",
            $"        goto {TargetLabel};",
            "    case 0x0020:",
            "        Foo();",
            "        break;",
            "    default:",
            "        throw FailAsUntested(\"Untested near jump\");",
            "}"));
    }

    [Fact]
    public void IfWithEmptyFalseBlockPrintsNoElse() {
        // Arrange
        BinaryOperationNode csMatches = new(DataType.BOOL,
            new SegmentRegisterNode((int)SegmentRegisterIndex.CsIndex), BinaryOperation.EQUAL, new SegmentVariableNode(0x1000));
        BinaryOperationNode ipMatches = new(DataType.BOOL,
            new InstructionPointerNode(), BinaryOperation.EQUAL, new OffsetLiteralNode(0x0010));
        IfElseNode ifElse = new(new BinaryOperationNode(DataType.BOOL, csMatches, BinaryOperation.LOGICAL_AND, ipMatches),
            new GotoNode(_target), new BlockNode());

        // Act
        string printed = Print(ifElse);

        // Assert
        printed.Should().Be(Lines(
            "if (CS == cs1 && IP == 0x0010) {",
            $"    goto {TargetLabel};",
            "}"));
    }

    [Fact]
    public void IfWithFalseStatementsPrintsElse() {
        // Arrange
        MethodCallValueNode signatureMatches = new(DataType.BOOL, null, "SelectorSignatureMatches",
            new SegmentVariableNode(0x1000), new OffsetLiteralNode(0x0010), new SignatureFieldNode("Signature_1000_0010"));
        FailAsUntestedNode failure = new([UntestedMessagePart.Literal($"Untested code variant at {At}")]);
        IfElseNode ifElse = new(signatureMatches, new GotoNode(_target), new BlockNode(failure));

        // Act
        string printed = Print(ifElse);

        // Assert
        printed.Should().Be(Lines(
            "if (SelectorSignatureMatches(cs1, 0x0010, Signature_1000_0010)) {",
            $"    goto {TargetLabel};",
            "}",
            "else {",
            "    throw FailAsUntested(\"Untested code variant at 1000:0010\");",
            "}"));
    }

    [Fact]
    public void WhilePrintsConditionAndBody() {
        // Arrange
        RegisterNode cx = new(DataType.UINT16, (int)RegisterIndex.CxIndex);
        WhileNode whileNode = new(new BinaryOperationNode(DataType.BOOL, cx, BinaryOperation.NOT_EQUAL, new ConstantNode(DataType.UINT16, 0)),
            new BlockNode(new BreakNode()));

        // Act
        string printed = Print(whileNode);

        // Assert
        printed.Should().Be(Lines(
            "while (CX != 0) {",
            "    break;",
            "}"));
    }

    [Fact]
    public void CpuFaultTryCatchDispatchesToTheHandlers() {
        // Arrange
        CpuFaultTryCatchNode tryCatch = new(new BlockNode(new MethodCallNode(null, "Foo")), At,
            [new CpuFaultHandler(new SegmentedAddress(0xF000, 0x0000), "handler_F000_0000_F0000", 0)]);

        // Act
        string printed = Print(tryCatch);

        // Assert
        printed.Should().Be(Lines(
            "try {",
            "    Foo();",
            "}",
            "catch (CpuException cpuException) {",
            "    return DispatchCpuFault(cpuException, cs1, 0x0010, [new CpuFaultTarget(new SegmentedAddress(cs2, 0x0000), () => handler_F000_0000_F0000(0x0000))]);",
            "}"));
    }

    [Fact]
    public void PlainUntestedMessagePrintsAPlainString() {
        // Arrange
        FailAsUntestedNode failure = new([UntestedMessagePart.Literal($"Untested jump at {At}")]);

        // Act
        string printed = Print(failure);

        // Assert
        printed.Should().Be(Lines("throw FailAsUntested(\"Untested jump at 1000:0010\");"));
    }

    [Fact]
    public void NearTargetHolePrintsTheCastExpression() {
        // Arrange
        TypeConversionNode target = new(DataType.UINT16,
            new BinaryOperationNode(DataType.UINT16, Ax, BinaryOperation.PLUS, new ConstantNode(DataType.UINT16, 2)));
        FailAsUntestedNode failure = new([
            UntestedMessagePart.Literal("Untested near jump target 0x"),
            UntestedMessagePart.Hole(target, "X4"),
            UntestedMessagePart.Literal($" at {At}")
        ]);

        // Act
        string printed = Print(failure);

        // Assert
        printed.Should().Be(Lines("throw FailAsUntested($\"Untested near jump target 0x{(ushort)(AX + 2):X4} at 1000:0010\");"));
    }

    [Fact]
    public void FarTargetHolesUseTheBoundarySuffix() {
        // Arrange
        FailAsUntestedNode failure = new([
            UntestedMessagePart.Literal("Untested far jump target "),
            UntestedMessagePart.Hole(new VariableReferenceNode(DataType.UINT16, "targetSegment"), "X4"),
            UntestedMessagePart.Literal(":"),
            UntestedMessagePart.Hole(new VariableReferenceNode(DataType.UINT16, "targetOffset"), "X4"),
            UntestedMessagePart.Literal($" at {At}")
        ]);
        InstructionBoundaryNode boundary = new(_target, "0010", new BlockNode(failure));

        // Act
        string printed = Print(boundary);

        // Assert
        printed.Should().Be(Lines("throw FailAsUntested($\"Untested far jump target {targetSegment_0010:X4}:{targetOffset_0010:X4} at 1000:0010\");"));
    }

    [Fact]
    public void EntryOffsetHolePrintsTheParameter() {
        // Arrange
        FailAsUntestedNode failure = new([
            UntestedMessagePart.Literal("Untested entry offset 0x"),
            UntestedMessagePart.Hole(new LoadOffsetParameterNode(), "X4")
        ]);

        // Act
        string printed = Print(failure);

        // Assert
        printed.Should().Be(Lines("throw FailAsUntested($\"Untested entry offset 0x{loadOffset:X4}\");"));
    }

    [Fact]
    public void CallbackTargetHolesPrintCsAndIp() {
        // Arrange
        FailAsUntestedNode failure = new([
            UntestedMessagePart.Literal("Untested callback target "),
            UntestedMessagePart.Hole(new SegmentRegisterNode((int)SegmentRegisterIndex.CsIndex), "X4"),
            UntestedMessagePart.Literal(":"),
            UntestedMessagePart.Hole(new InstructionPointerNode(), "X4"),
            UntestedMessagePart.Literal($" at {At}")
        ]);

        // Act
        string printed = Print(failure);

        // Assert
        printed.Should().Be(Lines("throw FailAsUntested($\"Untested callback target {CS:X4}:{IP:X4} at 1000:0010\");"));
    }

    [Fact]
    public void InstructionBoundaryScopesTheLocalVariableSuffix() {
        // Arrange
        VariableDeclarationNode declaration = new(DataType.UINT16, "result", Ax);
        BlockNode block = new(
            new InstructionBoundaryNode(_target, "0010", new BlockNode(declaration, new MethodCallNode(null, "Use", declaration.Reference))),
            new MethodCallNode(null, "Use", declaration.Reference));

        // Act
        string printed = Print(block);

        // Assert
        printed.Should().Be(Lines(
            "ushort result_0010 = AX;",
            "Use(result_0010);",
            "Use(result);"));
    }

    [Fact]
    public void DivisionPrintsOneTupleAssignment() {
        // Arrange
        RegisterNode dx = new(DataType.UINT16, (int)RegisterIndex.DxIndex);
        RegisterNode bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
        VariableReferenceNode dividend = new(DataType.UINT32, "dividend");
        DivisionNode unsigned = new(new DivisionPattern(BitWidth.WORD_16, false, dividend, bx, Ax, dx));
        DivisionNode signed = new(new DivisionPattern(BitWidth.WORD_16, true, dividend, bx, Ax, dx));

        // Act
        string printed = Print(new BlockNode(unsigned, signed));

        // Assert
        printed.Should().Be(Lines(
            "(AX, DX) = Div16(divisor: BX, dividend: (uint)dividend);",
            "(AX, DX) = IDiv16(divisor: (short)BX, dividend: (int)dividend);"));
    }

    [Fact]
    public void ExpressionPrinterRejectsStatementNodes() {
        // Arrange
        CSharpExpressionPrinter expressions = CreateExpressionPrinter();

        // Act
        Action print = () => expressions.Print(new GotoNode(_target));

        // Assert
        print.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void UnknownSegmentVariableIsAGeneratorBug() {
        // Arrange
        CSharpExpressionPrinter expressions = CreateExpressionPrinter();

        // Act
        Action print = () => expressions.Print(new SegmentVariableNode(0x2000));

        // Assert
        print.Should().Throw<InvalidOperationException>().WithMessage("*0x2000*");
    }

    [Fact]
    public void RuntimeVisitorsCannotVisitCodeGenerationNodes() {
        // Arrange
        IAstVisitor<int> runtimeVisitor = Substitute.For<IAstVisitor<int>>();

        // Act
        Action visit = () => new BreakNode().Accept(runtimeVisitor);

        // Assert
        visit.Should().Throw<NotSupportedException>();
    }

    private string Print(IVisitableAstNode node) {
        CSharpSourceWriter writer = new();
        CSharpStatementPrinter printer = new(writer, CreateExpressionPrinter(), LabelOf);
        printer.Print(node);
        return writer.ToString();
    }

    private static CSharpExpressionPrinter CreateExpressionPrinter() =>
        new(new Dictionary<ushort, string> { [0x1000] = "cs1", [0xF000] = "cs2" });

    private string LabelOf(ICfgNode node) {
        if (!ReferenceEquals(node, _target)) {
            throw new InvalidOperationException("Unexpected goto target.");
        }
        return TargetLabel;
    }

    private static string Lines(params string[] lines) =>
        string.Join(Environment.NewLine, lines) + Environment.NewLine;
}
