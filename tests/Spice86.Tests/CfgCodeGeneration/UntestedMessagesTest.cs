namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public sealed class UntestedMessagesTest {
    private static readonly SegmentedAddress At = new(0xF000, 0x0012);

    [Fact]
    public void IndirectJumpWithoutTargetsKeepsItsText() {
        AssertPrints(UntestedMessages.IndirectWithoutTargets("far jump", At),
            "throw FailAsUntested(\"Untested indirect far jump at F000:0012\");");
    }

    [Fact]
    public void NearTargetParenthesizesAHoleContainingAColon() {
        MethodCallValueNode target = new(DataType.UINT16, null, "global::Foo");

        AssertPrints(UntestedMessages.NearTarget("jump", target, At),
            "throw FailAsUntested($\"Untested near jump target 0x{(global::Foo()):X4} at F000:0012\");");
    }

    private static void AssertPrints(FailAsUntestedNode node, string expected) {
        CSharpSourceWriter writer = new();
        CSharpStatementPrinter printer = new(writer, new CSharpExpressionPrinter(new Dictionary<ushort, string>()),
            _ => throw new InvalidOperationException("No label expected."));

        printer.Print(node);

        writer.ToString().Should().Be(expected + Environment.NewLine);
        StatementSyntax parsed = SyntaxFactory.ParseStatement(expected);
        parsed.GetDiagnostics().Should().BeEmpty();
    }
}
