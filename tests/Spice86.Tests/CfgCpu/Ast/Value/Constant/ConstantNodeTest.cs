using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Xunit;

namespace Spice86.Tests.CfgCpu.Ast.Value.Constant;

public class ConstantNodeTest {

    [Fact]
    public void SignedValue_8Bit_Positive() {
        var node = new ConstantNode(DataType.INT8, 0x7F);
        Assert.Equal(127, node.SignedValue);
        Assert.False(node.IsNegative);
    }

    [Fact]
    public void SignedValue_8Bit_Negative() {
        var node = new ConstantNode(DataType.INT8, 0xFF);
        Assert.Equal(-1, node.SignedValue);
        Assert.True(node.IsNegative);
    }

    [Fact]
    public void SignedValue_16Bit_Positive() {
        var node = new ConstantNode(DataType.INT16, 0x7FFF);
        Assert.Equal(32767, node.SignedValue);
        Assert.False(node.IsNegative);
    }

    [Fact]
    public void SignedValue_16Bit_Negative() {
        var node = new ConstantNode(DataType.INT16, 0xFFFF);
        Assert.Equal(-1, node.SignedValue);
        Assert.True(node.IsNegative);
    }

    [Fact]
    public void SignedValue_Nibble_Positive() {
        var node = new ConstantNode(DataType.INT4, 0x7);
        Assert.Equal(7, node.SignedValue);
        Assert.False(node.IsNegative);
    }

    [Fact]
    public void SignedValue_Nibble_Negative() {
        var node = new ConstantNode(DataType.INT4, 0xF);
        Assert.Equal(-1, node.SignedValue);
        Assert.True(node.IsNegative);
    }

    [Fact]
    public void IsNegative_Unsigned_AlwaysFalse() {
        var node = new ConstantNode(DataType.UINT8, 0xFF);
        // Even though 0xFF is -1 in signed 8-bit, for unsigned it is positive 255.
        Assert.False(node.IsNegative);
    }

    [Fact]
    public void Convert_Truncation_16to8() {
        // 0x1234 -> 0x34
        var node = new ConstantNode(DataType.UINT16, 0x1234);
        ulong result = node.Convert(DataType.UINT8);
        Assert.Equal(0x34u, result);
    }

    [Fact]
    public void Convert_ZeroExtend_8to16_Unsigned() {
        // 0xFF -> 0x00FF
        var node = new ConstantNode(DataType.UINT8, 0xFF);
        ulong result = node.Convert(DataType.UINT16);
        Assert.Equal(0x00FFu, result);
    }

    [Fact]
    public void Convert_SignExtend_8to16_Signed() {
        // 0xFF (-1) -> 0xFFFF (-1)
        var node = new ConstantNode(DataType.INT8, 0xFF);
        ulong result = node.Convert(DataType.INT16);
        Assert.Equal(0xFFFFu, result);
    }

    [Fact]
    public void Convert_ZeroExtend_8to16_SignedSource_UnsignedTarget() {
        // 0xFF (signed -1) -> target unsigned 16 bit (0x00FF).
        var node = new ConstantNode(DataType.INT8, 0xFF);
        ulong result = node.Convert(DataType.UINT16);
        Assert.Equal(0x00FFu, result);
    }

    [Fact]
    public void Convert_SignExtend_8to16_UnsignedSource_SignedTarget() {
        // 0xFF (unsigned) -> target signed 16 bit (0x00FF).
        var node = new ConstantNode(DataType.UINT8, 0xFF);
        ulong result = node.Convert(DataType.INT16);
        Assert.Equal(0x00FFu, result);
    }

    [Fact]
    public void Convert_Nibble_To_Byte_Signed() {
        // 0xF (-1 nibble) -> 0xFF (-1 byte)
        var node = new ConstantNode(DataType.INT4, 0xF);
        ulong result = node.Convert(DataType.INT8);
        Assert.Equal(0xFFu, result);
    }

    [Fact]
    public void Convert_Nibble_To_Byte_Unsigned() {
        // 0xF (15 decimal) -> 0x0F (15 decimal)
        var node = new ConstantNode(DataType.UINT4, 0xF);
        ulong result = node.Convert(DataType.UINT8);
        Assert.Equal(0x0Fu, result);
    }
}




