namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

using Xunit;

/// <summary>
/// Locks in the completion analysis the method emitter relies on to reject a method body
/// that can fall off its end. Completion is computed from the emitted-code structure: a sequence
/// completes normally unless its last item diverges, and a diverging line (<c>return</c>/<c>goto</c>/
/// <c>throw</c>) is what marks the boundary.
/// </summary>
public class EmittedCodeCompletionTest {
    [Fact]
    public void EmptyStatementsCompleteNormally() {
        EmittedCode.None.CompletesNormally.Should().BeTrue();
    }

    [Fact]
    public void ExpressionCompletesNormally() {
        EmittedCode code = (CSharpFragment)"AX";

        code.CompletesNormally.Should().BeTrue();
    }

    [Fact]
    public void PlainLineCompletesNormally() {
        EmittedCode.Line("CX = (ushort)0x0000;").CompletesNormally.Should().BeTrue();
    }

    [Fact]
    public void DivergingLineDoesNotCompleteNormally() {
        EmittedCode.Diverging("return NearRet((ushort)0x0000);").CompletesNormally.Should().BeFalse();
    }

    [Fact]
    public void SequenceCompletionFollowsLastItem() {
        EmittedCode fallsThrough = EmittedCode.Concat(
            EmittedCode.Diverging("goto label_a;"),
            EmittedCode.Line("CX = (ushort)0x0001;"));
        EmittedCode diverges = EmittedCode.Concat(
            EmittedCode.Line("CX = (ushort)0x0001;"),
            EmittedCode.Diverging("goto label_a;"));

        fallsThrough.CompletesNormally.Should().BeTrue("the last item is a plain line");
        diverges.CompletesNormally.Should().BeFalse("the last item diverges");
    }

    [Fact]
    public void BlockCompletesNormallyEvenWhenBodyDiverges() {
        // A bare block (e.g. an `if` without `else`) can always fall through past itself, even when its body diverges.
        EmittedCode code = EmittedCode.Statements(
            new BlockStatement("if (ZeroFlag)", [new LineStatement("goto label_a;", Diverges: true)]));

        code.CompletesNormally.Should().BeTrue();
    }

    [Fact]
    public void SwitchCompletesNormallyWhenACaseBodyBreaks() {
        // A case body that completes normally is followed by an implicit break, so control reaches past the
        // switch through that case.
        EmittedCode code = EmittedCode.Statements(new SwitchStatement(
            "switch ((ushort)(AX))",
            [new SwitchCase("0x0001", [new LineStatement("CX = (ushort)0x0001;")])],
            [new LineStatement("throw FailAsUntested(\"x\");", Diverges: true)]));

        code.CompletesNormally.Should().BeTrue();
    }

    [Fact]
    public void SwitchDoesNotCompleteNormallyWhenEveryBranchDiverges() {
        // When every case body and the default diverge (goto/return/throw) no break is emitted, so the
        // statement following the switch is unreachable: the switch does not complete normally.
        EmittedCode code = EmittedCode.Statements(new SwitchStatement(
            "switch ((ushort)(AX))",
            [new SwitchCase("0x0001", [new LineStatement("goto label_a;", Diverges: true)])],
            [new LineStatement("throw FailAsUntested(\"x\");", Diverges: true)]));

        code.CompletesNormally.Should().BeFalse();
    }

    [Fact]
    public void IfElseCompletesNormallyWhenOnlyTheFalseArmCompletes() {
        EmittedCode code = EmittedCode.Statements(new IfElseStatement("ZeroFlag",
            [new LineStatement("goto label_a;", Diverges: true)],
            [new LineStatement("CX = (ushort)0x0001;")]));

        code.CompletesNormally.Should().BeTrue();
    }

    [Fact]
    public void IfElseCompletesNormallyWhenOnlyTheTrueArmCompletes() {
        EmittedCode code = EmittedCode.Statements(new IfElseStatement("ZeroFlag",
            [new LineStatement("CX = (ushort)0x0001;")],
            [new LineStatement("goto label_a;", Diverges: true)]));

        code.CompletesNormally.Should().BeTrue();
    }

    [Fact]
    public void IfElseWithEmptyFalseBodyCompletesNormally() {
        EmittedCode code = EmittedCode.Statements(new IfElseStatement("ZeroFlag",
            [new LineStatement("goto label_a;", Diverges: true)],
            []));

        code.CompletesNormally.Should().BeTrue();
    }

    [Fact]
    public void IfElseDoesNotCompleteNormallyWhenBothArmsDiverge() {
        EmittedCode code = EmittedCode.Statements(new IfElseStatement("ZeroFlag",
            [new LineStatement("goto label_a;", Diverges: true)],
            [new LineStatement("throw FailAsUntested(\"x\");", Diverges: true)]));

        code.CompletesNormally.Should().BeFalse();
    }

    [Fact]
    public void TryCatchCompletesNormallyWhenTheTryBodyCompletes() {
        EmittedCode code = EmittedCode.Statements(new TryCatchStatement(
            [new LineStatement("CX = (ushort)0x0001;")],
            "catch (CpuException cpuException)",
            [new LineStatement("throw FailAsUntested(\"x\");", Diverges: true)]));

        code.CompletesNormally.Should().BeTrue();
    }

    [Fact]
    public void TryCatchDoesNotCompleteNormallyWhenBothBodiesDiverge() {
        EmittedCode code = EmittedCode.Statements(new TryCatchStatement(
            [new LineStatement("throw FailAsUntested(\"x\");", Diverges: true)],
            "catch (CpuException cpuException)",
            [new LineStatement("return NearRet();", Diverges: true)]));

        code.CompletesNormally.Should().BeFalse();
    }
}
