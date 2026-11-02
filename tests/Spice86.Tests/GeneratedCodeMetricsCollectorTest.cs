namespace Spice86.Tests;

using FluentAssertions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using System.Collections.Immutable;

using Xunit;

public class GeneratedCodeMetricsCollectorTest {
    [Fact]
    public void CountsInvocationsOnEveryAluWidth() {
        // Arrange
        string body = """
            Alu8.Sub(AL, 1);
            AX = Alu16.Add(AX, 2);
            Alu32.UpdateFlags(EAX);
            Stack.Push16(AX);
            OtherAlu16.Add(AX, 3);
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.AluCalls.Should().Be(3);
    }

    [Fact]
    public void CountsStatusFlagReadsButNotAssignments() {
        // Arrange
        string body = """
            if (ZeroFlag) { }
            CarryFlag = true;
            CarryFlag = !CarryFlag;
            bool less = SignFlag != OverflowFlag;
            bool parity = State.ParityFlag;
            State.AuxiliaryFlag = AuxiliaryFlag;
            bool notCounted = InterruptFlag && DirectionFlag;
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.StatusFlagReads.Should().Be(6);
    }

    [Fact]
    public void CountsStackPushAndPopCalls() {
        // Arrange
        string body = """
            Stack.Push16(AX);
            Stack.Pop16();
            Stack.Push32(EAX);
            Stack.Pop32();
            Stack.Peek16(0);
            Alu8.Sub(AL, 1);
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.StackCalls.Should().Be(4);
    }

    [Fact]
    public void CountsStatusFlagAssignmentsSeparatelyFromReads() {
        // Arrange
        string body = """
            CarryFlag = true;
            ZeroFlag = AX == 0;
            CarryFlag &= ZeroFlag;
            InterruptFlag = false;
            bool read = ParityFlag;
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.FlagAssignments.Should().Be(3);
        metrics.StatusFlagReads.Should().Be(2);
    }

    [Fact]
    public void CountsOnlyLocalDeclarationsDirectlyInTheMethodBody() {
        // Arrange
        string body = """
            ushort flagOperand_0001 = UInt8[DS, 0x0401];
            if (ZeroFlag) { ushort nested = 1; }
            while (true) { int alsoNested = 2; }
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.MethodVariables.Should().Be(1);
    }

    [Fact]
    public void CountsCompoundAndTupleAssignments() {
        // Arrange
        string body = """
            DX += 1;
            CX -= 2;
            AX &= 0xFF;
            BX |= CX;
            DI ^= 3;
            SI <<= 4;
            DX >>= 1;
            AX /= 2;
            (BX, AX) = (AX, BX);
            AX = BX;
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.CompoundAssignments.Should().Be(7);
        metrics.TupleSwaps.Should().Be(1);
    }

    [Fact]
    public void CountsStructuredStatements() {
        // Arrange
        string body = """
            do { break; } while (CX != 0);
            while (true) { continue; }
            while (CX != 0) { }
            if (ZeroFlag) { } else if (CarryFlag) { }
            switch (AX) { case 1: break; }
            """;

        // Act
        GeneratedCodeMetrics metrics = CollectFromMethodBody(body);

        // Assert
        metrics.DoLoops.Should().Be(1);
        metrics.WhileTrueLoops.Should().Be(1);
        metrics.IfStatements.Should().Be(2);
        metrics.BreakStatements.Should().Be(2);
        metrics.ContinueStatements.Should().Be(1);
    }

    [Fact]
    public void LineNamesEveryValueInAFixedOrder() {
        // Arrange
        GeneratedCodeMetrics metrics = CollectFromMethodBody("""
            L_0001:
            goto L_0001;
            """);

        // Act
        string[] fields = metrics.ToLine().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Assert
        fields.Select(field => field[..field.IndexOf('=')]).Should().Equal("fixture", "methods", "lines", "labels",
            "gotos", "gotoTgts", "extEvents", "specGuard", "untested", "unchecked", "casts", "tries", "aluCalls",
            "flagReads", "doLoops", "whileTrue", "ifs", "breaks", "continues", "stackCalls", "flagAssign",
            "methodVars", "compound", "tupleSwaps", "warnings", "warningIds");
        fields.Should().Contain("fixture=test").And.Contain("labels=1").And.Contain("gotos=1").And.Contain("warningIds=");
    }

    private static GeneratedCodeMetrics CollectFromMethodBody(string body) {
        SyntaxTree tree = CSharpSyntaxTree.ParseText($"class Generated {{ void Method() {{ {body} }} }}");
        return GeneratedCodeMetricsCollector.Collect("test", tree, ImmutableArray<Diagnostic>.Empty);
    }
}
