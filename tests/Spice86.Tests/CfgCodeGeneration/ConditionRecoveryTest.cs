namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Conditions;
using Spice86.Tests.CfgCpu.Ast;

using Xunit;

public class ConditionRecoveryTest {
    [Theory]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x72, "AX < BX")]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x73, "AX >= BX")]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x74, "AX == BX")]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x75, "AX != BX")]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x76, "AX <= BX")]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x77, "AX > BX")]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x7C, "(short)AX < (short)BX")]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x7D, "(short)AX >= (short)BX")]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x7E, "(short)AX <= (short)BX")]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x7F, "(short)AX > (short)BX")]
    [InlineData(new byte[] { 0x83, 0xF8, 0xF9 }, 0x72, "AX < 0xFFF9")]
    [InlineData(new byte[] { 0x83, 0xF8, 0xF9 }, 0x7C, "(short)AX < -7")]
    [InlineData(new byte[] { 0x38, 0xD8 }, 0x72, "AL < BL")]
    [InlineData(new byte[] { 0x38, 0xD8 }, 0x7F, "(sbyte)AL > (sbyte)BL")]
    [InlineData(new byte[] { 0x66, 0x39, 0xD8 }, 0x72, "EAX < EBX")]
    [InlineData(new byte[] { 0x66, 0x39, 0xD8 }, 0x7C, "(int)EAX < (int)EBX")]
    [InlineData(new byte[] { 0x3B, 0x07 }, 0x74, "AX == UInt16[DS, BX]")]
    [InlineData(new byte[] { 0x85, 0xD8 }, 0x74, "(AX & BX) == 0")]
    [InlineData(new byte[] { 0x85, 0xD8 }, 0x75, "(AX & BX) != 0")]
    [InlineData(new byte[] { 0x85, 0xC0 }, 0x74, "AX == 0")]
    [InlineData(new byte[] { 0x85, 0xC0 }, 0x75, "AX != 0")]
    [InlineData(new byte[] { 0xA8, 0x80 }, 0x74, "(AL & 0x80) == 0")]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x78, "(short)(AX - BX) < 0")]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x79, "(short)(AX - BX) >= 0")]
    [InlineData(new byte[] { 0x38, 0xD8 }, 0x78, "(sbyte)(AL - BL) < 0")]
    [InlineData(new byte[] { 0x85, 0xD8 }, 0x78, "(short)(AX & BX) < 0")]
    [InlineData(new byte[] { 0x85, 0xC0 }, 0x79, "(short)AX >= 0")]
    public void RecoversTheRelationOfTheSetterOperands(byte[] setterBytes, byte jumpOpcode, string expected) {
        // Act
        ValueNode recovered = Recover(setterBytes, jumpOpcode)
            ?? throw new InvalidOperationException("expected a relation between the setter operands");

        // Assert
        Print(recovered).Should().Be(expected);
    }

    [Theory]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x70)]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x71)]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x7A)]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x7B)]
    [InlineData(new byte[] { 0x85, 0xD8 }, 0x72)]
    [InlineData(new byte[] { 0x85, 0xD8 }, 0x7C)]
    [InlineData(new byte[] { 0x01, 0xD8 }, 0x74)]
    public void KeepsConditionsWithoutAnExactRelation(byte[] setterBytes, byte jumpOpcode) {
        // Act
        ValueNode? recovered = Recover(setterBytes, jumpOpcode);

        // Assert
        recovered.Should().BeNull();
    }

    [Theory]
    [InlineData(new byte[] { 0x29, 0xD8 }, 0x74, "AX == 0")]
    [InlineData(new byte[] { 0x29, 0xD8 }, 0x75, "AX != 0")]
    [InlineData(new byte[] { 0x29, 0xD8 }, 0x78, "(short)AX < 0")]
    [InlineData(new byte[] { 0x29, 0xD8 }, 0x79, "(short)AX >= 0")]
    [InlineData(new byte[] { 0x01, 0xD8 }, 0x74, "AX == 0")]
    [InlineData(new byte[] { 0x11, 0xD8 }, 0x74, "AX == 0")]
    [InlineData(new byte[] { 0x19, 0xD8 }, 0x75, "AX != 0")]
    [InlineData(new byte[] { 0x20, 0xD8 }, 0x74, "AL == 0")]
    [InlineData(new byte[] { 0x09, 0xD8 }, 0x75, "AX != 0")]
    [InlineData(new byte[] { 0x31, 0xD8 }, 0x74, "AX == 0")]
    [InlineData(new byte[] { 0x40 }, 0x74, "AX == 0")]
    [InlineData(new byte[] { 0x49 }, 0x75, "CX != 0")]
    [InlineData(new byte[] { 0xFE, 0xC0 }, 0x78, "(sbyte)AL < 0")]
    [InlineData(new byte[] { 0x66, 0x49 }, 0x75, "ECX != 0")]
    public void TestsTheAssignedResult(byte[] setterBytes, byte jumpOpcode, string expected) {
        // Act
        ValueNode recovered = RecoverFromResult(setterBytes, jumpOpcode)
            ?? throw new InvalidOperationException("expected a test of the assigned result");

        // Assert
        Print(recovered).Should().Be(expected);
    }

    [Theory]
    [InlineData(new byte[] { 0x29, 0xD8 }, 0x72)]
    [InlineData(new byte[] { 0x29, 0xD8 }, 0x7C)]
    [InlineData(new byte[] { 0x29, 0x07 }, 0x74)]
    [InlineData(new byte[] { 0x39, 0xD8 }, 0x74)]
    [InlineData(new byte[] { 0xD1, 0xE0 }, 0x74)]
    [InlineData(new byte[] { 0xF7, 0xD8 }, 0x74)]
    public void KeepsResultConditionsItCannotExpress(byte[] setterBytes, byte jumpOpcode) {
        // Act
        ValueNode? recovered = RecoverFromResult(setterBytes, jumpOpcode);

        // Assert
        recovered.Should().BeNull();
    }

    private static ValueNode? Recover(byte[] setterBytes, byte jumpOpcode) {
        IVisitableAstNode setter = ParsedExecutionAst.Of(setterBytes);
        IfElseNode jump = (IfElseNode)ParsedExecutionAst.Of(jumpOpcode, 0x00);
        return ConditionRecovery.TryRecover(setter, jump.Condition);
    }

    private static ValueNode? RecoverFromResult(byte[] setterBytes, byte jumpOpcode) {
        IVisitableAstNode setter = ParsedExecutionAst.Of(setterBytes);
        IfElseNode jump = (IfElseNode)ParsedExecutionAst.Of(jumpOpcode, 0x00);
        return ConditionRecovery.TryRecoverFromResult(setter, jump.Condition);
    }

    private static string Print(ValueNode node) =>
        new CSharpExpressionPrinter(new Dictionary<ushort, string>()).Print(node).Text;
}
