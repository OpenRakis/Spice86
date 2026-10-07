namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Generic;

using Xunit;

public sealed class UntestedMessagesTest {
    public static IEnumerable<object[]> NearTargetFormatData => new List<object[]> {
        new object[] { "AX", "throw FailAsUntested($\"Untested near jump target 0x{AX:X4} at F000:0012\");" },
        new object[] { "(ushort)EBX", "throw FailAsUntested($\"Untested near jump target 0x{(ushort)EBX:X4} at F000:0012\");" },
        new object[] { "ZeroFlag ? AX : BX", "throw FailAsUntested($\"Untested near jump target 0x{(ZeroFlag ? AX : BX):X4} at F000:0012\");" },
        new object[] { "global::System.UInt16.MinValue", "throw FailAsUntested($\"Untested near jump target 0x{(global::System.UInt16.MinValue):X4} at F000:0012\");" }
    };

    [Theory]
    [MemberData(nameof(NearTargetFormatData))]
    public void NearTargetFormatsNormalizedExpression(string expression, string expected) {
        SegmentedAddress at = new SegmentedAddress(0xF000, 0x0012);
        string actual = UntestedMessages.NearTarget("jump", expression, at);

        actual.Should().Be(expected);

        // Verify the generated statement is syntactically valid C#.
        StatementSyntax parsed = SyntaxFactory.ParseStatement(actual);
        parsed.GetDiagnostics().Should().BeEmpty();
    }
}