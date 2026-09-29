namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Unit tests for constant literal rendering in <see cref="CSharpAstEmitter"/>.
/// </summary>
public class CSharpAstEmitterConstantTest {
    public static TheoryData<DataType, ulong, string, bool> ConstantLiteralCases => new() {
        // BOOL
        { DataType.BOOL, 0UL, "false", true },
        { DataType.BOOL, 1UL, "true", true },
        
        // UINT8
        { DataType.UINT8, 0UL, "0", true },
        { DataType.UINT8, 9UL, "9", true },
        { DataType.UINT8, 0x0AUL, "0x0A", true },
        { DataType.UINT8, 0xFFUL, "0xFF", true },
        
        // UINT16
        { DataType.UINT16, 0UL, "0", true },
        { DataType.UINT16, 9UL, "9", true },
        { DataType.UINT16, 0x0AUL, "0x000A", true },
        { DataType.UINT16, 0xFFUL, "0x00FF", true },
        { DataType.UINT16, 0xFFFFUL, "0xFFFF", true },
        
        // UINT32
        { DataType.UINT32, 0UL, "0", true },
        { DataType.UINT32, 0x0AUL, "0x0000000A", true },
        { DataType.UINT32, 0x7FFFFFFFUL, "0x7FFFFFFF", true },
        { DataType.UINT32, 0x80000000UL, "0x80000000u", true },
        { DataType.UINT32, 0xFFFFFFFFUL, "0xFFFFFFFFu", true },
        
        // UINT64
        { DataType.UINT64, 1UL, "1UL", true },
        { DataType.UINT64, 0xFFFFFFFFFFFFFFFFUL, "0xFFFFFFFFFFFFFFFFUL", true },
        
        // INT8
        { DataType.INT8, 0xFEUL, "-2", false },
        { DataType.INT8, 0xF7UL, "-9", false },
        { DataType.INT8, 0xF6UL, "-0x0A", false },
        { DataType.INT8, 0x80UL, "-0x80", false },
        { DataType.INT8, 0UL, "0", true },
        { DataType.INT8, 9UL, "9", true },
        { DataType.INT8, 0x0AUL, "0x0A", true },
        { DataType.INT8, 0x7FUL, "0x7F", true },
        
        // INT16
        { DataType.INT16, 0xFFFEUL, "-2", false },
        { DataType.INT16, 0xFF80UL, "-0x0080", false },
        { DataType.INT16, 0x8000UL, "-0x8000", false },
        { DataType.INT16, 0x7FFFUL, "0x7FFF", true },
        
        // INT32
        { DataType.INT32, 0xFFFFFFFEUL, "-2", false },
        { DataType.INT32, 0xFFFFFF80UL, "-0x00000080", false },
        { DataType.INT32, 0x80000000UL, "int.MinValue", true },
        { DataType.INT32, 0x7FFFFFFFUL, "0x7FFFFFFF", true },
        
        // INT64
        { DataType.INT64, 0xFFFFFFFFFFFFFFFFUL, "-1L", false },
        { DataType.INT64, 0UL, "0L", true },
        { DataType.INT64, 0x10UL, "0x0000000000000010L", true },
        { DataType.INT64, 0x8000000000000000UL, "long.MinValue", true }
    };

    [Theory]
    [MemberData(nameof(ConstantLiteralCases))]
    public void ConstantLiteral_RendersCorrectLiteral(DataType dataType, ulong value, string expectedText, bool expectedAtomic) {
        // Arrange
        ConstantNode node = new(dataType, value);

        // Act
        CSharpFragment fragment = CSharpAstEmitter.ConstantLiteral(node);

        // Assert
        fragment.Text.Should().Be(expectedText);
        fragment.Type.Should().Be(dataType);
        fragment.Constant.Should().Be(node);
        (fragment.Precedence == CSharpFragment.AtomicPrecedence).Should().Be(expectedAtomic);
    }
}
