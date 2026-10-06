namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

using Xunit;

public class EmittedCodeRendererTest {
    private static string Render(StatementItem item) {
        CSharpSourceWriter writer = new();
        EmittedCodeRenderer renderer = new(_ => "unused");
        renderer.Render(EmittedCode.Statements(item), writer);
        return writer.ToString();
    }

    private static string Lines(params string[] lines) =>
        string.Join(Environment.NewLine, lines) + Environment.NewLine;

    [Fact]
    public void IfElseWithEmptyFalseBodyRendersNoElse() {
        StatementItem statement = new IfElseStatement("ZeroFlag",
            [new LineStatement("CX = (ushort)0x0001;")],
            []);

        Render(statement).Should().Be(Lines(
            "if (ZeroFlag) {",
            "    CX = (ushort)0x0001;",
            "}"));
    }

    [Fact]
    public void IfElseWithFalseBodyRendersElse() {
        StatementItem statement = new IfElseStatement("ZeroFlag",
            [new LineStatement("CX = (ushort)0x0001;")],
            [new LineStatement("CX = (ushort)0x0002;")]);

        Render(statement).Should().Be(Lines(
            "if (ZeroFlag) {",
            "    CX = (ushort)0x0001;",
            "}",
            "else {",
            "    CX = (ushort)0x0002;",
            "}"));
    }

    [Fact]
    public void IfElseWithEmptyTrueBodyStillRendersIf() {
        StatementItem statement = new IfElseStatement("ZeroFlag",
            [],
            [new LineStatement("CX = (ushort)0x0002;")]);

        Render(statement).Should().Be(Lines(
            "if (ZeroFlag) {",
            "}",
            "else {",
            "    CX = (ushort)0x0002;",
            "}"));
    }

    [Fact]
    public void TryCatchRendersTryThenCatch() {
        StatementItem statement = new TryCatchStatement(
            [new LineStatement("CX = (ushort)0x0001;")],
            "catch (CpuException cpuException)",
            [new LineStatement("throw FailAsUntested(\"x\");", Diverges: true)]);

        Render(statement).Should().Be(Lines(
            "try {",
            "    CX = (ushort)0x0001;",
            "}",
            "catch (CpuException cpuException) {",
            "    throw FailAsUntested(\"x\");",
            "}"));
    }
}