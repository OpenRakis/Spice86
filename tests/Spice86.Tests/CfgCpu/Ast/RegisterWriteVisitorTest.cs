namespace Spice86.Tests.CfgCpu.Ast;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Xunit;

public class RegisterWriteVisitorTest {
    [Theory]
    [InlineData(new byte[] { 0x89, 0xD0 }, GeneralRegisters.Ax)]
    [InlineData(new byte[] { 0xB0, 0x01 }, GeneralRegisters.Ax)]
    [InlineData(new byte[] { 0xB4, 0x01 }, GeneralRegisters.Ax)]
    [InlineData(new byte[] { 0x66, 0x89, 0xD1 }, GeneralRegisters.Cx)]
    [InlineData(new byte[] { 0x50 }, GeneralRegisters.Sp)]
    [InlineData(new byte[] { 0x58 }, GeneralRegisters.Ax | GeneralRegisters.Sp)]
    [InlineData(new byte[] { 0x39, 0xD8 }, GeneralRegisters.None)]
    [InlineData(new byte[] { 0xF7, 0xE3 }, GeneralRegisters.Ax | GeneralRegisters.Dx)]
    [InlineData(new byte[] { 0xAA }, GeneralRegisters.Di)]
    [InlineData(new byte[] { 0xCD, 0x21 }, GeneralRegisters.All)]
    [InlineData(new byte[] { 0xF3, 0xA6 }, GeneralRegisters.Cx | GeneralRegisters.Si | GeneralRegisters.Di)]
    internal void ReportsTheRegistersAnInstructionWrites(byte[] bytes, GeneralRegisters expected) {
        // Act
        GeneralRegisters written = RegisterWriteVisitor.Of(ParsedExecutionAst.Of(bytes));

        // Assert
        written.Should().Be(expected);
    }
}
