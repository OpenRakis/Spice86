namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;
using Spice86.Tests.CfgCpu.Ast;

using System.Linq;

using Xunit;

/// <summary>
/// Locks the division recognizer contract: the parser's six-statement DIV/IDIV block is recognized
/// structurally (never by rendered text), candidates that violate a single invariant fail loudly with the
/// instruction address, everything else keeps normal lowering, and each parsed form compiles to exactly one
/// helper invocation that reads the divisor argument first.
/// </summary>
public class DivisionPatternTest {
    private static readonly SegmentedAddress InstructionAddress = new(0x1234, 0x0042);

    public static TheoryData<byte[], BitWidth, bool, bool> ParsedDivisionForms => new() {
        { [0xF6, 0xF3], BitWidth.BYTE_8, false, false },
        { [0xF6, 0x34], BitWidth.BYTE_8, false, true },
        { [0xF6, 0xFB], BitWidth.BYTE_8, true, false },
        { [0xF6, 0x3C], BitWidth.BYTE_8, true, true },
        { [0xF7, 0xF3], BitWidth.WORD_16, false, false },
        { [0xF7, 0x34], BitWidth.WORD_16, false, true },
        { [0xF7, 0xFB], BitWidth.WORD_16, true, false },
        { [0xF7, 0x3C], BitWidth.WORD_16, true, true },
        { [0x66, 0xF7, 0xF3], BitWidth.DWORD_32, false, false },
        { [0x66, 0xF7, 0x34], BitWidth.DWORD_32, false, true },
        { [0x66, 0xF7, 0xFB], BitWidth.DWORD_32, true, false },
        { [0x66, 0xF7, 0x3C], BitWidth.DWORD_32, true, true }
    };

    public static TheoryData<byte[], string> CompilableDivisionForms => new() {
        { [0xF6, 0xF3], "Div8" },
        { [0xF6, 0xFB], "IDiv8" },
        { [0xF7, 0xF3], "Div16" },
        { [0xF7, 0xFB], "IDiv16" },
        { [0x66, 0xF7, 0xF3], "Div32" },
        { [0x66, 0xF7, 0xFB], "IDiv32" }
    };

    [Theory]
    [MemberData(nameof(ParsedDivisionForms))]
    public void ParsedDivisionFormsAreRecognized(byte[] bytes, BitWidth width, bool signed, bool memoryOperand) {
        BlockNode block = DivisionBlock(bytes);

        DivisionPattern matched = Match(block);

        matched.Width.Should().Be(width);
        matched.Signed.Should().Be(signed);
        (RegisterNode low, RegisterNode high) = ExpectedRegisters(width);
        matched.Low.Should().Be(low);
        matched.High.Should().Be(high);
        ValueNode divisorOperand = DivisorOperand(matched, signed);
        divisorOperand.Should().BeOfType(memoryOperand ? typeof(SegmentedPointerNode) : typeof(RegisterNode));
        VariableDeclarationNode divisorDeclaration = (VariableDeclarationNode)block.Statements[0];
        VariableDeclarationNode dividendDeclaration = (VariableDeclarationNode)block.Statements[1];
        ReferenceEquals(matched.Divisor, divisorDeclaration.Initializer)
            .Should().BeTrue("the matcher must return the parsed divisor expression without rebuilding it");
        ReferenceEquals(matched.Dividend, dividendDeclaration.Initializer)
            .Should().BeTrue("the matcher must return the parsed dividend expression without rebuilding it");
        BinaryOperationNode quotientAssignment = (BinaryOperationNode)block.Statements[3];
        BinaryOperationNode remainderAssignment = (BinaryOperationNode)block.Statements[4];
        ReferenceEquals(matched.Low, quotientAssignment.Left).Should().BeTrue();
        ReferenceEquals(matched.High, remainderAssignment.Left).Should().BeTrue();
    }

    [Fact]
    public void PrefixedMemoryOperandKeepsTheParsedSegmentedPointer() {
        BlockNode block = DivisionBlock(0x64, 0x67, 0x66, 0xF7, 0x74, 0x88, 0x10);

        DivisionPattern matched = Match(block);

        matched.Width.Should().Be(BitWidth.DWORD_32);
        matched.Signed.Should().BeFalse();
        matched.Divisor.Should().BeOfType<SegmentedPointerNode>();
        SegmentedPointerNode pointer = (SegmentedPointerNode)matched.Divisor;
        pointer.Segment.Should().BeOfType<SegmentRegisterNode>()
            .Which.RegisterIndex.Should().Be((int)SegmentRegisterIndex.FsIndex);
        pointer.Offset.Should().BeOfType<BinaryOperationNode>(
            "the SIB address computation must be retained rather than evaluated or rebuilt");
        VariableDeclarationNode divisorDeclaration = (VariableDeclarationNode)block.Statements[0];
        ReferenceEquals(matched.Divisor, divisorDeclaration.Initializer).Should().BeTrue();
    }

    [Fact]
    public void NonBlockNodeIsNotACandidate() {
        MoveIpNextNode node = new(new ConstantNode(DataType.UINT32, 0));

        DivisionPattern.TryMatch(node, InstructionAddress).Should().BeNull();
    }

    [Fact]
    public void NopBlockIsNotACandidate() {
        IVisitableAstNode node = ParsedExecutionAst.Of(0x90);

        DivisionPattern.TryMatch(node, InstructionAddress).Should().BeNull();
    }

    [Fact]
    public void MulBlockIsNotACandidate() {
        IVisitableAstNode node = ParsedExecutionAst.Of(0xF6, 0xE3);

        DivisionPattern.TryMatch(node, InstructionAddress).Should().BeNull();
    }

    [Fact]
    public void ImulBlockIsNotACandidate() {
        IVisitableAstNode node = ParsedExecutionAst.Of(0xF6, 0xEB);

        DivisionPattern.TryMatch(node, InstructionAddress).Should().BeNull();
    }

    [Fact]
    public void DeclarationsInADifferentOrderAreNotACandidate() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        IVisitableAstNode[] statements = CopyOf(division);
        (statements[0], statements[1]) = (statements[1], statements[0]);

        DivisionPattern.TryMatch(new BlockNode(statements), InstructionAddress).Should().BeNull();
    }

    [Fact]
    public void MissingStatementIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        IVisitableAstNode[] shortened = new IVisitableAstNode[5];
        Array.Copy(CopyOf(division), shortened, shortened.Length);

        NotSupportedException exception = AssertMalformed(new BlockNode(shortened));

        exception.Message.Should().Contain("instead of six");
    }

    [Fact]
    public void ExtraStatementIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        IVisitableAstNode[] statements = CopyOf(division);
        IVisitableAstNode[] extended = new IVisitableAstNode[statements.Length + 1];
        Array.Copy(statements, extended, statements.Length);
        extended[^1] = statements[^1];

        NotSupportedException exception = AssertMalformed(new BlockNode(extended));

        exception.Message.Should().Contain("instead of six");
    }

    [Fact]
    public void WrongFinalNodeIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);

        NotSupportedException exception = AssertMalformed(Changing(division, 5, new ConstantNode(DataType.UINT32, 0)));

        exception.Message.Should().Contain("final statement is not a MoveIpNextNode");
    }

    [Fact]
    public void UnknownAluPropertyIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        VariableDeclarationNode quotient = (VariableDeclarationNode)division.Statements[2];
        VariableDeclarationNode dividend = (VariableDeclarationNode)division.Statements[1];
        VariableDeclarationNode divisor = (VariableDeclarationNode)division.Statements[0];
        MethodCallValueNode unknownProperty = new(quotient.DataType, "Alu64", "Div",
            dividend.Reference, divisor.Reference);

        NotSupportedException exception = AssertMalformed(
            Changing(division, 2, new VariableDeclarationNode(quotient.DataType, "quotient", unknownProperty)));

        exception.Message.Should().Contain("unknown ALU property path 'Alu64'");
    }

    [Fact]
    public void UnknownAluMethodIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        VariableDeclarationNode quotient = (VariableDeclarationNode)division.Statements[2];
        VariableDeclarationNode dividend = (VariableDeclarationNode)division.Statements[1];
        VariableDeclarationNode divisor = (VariableDeclarationNode)division.Statements[0];
        MethodCallValueNode unknownMethod = new(quotient.DataType, "Alu8", "Mul",
            dividend.Reference, divisor.Reference);

        NotSupportedException exception = AssertMalformed(
            Changing(division, 2, new VariableDeclarationNode(quotient.DataType, "quotient", unknownMethod)));

        exception.Message.Should().Contain("unknown ALU method 'Mul'");
    }

    [Fact]
    public void TooFewAluArgumentsIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        VariableDeclarationNode quotient = (VariableDeclarationNode)division.Statements[2];
        VariableDeclarationNode dividend = (VariableDeclarationNode)division.Statements[1];
        MethodCallValueNode singleArgument = new(quotient.DataType, "Alu8", "Div", dividend.Reference);

        NotSupportedException exception = AssertMalformed(
            Changing(division, 2, new VariableDeclarationNode(quotient.DataType, "quotient", singleArgument)));

        exception.Message.Should().Contain("argument count is 1 instead of two");
    }

    [Fact]
    public void TooManyAluArgumentsIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        VariableDeclarationNode quotient = (VariableDeclarationNode)division.Statements[2];
        VariableDeclarationNode dividend = (VariableDeclarationNode)division.Statements[1];
        VariableDeclarationNode divisor = (VariableDeclarationNode)division.Statements[0];
        MethodCallValueNode threeArguments = new(quotient.DataType, "Alu8", "Div",
            dividend.Reference, dividend.Reference, divisor.Reference);

        NotSupportedException exception = AssertMalformed(
            Changing(division, 2, new VariableDeclarationNode(quotient.DataType, "quotient", threeArguments)));

        exception.Message.Should().Contain("argument count is 3 instead of two");
    }

    [Fact]
    public void SwappedAluArgumentsAreMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        VariableDeclarationNode quotient = (VariableDeclarationNode)division.Statements[2];
        VariableDeclarationNode dividend = (VariableDeclarationNode)division.Statements[1];
        VariableDeclarationNode divisor = (VariableDeclarationNode)division.Statements[0];
        MethodCallValueNode swapped = new(quotient.DataType, "Alu8", "Div",
            divisor.Reference, dividend.Reference);

        NotSupportedException exception = AssertMalformed(
            Changing(division, 2, new VariableDeclarationNode(quotient.DataType, "quotient", swapped)));

        exception.Message.Should().Contain("first ALU argument is not the dividend declaration reference");
    }

    [Fact]
    public void UnrelatedAluArgumentIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        VariableDeclarationNode quotient = (VariableDeclarationNode)division.Statements[2];
        VariableDeclarationNode divisor = (VariableDeclarationNode)division.Statements[0];
        VariableReferenceNode unrelated = new(DataType.UINT16, "cx");
        MethodCallValueNode withUnrelated = new(quotient.DataType, "Alu8", "Div",
            unrelated, divisor.Reference);

        NotSupportedException exception = AssertMalformed(
            Changing(division, 2, new VariableDeclarationNode(quotient.DataType, "quotient", withUnrelated)));

        exception.Message.Should().Contain("first ALU argument is not the dividend declaration reference");
    }

    [Fact]
    public void WrongDivisorDeclarationTypeIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        VariableDeclarationNode divisor = (VariableDeclarationNode)division.Statements[0];

        NotSupportedException exception = AssertMalformed(Changing(division, 0,
            new VariableDeclarationNode(DataType.INT16, "divisor", divisor.Initializer)));

        exception.Message.Should().Contain("divisor declaration type is INT16 instead of UINT8");
    }

    [Fact]
    public void WrongDividendDeclarationTypeIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        VariableDeclarationNode dividend = (VariableDeclarationNode)division.Statements[1];

        NotSupportedException exception = AssertMalformed(Changing(division, 1,
            new VariableDeclarationNode(DataType.UINT32, "dividend", dividend.Initializer)));

        exception.Message.Should().Contain("dividend declaration type is UINT32 instead of UINT16");
    }

    [Fact]
    public void WrongDividendInitializerTypeIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);

        NotSupportedException exception = AssertMalformed(Changing(division, 1,
            new VariableDeclarationNode(DataType.UINT16, "dividend", new ConstantNode(DataType.UINT32, 0))));

        exception.Message.Should().Contain("dividend initializer type is UINT32 instead of UINT16");
    }

    [Fact]
    public void WrongAluMethodResultTypeIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        VariableDeclarationNode quotient = (VariableDeclarationNode)division.Statements[2];
        VariableDeclarationNode dividend = (VariableDeclarationNode)division.Statements[1];
        VariableDeclarationNode divisor = (VariableDeclarationNode)division.Statements[0];
        MethodCallValueNode wrongResultType = new(DataType.UINT16, "Alu8", "Div",
            dividend.Reference, divisor.Reference);

        NotSupportedException exception = AssertMalformed(
            Changing(division, 2, new VariableDeclarationNode(quotient.DataType, "quotient", wrongResultType)));

        exception.Message.Should().Contain("ALU method result type is UINT16 instead of UINT8");
    }

    [Fact]
    public void QuotientAssignmentUsingTheDivisorReferenceIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        VariableDeclarationNode divisor = (VariableDeclarationNode)division.Statements[0];
        AstBuilder builder = new();
        IVisitableAstNode wrongAssignment = builder.AssignWithConversion(DataType.UINT8,
            new RegisterNode(DataType.UINT8, (int)RegisterIndex.AxIndex), divisor.Reference);

        NotSupportedException exception = AssertMalformed(Changing(division, 3, wrongAssignment));

        exception.Message.Should().Contain("statement 4 does not assign the quotient reference");
    }

    [Fact]
    public void QuotientAssignmentToTheWrongRegisterIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        BinaryOperationNode assignment = (BinaryOperationNode)division.Statements[3];
        BinaryOperationNode wrongTarget = new(assignment.DataType,
            new RegisterNode(DataType.INT8, (int)RegisterIndex.AxIndex), BinaryOperation.ASSIGN, assignment.Right);

        NotSupportedException exception = AssertMalformed(Changing(division, 3, wrongTarget));

        exception.Message.Should().Contain("statement 4 does not target the low output register");
    }

    [Fact]
    public void QuotientAssignmentWithTheWrongNodeTypeIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        BinaryOperationNode assignment = (BinaryOperationNode)division.Statements[3];
        BinaryOperationNode wrongType = new(DataType.INT16, assignment.Left, BinaryOperation.ASSIGN, assignment.Right);

        NotSupportedException exception = AssertMalformed(Changing(division, 3, wrongType));

        exception.Message.Should().Contain("statement 4 type is INT16 instead of UINT8");
    }

    [Fact]
    public void RemainderUsingTheWrongVariableIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        VariableDeclarationNode divisor = (VariableDeclarationNode)division.Statements[0];
        VariableDeclarationNode quotient = (VariableDeclarationNode)division.Statements[2];
        BinaryOperationNode remainder = (BinaryOperationNode)division.Statements[4];
        AstBuilder builder = new();
        BinaryOperationNode wrongModulo = new(DataType.UINT16, quotient.Reference, BinaryOperation.MODULO,
            builder.TypeConversion.Convert(DataType.UINT16, divisor.Reference));
        IVisitableAstNode wrongAssignment = builder.AssignWithConversion(DataType.UINT8, remainder.Left,
            builder.TypeConversion.Convert(DataType.UINT8, wrongModulo));

        NotSupportedException exception = AssertMalformed(Changing(division, 4, wrongAssignment));

        exception.Message.Should().Contain("statement 5 does not assign the canonical remainder expression");
    }

    [Fact]
    public void RemainderWithTheWrongOperationIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        VariableDeclarationNode divisor = (VariableDeclarationNode)division.Statements[0];
        VariableDeclarationNode dividend = (VariableDeclarationNode)division.Statements[1];
        BinaryOperationNode remainder = (BinaryOperationNode)division.Statements[4];
        AstBuilder builder = new();
        BinaryOperationNode wrongOperation = new(DataType.UINT16, dividend.Reference, BinaryOperation.PLUS,
            builder.TypeConversion.Convert(DataType.UINT16, divisor.Reference));
        IVisitableAstNode wrongAssignment = builder.AssignWithConversion(DataType.UINT8, remainder.Left,
            builder.TypeConversion.Convert(DataType.UINT8, wrongOperation));

        NotSupportedException exception = AssertMalformed(Changing(division, 4, wrongAssignment));

        exception.Message.Should().Contain("statement 5 does not assign the canonical remainder expression");
    }

    [Fact]
    public void ByteHighResultWrittenToDlInsteadOfAhIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);
        BinaryOperationNode remainder = (BinaryOperationNode)division.Statements[4];
        BinaryOperationNode wrongTarget = new(remainder.DataType, new RegisterNode(DataType.UINT8, 2),
            BinaryOperation.ASSIGN, remainder.Right);

        NotSupportedException exception = AssertMalformed(Changing(division, 4, wrongTarget));

        exception.Message.Should().Contain("statement 5 does not target the high output register");
    }

    [Fact]
    public void ChangedDividendCompositionIsMalformed() {
        BlockNode division = DivisionBlock(0xF7, 0xF3);
        AstBuilder builder = new();
        RegisterNode high = new(DataType.UINT16, (int)RegisterIndex.DxIndex);
        RegisterNode cxAsLowHalf = new(DataType.UINT16, (int)RegisterIndex.CxIndex);
        ValueNode wrongDividend = builder.CombineHighLowRegisters(high, cxAsLowHalf, BitWidth.WORD_16, DataType.UINT32);

        NotSupportedException exception = AssertMalformed(Changing(division, 1,
            new VariableDeclarationNode(DataType.UINT32, "dividend", wrongDividend)));

        exception.Message.Should().Contain("dividend initializer is not the canonical register combination");
    }

    [Fact]
    public void WrongDivisorInitializerTypeIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);

        NotSupportedException exception = AssertMalformed(Changing(division, 0,
            new VariableDeclarationNode(DataType.UINT8, "divisor", new ConstantNode(DataType.UINT16, 1))));

        exception.Message.Should().Contain("divisor initializer type is UINT16 instead of UINT8");
    }

    [Fact]
    public void UnsupportedDivisorOperandShapeIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xF3);

        NotSupportedException exception = AssertMalformed(Changing(division, 0,
            new VariableDeclarationNode(DataType.UINT8, "divisor", new ConstantNode(DataType.UINT8, 1))));

        exception.Message.Should().Contain("divisor initializer is not a register or memory operand");
    }

    [Fact]
    public void SignedDivisorWithoutTheSignedConversionIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xFB);
        VariableDeclarationNode divisor = (VariableDeclarationNode)division.Statements[0];
        TypeConversionNode conversion = (TypeConversionNode)divisor.Initializer;

        NotSupportedException exception = AssertMalformed(Changing(division, 0,
            new VariableDeclarationNode(divisor.DataType, "divisor", conversion.Value)));

        exception.Message.Should().Contain("divisor initializer type is UINT8 instead of INT8");
    }

    [Fact]
    public void SignedDivisorWithoutAnyConversionIsMalformed() {
        BlockNode division = DivisionBlock(0xF6, 0xFB);
        VariableDeclarationNode divisor = (VariableDeclarationNode)division.Statements[0];
        TypeConversionNode conversion = (TypeConversionNode)divisor.Initializer;
        RegisterNode operand = (RegisterNode)conversion.Value;

        NotSupportedException exception = AssertMalformed(Changing(division, 0,
            new VariableDeclarationNode(divisor.DataType, "divisor",
                new RegisterNode(divisor.DataType, operand.RegisterIndex))));

        exception.Message.Should().Contain("divisor initializer is not a signed conversion around a register or memory operand");
    }

    [Theory]
    [MemberData(nameof(CompilableDivisionForms))]
    public void ParsedDivisionFormsCompileToOneHelperInvocationWithDivisorFirst(
        byte[] bytes, string expectedHelper) {
        TestInstructionHelper helper = new();
        helper.State.Flags.CpuModel = CpuModel.INTEL_80386;
        CfgInstruction instruction = helper.WriteAndParse(new SegmentedAddress(0, 0), writer => {
            foreach (byte value in bytes) {
                writer.WriteUInt8(value);
            }
        });
        CfgBlock block = new(100, instruction);
        instruction.ContainingBlock = block;
        CfgCodePartition partition = new() {
            Id = 1,
            Kind = CfgCodePartitionKind.Observed,
            Name = "division",
            Blocks = [block],
            Entries = [
                new CfgCodePartitionEntry {
                    Node = instruction,
                    Kind = CfgCodePartitionEntryKind.FunctionEntry
                }
            ]
        };
        CfgPartitionedProgram program = new() {
            Partitions = [partition],
            Transfers = []
        };

        GeneratedCSharpProgram generatedProgram = new CfgCSharpGenerator().Generate(program);
        GeneratedOverrideCompiler compiler = new();
        GeneratedCompilation compilation = compiler.Compile(generatedProgram.SourceText);
        using CompiledGeneratedOverride compiledOverride = compiler.CompileSupplier(compilation);
        compiledOverride.Supplier.Should().NotBeNull();

        SyntaxNode root = compilation.SyntaxTree.GetRoot(TestContext.Current.CancellationToken);
        List<InvocationExpressionSyntax> directInvocations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText == expectedHelper)
            .ToList();
        directInvocations.Should().HaveCount(1,
            "the body must call the {0} helper exactly once through a bare identifier", expectedHelper);

        List<AssignmentExpressionSyntax> helperAssignments = root.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.Right is InvocationExpressionSyntax right
                && right.Expression is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText == expectedHelper)
            .ToList();
        helperAssignments.Should().HaveCount(1);
        AssignmentExpressionSyntax tupleAssignment = helperAssignments[0];
        if (tupleAssignment.Left is not TupleExpressionSyntax tuple) {
            Assert.Fail($"The right side of {expectedHelper} is not assigned to a tuple.");
            return;
        }
        tuple.Arguments.Count.Should().Be(2, "the quotient and remainder registers are assigned together");

        ArgumentListSyntax argumentList = ((InvocationExpressionSyntax)tupleAssignment.Right).ArgumentList;
        argumentList.Arguments.Count.Should().Be(2);
        string? divisorArgumentName = argumentList.Arguments[0].NameColon?.Name.Identifier.ValueText;
        string? dividendArgumentName = argumentList.Arguments[1].NameColon?.Name.Identifier.ValueText;

        divisorArgumentName.Should().Be("divisor",
            "the divisor must be written first so C# evaluates it before the dividend");
        dividendArgumentName.Should().Be("dividend");
    }

    private static BlockNode DivisionBlock(params byte[] bytes) => (BlockNode)ParsedExecutionAst.Of(bytes);

    private static ValueNode DivisorOperand(DivisionPattern matched, bool signed) {
        if (!signed) {
            matched.Divisor.Should().NotBeOfType<TypeConversionNode>(
                "unsigned division reads the divisor operand without a conversion");
            return matched.Divisor;
        }
        if (matched.Divisor is not TypeConversionNode conversion) {
            throw new InvalidOperationException("Signed division must wrap the divisor operand in a signed conversion.");
        }
        return conversion.Value;
    }

    private static DivisionPattern Match(BlockNode block) {
        DivisionPattern? result = DivisionPattern.TryMatch(block, InstructionAddress);
        result.Should().NotBeNull("a parsed division instruction is a division candidate");
        if (result is null) {
            throw new InvalidOperationException("DivisionPattern.TryMatch returned null for a parsed division.");
        }
        return result;
    }

    private static NotSupportedException AssertMalformed(BlockNode block) {
        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => { DivisionPattern.TryMatch(block, InstructionAddress); });
        exception.Message.Should().Contain("Malformed division pattern at 1234:0042");
        return exception;
    }

    private static BlockNode Changing(BlockNode source, int index, IVisitableAstNode replacement) {
        IVisitableAstNode[] statements = CopyOf(source);
        statements[index] = replacement;
        return new BlockNode(statements);
    }

    private static IVisitableAstNode[] CopyOf(BlockNode source) {
        IVisitableAstNode[] statements = new IVisitableAstNode[source.Statements.Count];
        for (int i = 0; i < statements.Length; i++) {
            statements[i] = source.Statements[i];
        }
        return statements;
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
            _ => throw new ArgumentOutOfRangeException(nameof(width), width, "Unsupported division width")
        };
    }
}
