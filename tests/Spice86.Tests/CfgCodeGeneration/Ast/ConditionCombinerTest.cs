namespace Spice86.Tests.CfgCodeGeneration.Ast;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using System.Collections.Generic;

using Xunit;

public class ConditionCombinerTest {
    private static readonly CpuFlagNode Zero = new(Flags.Zero);
    private static readonly CpuFlagNode Carry = new(Flags.Carry);
    private readonly ICfgNode _first = Node(0x10);
    private readonly ICfgNode _second = Node(0x20);
    private readonly ICfgNode _third = Node(0x30);
    private readonly HashSet<IGotoTarget> _jumpTargets = [];

    private static ICfgNode Node(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));

    [Fact]
    public void NestedIfCombinesWithAnd() {
        // Arrange
        BlockNode body = new(
            If(Zero, new BlockNode(If(Carry, new BlockNode(Call("Work"))))));

        // Act
        string printed = Print(ConditionCombiner.Apply(body, _jumpTargets));

        // Assert
        printed.Should().Be(Lines(
            "if (ZeroFlag && CarryFlag) {",
            "    Work();",
            "}"));
    }

    [Fact]
    public void NestedIfCombinesAcrossEmptyBoundaries() {
        // Arrange
        BlockNode body = new(
            If(Zero, new BlockNode(
                Boundary(_first, "first"),
                If(Carry, new BlockNode(Call("Work"))))));

        // Act
        string printed = Print(ConditionCombiner.Apply(body, _jumpTargets));

        // Assert
        printed.Should().Be(Lines(
            "// first",
            "if (ZeroFlag && CarryFlag) {",
            "    Work();",
            "}"));
    }

    [Fact]
    public void NestedIfInsideABoundaryCombinesWhenItReadsOnlyMachineState() {
        // Arrange
        BlockNode body = new(
            If(Zero, new BlockNode(
                Boundary(_first, "first", If(Carry, new BlockNode(Call("Work")))))));

        // Act
        string printed = Print(ConditionCombiner.Apply(body, _jumpTargets));

        // Assert
        printed.Should().Be(Lines(
            "// first",
            "if (ZeroFlag && CarryFlag) {",
            "    Work();",
            "}"));
    }

    [Fact]
    public void SequentialIfsToTheSameGotoTargetCombineWithOr() {
        // Arrange
        BlockNode body = new(
            If(Zero, new BlockNode(new GotoNode(_second))),
            Boundary(_first, "first"),
            If(Carry, new BlockNode(new GotoNode(_second))));

        // Act
        string printed = Print(ConditionCombiner.Apply(body, _jumpTargets));

        // Assert
        printed.Should().Be(Lines(
            "// first",
            "if (ZeroFlag || CarryFlag) {",
            "    goto L_second;",
            "}"));
    }

    [Fact]
    public void SequentialIfsEndingWithBreakCombineWithOr() {
        // Arrange
        BlockNode body = new(
            new InfiniteLoopNode(new BlockNode(
                If(Zero, new BlockNode(new BreakNode())),
                Boundary(_first, "first"),
                If(Carry, new BlockNode(new BreakNode())),
                new GotoNode(_third))));

        // Act
        string printed = Print(ConditionCombiner.Apply(body, _jumpTargets));

        // Assert
        printed.Should().Be(Lines(
            "while (true) {",
            "    // first",
            "    if (ZeroFlag || CarryFlag) {",
            "        break;",
            "    }",
            "    goto L_third;",
            "}"));
    }

    [Fact]
    public void SequentialIfsWithTheSameCopiedStatementsCombine() {
        // Arrange
        FailAsUntestedNode failure = new([UntestedMessagePart.Literal("Untested")]);
        BlockNode arm = new(failure);
        BlockNode body = new(
            If(Zero, arm),
            Boundary(_first, "first"),
            If(Carry, arm));

        // Act
        string printed = Print(ConditionCombiner.Apply(body, _jumpTargets));

        // Assert
        printed.Should().Be(Lines(
            "// first",
            "if (ZeroFlag || CarryFlag) {",
            "    throw FailAsUntested(\"Untested\");",
            "}"));
    }

    [Fact]
    public void AChainOfThreeSequentialIfsCombinesIntoOneCondition() {
        // Arrange
        BlockNode body = new(
            If(Zero, new BlockNode(new GotoNode(_third))),
            Boundary(_first, "first"),
            If(Carry, new BlockNode(new GotoNode(_third))),
            Boundary(_second, "second"),
            If(NotZero(), new BlockNode(new GotoNode(_third))));

        // Act
        string printed = Print(ConditionCombiner.Apply(body, _jumpTargets));

        // Assert
        printed.Should().Be(Lines(
            "// first",
            "// second",
            "if (ZeroFlag || CarryFlag || !ZeroFlag) {",
            "    goto L_third;",
            "}"));
    }

    [Fact]
    public void AStatementBetweenTwoIfsBlocksTheCombination() {
        // Arrange
        BlockNode body = new(
            If(Zero, new BlockNode(new GotoNode(_second))),
            Call("Work"),
            If(Carry, new BlockNode(new GotoNode(_second))));

        // Act
        string printed = Print(ConditionCombiner.Apply(body, _jumpTargets));

        // Assert
        printed.Should().Be(Lines(
            "if (ZeroFlag) {",
            "    goto L_second;",
            "}",
            "Work();",
            "if (CarryFlag) {",
            "    goto L_second;",
            "}"));
    }

    [Fact]
    public void AnElseOnEitherIfBlocksTheCombination() {
        // Arrange
        BlockNode body = new(
            new IfElseNode(Zero, new BlockNode(new GotoNode(_second)), new BlockNode(Call("Else"))),
            If(Carry, new BlockNode(new GotoNode(_second))));

        // Act
        string printed = Print(ConditionCombiner.Apply(body, _jumpTargets));

        // Assert
        printed.Should().Be(Lines(
            "if (ZeroFlag) {",
            "    goto L_second;",
            "}",
            "else {",
            "    Else();",
            "}",
            "if (CarryFlag) {",
            "    goto L_second;",
            "}"));
    }

    [Fact]
    public void DifferentGotoTargetsBlockTheCombination() {
        // Arrange
        BlockNode body = new(
            If(Zero, new BlockNode(new GotoNode(_second))),
            Boundary(_first, "first"),
            If(Carry, new BlockNode(new GotoNode(_third))));

        // Act
        string printed = Print(ConditionCombiner.Apply(body, _jumpTargets));

        // Assert
        printed.Should().Be(Lines(
            "if (ZeroFlag) {",
            "    goto L_second;",
            "}",
            "// first",
            "if (CarryFlag) {",
            "    goto L_third;",
            "}"));
    }

    [Fact]
    public void ANestedIfReadingALocalInsideABoundaryDoesNotMove() {
        // Arrange
        VariableReferenceNode local = new(DataType.UINT16, "local");
        BlockNode body = new(
            If(Zero, new BlockNode(
                Boundary(_first, "first", new IfElseNode(local, new BlockNode(Call("Work")), new BlockNode())))));

        // Act
        string printed = Print(ConditionCombiner.Apply(body, _jumpTargets));

        // Assert
        printed.Should().Be(Lines(
            "if (ZeroFlag) {",
            "    // first",
            "    if (local) {",
            "        Work();",
            "    }",
            "}"));
    }

    [Fact]
    public void SequentialIfsDoNotCombineAcrossAJumpTargetedBoundary() {
        // Arrange: the boundary holding the second if is a goto target.
        _jumpTargets.Add(new CfgNodeGotoTarget(_second));
        BlockNode body = new(
            If(Zero, new BlockNode(new GotoNode(_third))),
            Boundary(_first, "first"),
            Boundary(_second, "second", If(Carry, new BlockNode(new GotoNode(_third)))));

        // Act
        string printed = Print(ConditionCombiner.Apply(body, _jumpTargets));

        // Assert
        printed.Should().Be(Lines(
            "if (ZeroFlag) {",
            "    goto L_third;",
            "}",
            "// first",
            "// second",
            "if (CarryFlag) {",
            "    goto L_third;",
            "}"));
    }

    [Fact]
    public void NestedIfDoesNotCombineWhenAMovedBoundaryIsAJumpTarget() {
        // Arrange: a goto can land on the boundary holding the inner if.
        _jumpTargets.Add(new CfgNodeGotoTarget(_second));
        BlockNode body = new(
            If(Zero, new BlockNode(
                Boundary(_second, "second", If(Carry, new BlockNode(Call("Work")))))));

        // Act
        string printed = Print(ConditionCombiner.Apply(body, _jumpTargets));

        // Assert
        printed.Should().Be(Lines(
            "if (ZeroFlag) {",
            "    // second",
            "    if (CarryFlag) {",
            "        Work();",
            "    }",
            "}"));
    }

    private static UnaryOperationNode NotZero() => new(DataType.BOOL, UnaryOperation.NOT, Zero);

    private static IfElseNode If(ValueNode condition, IVisitableAstNode trueCase) =>
        new(condition, trueCase, new BlockNode());

    private static MethodCallNode Call(string name) => new(null, name);

    private static InstructionBoundaryNode Boundary(ICfgNode node, string name, params IVisitableAstNode[] statements) =>
        new(node, "", new BlockNode([new CommentNode(name), .. statements]));

    private string Print(IVisitableAstNode node) {
        CSharpSourceWriter writer = new();
        CSharpStatementPrinter printer = new(writer, new CSharpExpressionPrinter(new Dictionary<ushort, string>()), LabelOf);
        printer.Print(node);
        return writer.ToString();
    }

    private string LabelOf(IGotoTarget target) {
        ICfgNode node = ((CfgNodeGotoTarget)target).Node;
        if (ReferenceEquals(node, _first)) {
            return "L_first";
        }
        if (ReferenceEquals(node, _second)) {
            return "L_second";
        }
        if (ReferenceEquals(node, _third)) {
            return "L_third";
        }
        throw new InvalidOperationException("Unexpected goto target.");
    }

    private static string Lines(params string[] lines) =>
        string.Join(Environment.NewLine, lines) + Environment.NewLine;
}
