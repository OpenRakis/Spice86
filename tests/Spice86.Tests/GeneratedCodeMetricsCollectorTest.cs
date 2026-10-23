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
    public void HeaderPlacesNewColumnsBetweenTriesAndWarnings() {
        // Act
        string[] headers = GeneratedCodeMetrics.Header().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Assert
        headers.Should().Equal("fixture", "methods", "lines", "labels", "gotos", "gotoTgts", "extEvents",
            "specGuard", "untested", "unchecked", "casts", "tries", "aluCalls", "flagReads",
            "warnings", "warningIds");
    }

    private static GeneratedCodeMetrics CollectFromMethodBody(string body) {
        SyntaxTree tree = CSharpSyntaxTree.ParseText($"class Generated {{ void Method() {{ {body} }} }}");
        return GeneratedCodeMetricsCollector.Collect("test", tree, ImmutableArray<Diagnostic>.Empty);
    }
}
